using System;
using System.Collections.Generic;
using System.Linq;

namespace RandWords
{
    /// <summary>Hybrid generation methods.</summary>
    public enum GenerationMode
    {
        /// <summary>Weighted CFG templates filled with real lexicon words.</summary>
        Grammar,
        /// <summary>Statistical word chaining over the built-in seed corpus (or a user corpus).</summary>
        Markov,
        /// <summary>Grammar scaffolding with phoneme-synthesized nonsense words.</summary>
        Invented,
        /// <summary>Learn from user example text (GeneratorOptions.SampleText) and generate
        /// novel words/sentences in its style; pick the learn strategy with SampleStrategy.</summary>
        Sample
    }

    /// <summary>Where content words come from (applies to Grammar mode).</summary>
    public enum WordSource
    {
        RealWords,
        Invented,
        Mixed
    }

    /// <summary>All knobs of the generator.</summary>
    public sealed class GeneratorOptions
    {
        public GenerationMode Mode { get; set; } = GenerationMode.Grammar;
        /// <summary>Null (default) draws OS entropy so every run differs.</summary>
        public ulong? Seed { get; set; }
        public WordSource WordSource { get; set; } = WordSource.RealWords;
        public int MinWords { get; set; } = 3;
        public int MaxWords { get; set; } = 18;
        public int MarkovOrder { get; set; } = 2;
        /// <summary>Custom training sentences for Markov mode; null uses the built-in seed corpus.</summary>
        public IReadOnlyList<string> Corpus { get; set; }
        /// <summary>Substring filters applied to the noun/adjective pools.</summary>
        public IReadOnlyList<string> Themes { get; set; }
        /// <summary>Example text to learn from (required for Sample mode).</summary>
        public string SampleText { get; set; }
        /// <summary>How to learn from the sample: "markov" (adaptive chain), "blend"
        /// (grammar slots filled from the sample vocabulary) or "invent" (new words
        /// weighted by the sample's letter patterns).</summary>
        public string SampleStrategy { get; set; } = "markov";
        /// <summary>0-100: how strongly sample words dominate blend mode (default 60).</summary>
        public int SampleBias { get; set; } = 60;
    }

    /// <summary>One random standalone word with its POS label.</summary>
    public sealed class GeneratedWord
    {
        public string Word { get; }
        public string Pos { get; }

        public GeneratedWord(string word, string pos)
        {
            Word = word;
            Pos = pos;
        }

        public override string ToString() => Word;
    }

    /// <summary>Generate random words that connect like sentences - for AI training data.</summary>
    public sealed class WordGenerator
    {
        /// <summary>Seed of this generator (also stamped on every sentence it produces).</summary>
        public ulong Seed { get; }

        private readonly GenerationMode _mode;
        private GenerationMode _activeMode;
        private readonly string _source;
        private readonly int _minWords;
        private readonly int _maxWords;
        private readonly Rand _rng;
        private readonly Lexicon _lexicon;
        private readonly GrammarEngine _grammar;
        private MarkovChain _markov;

        // Sample-mode state (set when mode is Sample or TrainSample is called).
        private string _sampleStrategy;
        private int _sampleBias;
        private SampleModel _sample;
        private Lexicon _sampleLexicon;
        private GrammarEngine _grammarSample;
        private GrammarEngine _grammarSampleInvent;
        private HashSet<string> _seenSentences;
        private HashSet<string> _seenWords;

        public WordGenerator(GeneratorOptions options = null)
        {
            var o = options ?? new GeneratorOptions();
            _mode = o.Mode;
            _activeMode = o.Mode;
            _minWords = o.MinWords;
            _maxWords = o.MaxWords;
            Seed = o.Seed ?? DrawEntropySeed();
            _rng = new Rand(Seed);
            _lexicon = new Lexicon(o.Themes);
            // Invented mode is the grammar engine with the word source forced to invented.
            _source = o.Mode == GenerationMode.Invented ? "invented" : SourceString(o.WordSource);
            _grammar = new GrammarEngine(_lexicon, _source, o.MinWords, o.MaxWords);
            _markov = new MarkovChain(o.MarkovOrder);
            if (_mode == GenerationMode.Markov)
            {
                IReadOnlyList<string> corpus = o.Corpus ?? LexiconData.MarkovSeedCorpus;
                _markov.Train(corpus);
            }
            // Sample mode: learn from user example text.
            _sampleStrategy = o.SampleStrategy;
            _sampleBias = o.SampleBias;
            if (_mode == GenerationMode.Sample)
            {
                if (string.IsNullOrWhiteSpace(o.SampleText))
                    throw new ArgumentException(
                        "mode 'Sample' requires GeneratorOptions.SampleText");
                InitSample(o.SampleText);
            }
        }

