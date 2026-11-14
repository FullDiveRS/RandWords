using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RandWords.Tests
{
    public class RandTests
    {
        [Fact]
        public void SameSeedSameStream()
        {
            var a = new Rand(42);
            var b = new Rand(42);
            var streamA = Enumerable.Range(0, 100).Select(_ => a.NextU64()).ToList();
            var streamB = Enumerable.Range(0, 100).Select(_ => b.NextU64()).ToList();
            Assert.Equal(streamA, streamB);
        }

        [Fact]
        public void NextIntBounds()
        {
            var rand = new Rand(7);
            for (int i = 0; i < 500; i++)
                Assert.InRange(rand.NextInt(10), 0, 9);
        }

        [Fact]
        public void NextIntInvalidBoundThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Rand(1).NextInt(0));
        }

        [Fact]
        public void WeightedIndexExtremes()
        {
            var rand = new Rand(5);
            Assert.Equal(0, rand.WeightedIndex(new[] { 1 }));
            Assert.Equal(2, rand.WeightedIndex(new[] { 0, 0, 5, 0 }));
        }
    }

    public class ModeTests
    {
        [Fact]
        public void GrammarModeSentenceShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 42 });
            var s = gen.GenerateSentence();
            Assert.False(string.IsNullOrEmpty(s.Text));
            Assert.True(char.IsUpper(s.Text[0]) || s.Text[0] == '"');
            Assert.Equal("grammar", s.Mode);
            Assert.Equal(s.Words.Count, s.PosTags.Count);
            Assert.StartsWith("T", s.Rule);
        }

        [Fact]
        public void AllTemplatesReachable()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 11 });
            var seen = new HashSet<string>();
            for (int i = 0; i < 4000; i++) seen.Add(gen.GenerateSentence().Rule);
            Assert.True(seen.Count >= 29, $"only {seen.Count} templates seen");
        }

        [Fact]
        public void MarkovModeSentenceShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 42, Mode = GenerationMode.Markov });
            var s = gen.GenerateSentence();
            Assert.Equal("markov", s.Mode);
            Assert.Equal("markov", s.Rule);
            Assert.True(s.Words.Count >= 1);
        }

        [Fact]
        public void InventedModeWordsAreNew()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 42, Mode = GenerationMode.Invented });
            var s = gen.GenerateSentence();
            Assert.Matches(@"^[a-z]+$", s.Words[0].ToLowerInvariant());
            var real = new HashSet<string>(LexiconData.Nouns.Concat(LexiconData.Adjectives).Concat(LexiconData.Adverbs));
            Assert.DoesNotContain(s.Words[0].ToLowerInvariant(), real);
        }

        [Fact]
        public void BatchCount()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 1 });
            Assert.Equal(50, gen.GenerateBatch(50).Count);
        }

        [Fact]
        public void LengthFilter()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 3, MinWords = 6, MaxWords = 9 });
            foreach (var s in gen.GenerateBatch(100))
                Assert.InRange(s.Words.Count, 6, 9);
        }

        [Fact]
        public void ThemesFilterRuns()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 8, Themes = new[] { "water", "wind" } });
            Assert.False(string.IsNullOrEmpty(gen.GenerateSentence().Text));
        }

        [Fact]
        public void GenerateWordsByPos()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 2 });
            foreach (var pos in new[] { "noun", "adjective", "verb", "adverb", "plural", "name" })
                foreach (var w in gen.GenerateWords(20, pos))
                    Assert.Equal(pos, w.Pos);
            Assert.Throws<ArgumentException>(() => gen.GenerateWords(1, "nope"));
        }

        [Fact]
        public void GenerateWordsMixedCategories()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 2 });
            Assert.True(gen.GenerateWords(200).Select(w => w.Pos).Distinct().Count() >= 4);
        }

        [Fact]
        public void DeterministicAcrossInstances()
        {
            var opts = new GeneratorOptions { Seed = 123, Mode = GenerationMode.Markov };
            var one = new WordGenerator(Clone(opts)).GenerateBatch(20).Select(s => s.Text).ToList();
            var two = new WordGenerator(Clone(opts)).GenerateBatch(20).Select(s => s.Text).ToList();
            Assert.Equal(one, two);
            var three = new WordGenerator(new GeneratorOptions { Seed = 124, Mode = GenerationMode.Markov })
                .GenerateBatch(20).Select(s => s.Text).ToList();
            Assert.NotEqual(one, three);
        }

        private static GeneratorOptions Clone(GeneratorOptions o)
            => new GeneratorOptions { Seed = o.Seed, Mode = o.Mode };
    }

    public class PairAndStatsTests
    {
        [Fact]
        public void CompletePairsShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 9 });
            var sentences = gen.GenerateBatch(20);
            var pairs = gen.BuildPairs(sentences, 10, "complete");
            Assert.Equal(10, pairs.Count);
            foreach (var (p, i) in pairs.Select((p, i) => (p, i)))
            {
                Assert.Equal(i, p.Id);
                Assert.Equal("complete", p.Strategy);
                Assert.StartsWith("Complete this sentence:", p.Prompt);
                Assert.EndsWith(".", p.Completion);
                Assert.NotEqual(p.Prompt, p.Completion);
            }
        }

        [Fact]
        public void TopicPairsShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 9 });
            var pairs = gen.BuildPairs(gen.GenerateBatch(10), 5, "topic");
            Assert.All(pairs, p => Assert.StartsWith("Write a sentence that mentions", p.Prompt));
        }

        [Fact]
        public void ContinuePairsShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 9 });
            var pairs = gen.BuildPairs(gen.GenerateBatch(10), 5, "continue");
            Assert.All(pairs, p => Assert.False(string.IsNullOrEmpty(p.Completion)));
        }

        [Fact]
        public void UnknownStrategyThrows()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 9 });
            Assert.Throws<ArgumentException>(() =>
                gen.BuildPairs(gen.GenerateBatch(5), 1, "bogus"));
        }

        [Fact]
        public void StatsShape()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 10 });
            var batch = gen.GenerateBatch(30);
            var st = gen.Describe(batch);
            Assert.Equal(30, st.SentenceCount);
            Assert.Equal(batch.Sum(s => s.Words.Count), st.WordCount);
            Assert.InRange(st.TypeTokenRatio, 0.0, 1.0);
            Assert.True(st.MinSentenceLength <= st.AvgSentenceLength);
            Assert.True(st.AvgSentenceLength <= st.MaxSentenceLength);
            Assert.False(string.IsNullOrEmpty(st.LongestSentence));
        }
    }

    public class ExporterTests : IDisposable
    {
        private readonly string _dir;

        public ExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "randwords-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private WordGenerator Gen()
            => new WordGenerator(new GeneratorOptions { Seed = 77 });

        [Fact]
        public void TxtExport()
        {
            var gen = Gen();
            var batch = gen.GenerateBatch(25);
            var path = Path.Combine(_dir, "out.txt");
            gen.ExportTxt(batch, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(25, lines.Length);
            Assert.Equal(batch.Select(s => s.Text), lines);
        }

        [Fact]
        public void JsonlExport()
        {
            var gen = Gen();
            var batch = gen.GenerateBatch(25);
            var path = Path.Combine(_dir, "out.jsonl");
            gen.ExportJsonl(batch, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal(25, lines.Length);
            Assert.Contains("\"sentence\": " + Json.Str(batch[0].Text), lines[0]);
            Assert.Contains("\"rule\": \"" + batch[0].Rule + "\"", lines[0]);
        }

        [Fact]
        public void CsvExport()
        {
            var gen = Gen();
            var batch = gen.GenerateBatch(25);
            var path = Path.Combine(_dir, "out.csv");
            gen.ExportCsv(batch, path);
            var lines = File.ReadAllLines(path);
            Assert.Equal("sentence_id,word_index,word,pos_tag,sentence", lines[0]);
            Assert.Equal(batch.Sum(s => s.Words.Count), lines.Length - 1);
        }

        [Fact]
        public void PairsJsonlAndChat()
        {
            var gen = Gen();
            var pairs = gen.BuildPairs(gen.GenerateBatch(25), 5, "complete");
            var flatPath = Path.Combine(_dir, "pairs.jsonl");
            var chatPath = Path.Combine(_dir, "pairs_chat.jsonl");
            gen.ExportPairsJsonl(pairs, flatPath);
            gen.ExportPairsJsonl(pairs, chatPath, chatFormat: true);
            var flat = File.ReadAllLines(flatPath);
            var chat = File.ReadAllLines(chatPath);
            Assert.Equal(5, flat.Length);
            Assert.Contains("\"prompt\": ", flat[0]);
            Assert.Contains("\"messages\": [{\"role\": \"user\", \"content\": ", chat[0]);
        }
    }

    public class CorpusTests : IDisposable
    {
        private readonly string _dir;

        public CorpusTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "randwords-corpus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Fact]
        public void TxtTraining()
        {
            var path = Path.Combine(_dir, "c.txt");
            File.WriteAllText(path, "the keeper watched the river.\nthe ferry docked quietly.\n");
            var gen = new WordGenerator(new GeneratorOptions { Seed = 13, Mode = GenerationMode.Markov });
            gen.TrainCorpus(path);
            var s = gen.GenerateSentence();
            Assert.Equal("markov", s.Mode);
            Assert.False(string.IsNullOrEmpty(s.Text));
        }

        [Fact]
        public void JsonTraining()
        {
            var path = Path.Combine(_dir, "c.json");
            File.WriteAllText(path, "[\"a sentence here.\", {\"text\": \"another one.\"}]");
            Assert.Equal(2, CorpusLoader.Load(path).Count);
        }

        [Fact]
        public void JsonlTraining()
        {
            var path = Path.Combine(_dir, "c.jsonl");
            File.WriteAllText(path, "{\"sentence\": \"one.\"}\n{\"sentence\": \"two.\"}\n");
            Assert.Equal(new[] { "one.", "two." }, CorpusLoader.Load(path));
        }

        [Fact]
        public void CsvTraining()
        {
            var path = Path.Combine(_dir, "c.csv");
            File.WriteAllText(path, "id,sentence\n1,first.\n2,second.\n");
            Assert.Equal(new[] { "first.", "second." }, CorpusLoader.Load(path));
        }

        [Fact]
        public void UntrainedChainThrows()
        {
            Assert.Throws<InvalidOperationException>(() => new MarkovChain().Generate(new Rand(1), 20));
        }

        [Fact]
        public void MissingFileThrows()
        {
            Assert.Throws<FileNotFoundException>(() => CorpusLoader.Load("/definitely/not/here.txt"));
        }

        [Fact]
        public void TokenizeMirrorsPythonRegex()
        {
            Assert.Equal(new[] { "the", "keeper", "watched", "the", "river", "." },
                MarkovChain.Tokenize("The keeper watched the river."));
            // apostrophe continues a token: regex class is [a-z']+ (same in Python)
            Assert.Equal(new[] { "don't", "!" }, MarkovChain.Tokenize("DON'T!"));
            Assert.Equal(new[] { "what", "a", "restless", "wind", "!" },
                MarkovChain.Tokenize("What a restless wind!"));
        }
    }

    /// <summary>Behavior of SAMPLE mode (learn from user example text).</summary>
    public class SampleTests
    {
        private const string Dialog =
            "\"Oh? thank you very much, please have these cookies i baked! " +
            "i made extra and didn't know what to do with them\", " +
            "\"You liked them? that's so good to hear! No no, i can't " +
            "accept that, please let me help you from now on.\"";

        private static ISet<string> Keys(IEnumerable<Sentence> batch)
        {
            return new HashSet<string>(
                batch.Select(s => SampleModel.WordKey(MarkovChain.Tokenize(s.Text))));
        }

        [Fact]
        public void SampleMarkovSentenceShape()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 42, Mode = GenerationMode.Sample, SampleText = Dialog
            });
            foreach (var s in gen.GenerateBatch(6))
            {
                Assert.Equal("sample", s.Mode);
                Assert.Equal("sample/markov", s.Rule);
                Assert.False(string.IsNullOrEmpty(s.Text));
                Assert.True(s.Words.Count > 0);
                Assert.Equal(s.Words.Count, s.PosTags.Count);
                Assert.Equal(s.Text, s.Text.ToLowerInvariant());  // dialogue style echoed
            }
        }

        [Fact]
        public void SampleSentencesAreUniqueAndNovel()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 42, Mode = GenerationMode.Sample, SampleText = Dialog
            });
            var batch = gen.GenerateBatch(12);
            Assert.Equal(12, Keys(batch).Count);
            var model = new SampleModel(Dialog);
            foreach (var s in batch)
                Assert.DoesNotContain(
                    SampleModel.WordKey(MarkovChain.Tokenize(s.Text)),
                    model.SentenceKeys);
        }

        [Fact]
        public void SampleDeterministic()
        {
            var a = new WordGenerator(new GeneratorOptions
            {
                Seed = 99, Mode = GenerationMode.Sample, SampleText = Dialog
            }).GenerateBatch(10);
            var b = new WordGenerator(new GeneratorOptions
            {
                Seed = 99, Mode = GenerationMode.Sample, SampleText = Dialog
            }).GenerateBatch(10);
            Assert.Equal(a.ConvertAll(s => s.Text), b.ConvertAll(s => s.Text));
        }

        [Fact]
        public void SampleBlendUsesSampleVocab()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 42, Mode = GenerationMode.Sample,
                SampleText = Dialog, SampleStrategy = "blend"
            });
            var joined = string.Join(" ", gen.GenerateBatch(15)
                                            .Select(s => s.Text.ToLowerInvariant()));
            Assert.True(joined.Contains("cookies") || joined.Contains("baked"));
        }

        [Fact]
        public void SampleBiasZeroExcludesVocab()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 3, Mode = GenerationMode.Sample,
                SampleText = Dialog, SampleStrategy = "blend", SampleBias = 0
            });
            var joined = string.Join(" ", gen.GenerateBatch(20)
                                            .Select(s => s.Text.ToLowerInvariant()));
            Assert.DoesNotContain("cookies", joined);
            Assert.DoesNotContain("baked", joined);
            Assert.DoesNotContain("accept", joined);
        }

        [Fact]
        public void SampleInventWordsUniqueAndNovel()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 7, Mode = GenerationMode.Sample,
                SampleText = Dialog, SampleStrategy = "invent"
            });
            var words = gen.GenerateWords(15).ConvertAll(w => w.Word);
            Assert.Equal(words.Count, new HashSet<string>(words).Count);
            var model = new SampleModel(Dialog);
            foreach (var w in words)
                Assert.False(model.InVocab(w.ToLowerInvariant()));
        }

        [Fact]
        public void TrainSampleSwitchesMode()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 42 });
            gen.TrainSample(Dialog);
            Assert.Equal("sample", gen.GenerateSentence().Mode);
        }

        [Fact]
        public void SampleModeRequiresSampleText()
        {
            Assert.Throws<ArgumentException>(() => new WordGenerator(
                new GeneratorOptions { Seed = 1, Mode = GenerationMode.Sample }));
        }

        [Fact]
        public void EmptySampleThrows()
        {
            Assert.Throws<ArgumentException>(() => new SampleModel("   "));
        }

        [Fact]
        public void SingleSentenceSampleWorks()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = 5, Mode = GenerationMode.Sample, SampleText = "hello world today."
            });
            Assert.False(string.IsNullOrEmpty(gen.GenerateSentence().Text));
        }
    }
}