        private static ulong DrawEntropySeed()
        {
            var bytes = new byte[8];
            using (var provider = System.Security.Cryptography.RandomNumberGenerator.Create())
                provider.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }

        private static string SourceString(WordSource source)
        {
            switch (source)
            {
                case WordSource.Invented: return "invented";
                case WordSource.Mixed: return "mixed";
                default: return "real";
            }
        }

        // ------------------------------------------------------------------ //
        //  Markov training                                                   //
        // ------------------------------------------------------------------ //

        /// <summary>(Re)train the Markov chain from a .txt/.json/.jsonl/.csv file.</summary>
        public void TrainCorpus(string path)
        {
            _markov = new MarkovChain(_markov.Order);
            _markov.Train(CorpusLoader.Load(path));
            _activeMode = GenerationMode.Markov;
        }

        /// <summary>(Re)train the Markov chain from an in-memory list of sentences.</summary>
        public void TrainSentences(IReadOnlyList<string> sentences)
        {
            _markov = new MarkovChain(_markov.Order);
            _markov.Train(sentences);
            _activeMode = GenerationMode.Markov;
        }

        /// <summary>(Re)learn from a sample text and switch to Sample mode.</summary>
        public void TrainSample(string text, string strategy = null, int? bias = null)
        {
            if (!string.IsNullOrEmpty(strategy)) _sampleStrategy = strategy;
            if (bias.HasValue) _sampleBias = bias.Value;
            InitSample(text);
            _activeMode = GenerationMode.Sample;
        }

        // ------------------------------------------------------------------ //
        //  Generation                                                        //
        // ------------------------------------------------------------------ //

        /// <summary>One sentence object (text, words, pos tags, rule, seed, mode).</summary>
        public Sentence GenerateSentence()
        {
            if (_activeMode == GenerationMode.Sample)
                return SampleSentence();
            if (_activeMode == GenerationMode.Markov)
            {
                var pair = _markov.Generate(_rng, _maxWords);
                var tags = pair.Item2.Select(_ => "word").ToList();
                return new Sentence(pair.Item1, pair.Item2, tags, "markov", Seed, "markov");
            }
            var mode = _activeMode == GenerationMode.Invented ? "invented" : "grammar";
            return _grammar.Generate(_rng, mode, Seed);
        }

        /// <summary>A batch of sentences (bulk corpora for pretraining).</summary>
        public List<Sentence> GenerateBatch(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0");
            var output = new List<Sentence>(count);
            for (int i = 0; i < count; i++) output.Add(GenerateSentence());
            return output;
        }

        /// <summary>Random standalone words; pos in noun/adjective/verb/adverb/plural/name.</summary>
        public List<GeneratedWord> GenerateWords(int count, string pos = null)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 0");
            var categories = _activeMode == GenerationMode.Sample
                ? _sampleLexicon.ContentCategories()
                : _lexicon.ContentCategories();
            if (pos != null)
            {
                var key = pos.ToLowerInvariant();
                if (!categories.Any(c => c.Key == key))
                    throw new ArgumentException(
                        $"unknown pos '{pos}'; expected one of [{string.Join(", ", categories.Select(c => c.Key))}]");
            }
            var output = new List<GeneratedWord>(count);
            for (int i = 0; i < count; i++)
            {
                string label;
                IReadOnlyList<string> pool;
                if (pos == null)
                {
                    var chosen = categories[_rng.NextInt(categories.Count)];
                    label = chosen.Key;
                    pool = chosen.Value;
                }
                else
                {
                    var chosen = categories.First(c => c.Key == pos.ToLowerInvariant());
                    label = chosen.Key;
                    pool = chosen.Value;
                }
                if (_activeMode == GenerationMode.Sample
                    && _sampleStrategy == "invent"
                    && (label == "noun" || label == "adjective"
                        || label == "verb" || label == "plural"))
                {
                    output.Add(new GeneratedWord(SampleInventWord(label), label));
                }
                else
                {
                    output.Add(new GeneratedWord(NextWord(pool, label), label));
                }
            }
            return output;
        }

        /// <summary>Several sentences joined into one paragraph string.</summary>
        public string GenerateParagraph(int sentenceCount = 4)
        {
            return string.Join(" ", GenerateBatch(sentenceCount).Select(s => s.Text));
        }

        private string NextWord(IReadOnlyList<string> pool, string label)
        {
            bool inventable = label == "noun" || label == "adjective" || label == "verb" || label == "plural";
            if (_source == "invented" && inventable) return Invent(label);
            bool useReal = _source == "real";
            if (_source == "mixed") useReal = inventable ? _rng.Chance(50) : true;
            if (!useReal) return Invent(label);
            return pool[_rng.NextInt(pool.Count)];
        }

        private string Invent(string label)
        {
            var word = Phonemes.NewWord(_rng);
            return label == "plural" ? Phonemes.Pluralize(word) : word;
        }

        // ------------------------------------------------------------------ //
        //  Sample mode internals                                             //
        // ------------------------------------------------------------------ //

        private void InitSample(string text)
        {
            _sample = new SampleModel(text, _lexicon);
            int copies = Math.Max(0, _sampleBias / 20);
            _sampleLexicon = _sample.BlendedLexicon(_lexicon, copies);
            _grammarSample = new GrammarEngine(_sampleLexicon, "real", _minWords, _maxWords);
            _grammarSampleInvent = new GrammarEngine(
                _sampleLexicon, "invented", _minWords, _maxWords, _sample.InventWord);
            _seenSentences = new HashSet<string>();
            _seenWords = new HashSet<string>();
        }

        private Sentence SampleSentence()
        {
            if (_sampleStrategy == "markov")
            {
                var pair = _sample.GenerateSentence(_rng, _maxWords, _seenSentences, _minWords);
                _seenSentences.Add(SampleModel.WordKey(MarkovChain.Tokenize(pair.Item1)));
                var tags = Enumerable.Repeat("word", pair.Item2.Count).ToList();
                return new Sentence(pair.Item1, pair.Item2, tags, "sample/markov", Seed, "sample");
            }
            Sentence s = _sampleStrategy == "invent"
                ? _grammarSampleInvent.Generate(_rng, "sample", Seed)
                : _grammarSample.Generate(_rng, "sample", Seed);
            if (_sample.LowercaseStyle && s.Text != s.Text.ToLowerInvariant())
                s = new Sentence(s.Text.ToLowerInvariant(), s.Words, s.PosTags, s.Rule, s.Seed, s.Mode);
            return s;
        }

        /// <summary>Unique sample-flavored invented word (never one from the sample).</summary>
        private string SampleInventWord(string label)
        {
            string word = "";
            for (int attempt = 0; attempt < 60; attempt++)
            {
                word = _sample.NewWord(_rng);
                if (label == "plural") word = Phonemes.Pluralize(word);
                if (!_seenWords.Contains(word) && !_sample.InVocab(word))
                {
                    _seenWords.Add(word);
                    return word;
                }
            }
            _seenWords.Add(word);
            return word;
        }

        // ------------------------------------------------------------------ //
        //  Datasets: pairs, stats, exports                                   //
        // ------------------------------------------------------------------ //

        /// <summary>Prompt/completion pairs for fine-tuning datasets.</summary>
        public List<TextPair> BuildPairs(IReadOnlyList<Sentence> sentences, int count,
                                         string strategy = "complete")
        {
            return PairBuilder.Build(sentences, _rng, count, strategy);
        }

        /// <summary>Corpus statistics over a batch of sentences.</summary>
        public StatsResult Describe(IReadOnlyList<Sentence> sentences)
        {
            return Stats.Describe(sentences);
        }

        public string ExportTxt(IReadOnlyList<Sentence> sentences, string path)
            => Exporters.ExportTxt(sentences, path);

        public string ExportJsonl(IReadOnlyList<Sentence> sentences, string path)
            => Exporters.ExportJsonl(sentences, path);

        public string ExportCsv(IReadOnlyList<Sentence> sentences, string path)
            => Exporters.ExportCsv(sentences, path);

        public string ExportPairsJsonl(IReadOnlyList<TextPair> pairs, string path,
                                       bool chatFormat = false)
            => Exporters.ExportPairsJsonl(pairs, path, chatFormat);
    }

    /// <summary>Namespace alias mirroring the Python package name.</summary>
    public static class RandWordsCompat
    {
        /// <summary>One-shot convenience: a single sentence string.</summary>
        public static string Sentence(ulong seed = 42, GenerationMode mode = GenerationMode.Grammar)
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = seed, Mode = mode });
            return gen.GenerateSentence().Text;
        }

        /// <summary>One-shot convenience: random standalone word strings.</summary>
        public static List<string> Words(int count = 8, ulong seed = 42)
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = seed });
            return gen.GenerateWords(count).Select(w => w.Word).ToList();
        }

        /// <summary>One-shot convenience: a paragraph of generated sentences.</summary>
        public static string Paragraph(int sentences = 4, ulong seed = 42)
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = seed });
            return gen.GenerateParagraph(sentences);
        }
    }
}
