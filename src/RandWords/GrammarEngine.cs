using System;
using System.Collections.Generic;

namespace RandWords
{
    /// <summary>One generated sentence with word-level metadata.</summary>
    public sealed class Sentence
    {
        public string Text { get; }
        /// <summary>Word tokens only (punctuation excluded); parallel to PosTags.</summary>
        public IReadOnlyList<string> Words { get; }
        /// <summary>POS tags, parallel to Words (det/adj/noun/verb/name/adv/prep/lit/word).</summary>
        public IReadOnlyList<string> PosTags { get; }
        /// <summary>Template rule name (T01...) or "markov".</summary>
        public string Rule { get; }
        /// <summary>Seed of the generator that produced this sentence.</summary>
        public ulong Seed { get; }
        /// <summary>"grammar", "markov" or "invented".</summary>
        public string Mode { get; }

        public Sentence(string text, IReadOnlyList<string> words, IReadOnlyList<string> posTags,
                        string rule, ulong seed, string mode)
        {
            Text = text;
            Words = words;
            PosTags = posTags;
            Rule = rule;
            Seed = seed;
            Mode = mode;
        }
    }

    /// <summary>
    /// Grammar engine: weighted CFG-style templates filled with lexicon words.
    /// SLOT VOCABULARY
    ///   DET determiner   DETP plural-safe determiner   Q literal quote
    ///   ADJ adjective    N singular noun               NP plural noun
    ///   PN proper name   ADV adverb                    PREP preposition
    ///   INNERS nested quote sentence
    ///   VTP transitive past   VIP intransitive past
    ///   V3I intransitive 3rd person present
    ///   VBI intransitive base VBT transitive base
    /// IMPORTANT: every rng call in this class is part of the cross-language
    /// parity contract - the Python GrammarEngine performs the identical sequence.
    /// </summary>
    public sealed class GrammarEngine
    {
        private const string Real = "real";
        private const string Invented = "invented";
        private const string Mixed = "mixed";

        private static readonly string[] Punct = { ",", ".", "?", "!", ";", ":", "\"" };
        private static readonly string Vowels = "aeiou";

        private readonly Lexicon _lex;
        private readonly string _source;
        private readonly int _minWords;
        private readonly int _maxWords;
        // Optional hook (rng, kind_or_form) -> word. When set it replaces the
        // built-in phoneme inventor, e.g. by sample-weighted synthesis. It is
        // consumed with the same RNG draw count, so parity is preserved.
        private readonly Func<Rand, string, string> _inventor;
        private static readonly int[] TemplateWeights =
            Array.ConvertAll(LexiconData.GrammarTemplates, t => t.Weight);

        public GrammarEngine(Lexicon lexicon, string wordSource, int minWords, int maxWords,
                             Func<Rand, string, string> inventor = null)
        {
            _lex = lexicon;
            _source = wordSource;
            _minWords = minWords;
            _maxWords = maxWords;
            _inventor = inventor;
        }

        /// <summary>Generate one sentence; retries up to 8 times if the length filter fails.</summary>
        public Sentence Generate(Rand rng, string mode, ulong seed)
        {
            Sentence sentence = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                sentence = Attempt(rng, mode, seed);
                if (_minWords <= sentence.Words.Count && sentence.Words.Count <= _maxWords)
                    return sentence;
            }
            return sentence;
        }

        private Sentence Attempt(Rand rng, string mode, ulong seed)
        {
            var template = LexiconData.GrammarTemplates[rng.WeightedIndex(TemplateWeights)];
            var tokens = new List<string>();
            var tags = new List<string>();
            Expand(template.Pattern, rng, 0, tokens, tags);
            var words = new List<string>();
            var wtags = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (Phonemes.IsWordToken(tokens[i]))
                {
                    words.Add(tokens[i]);
                    wtags.Add(tags[i]);
                }
            }
            return new Sentence(Render(tokens), words, wtags, template.Name, seed, mode);
        }

        private void Expand(string pattern, Rand rng, int depth, List<string> tokens, List<string> tags)
        {
            foreach (var tok in pattern.Split(' '))
            {
                if (tok.StartsWith("{") && tok.EndsWith("}"))
                {
                    var slot = tok.Substring(1, tok.Length - 2);
                    if (slot == "INNERS")
                    {
                        var inner = LexiconData.InnerTemplates[rng.NextInt(LexiconData.InnerTemplates.Length)];
                        Expand(inner.Pattern, rng, depth + 1, tokens, tags);
                    }
                    else
                    {
                        Resolve(slot, rng, out var word, out var tag);
                        tokens.Add(word);
                        tags.Add(tag);
                    }
                }
                else
                {
                    tokens.Add(tok);
                    tags.Add(Phonemes.IsWordToken(tok) ? "lit" : "punct");
                }
            }
        }

        private void Resolve(string slot, Rand rng, out string word, out string tag)
        {
            switch (slot)
            {
                case "DET":
                    word = _lex.Determiners[rng.NextInt(_lex.Determiners.Count)];
                    tag = "det";
                    break;
                case "DETP":
                    word = _lex.DeterminersPlural[rng.NextInt(_lex.DeterminersPlural.Count)];
                    tag = "det";
                    break;
                case "Q":
                    word = "\"";
                    tag = "punct";
                    break;
                case "ADJ":
                    word = Content(rng, _lex.Adjectives, "adj");
                    tag = "adj";
                    break;
                case "N":
                    word = Content(rng, _lex.Nouns, "noun");
                    tag = "noun";
                    break;
                case "NP":
                    word = Content(rng, _lex.NounsPlural, "plural");
                    tag = "noun";
                    break;
                case "PN":
                    word = _lex.ProperNames[rng.NextInt(_lex.ProperNames.Count)];
                    tag = "name";
                    break;
                case "ADV":
                    word = _lex.Adverbs[rng.NextInt(_lex.Adverbs.Count)];
                    tag = "adv";
                    break;
                case "PREP":
                    word = _lex.Prepositions[rng.NextInt(_lex.Prepositions.Count)];
                    tag = "prep";
                    break;
                case "VTP":
                    word = Verb(rng, _lex.VerbsTransitive, "past");
                    tag = "verb";
                    break;
                case "VIP":
                    word = Verb(rng, _lex.VerbsIntransitive, "past");
                    tag = "verb";
                    break;
                case "V3I":
                    word = Verb(rng, _lex.VerbsIntransitive, "third");
                    tag = "verb";
                    break;
                case "VBI":
                    word = Verb(rng, _lex.VerbsIntransitive, "base");
                    tag = "verb";
                    break;
                case "VBT":
                    word = Verb(rng, _lex.VerbsTransitive, "base");
                    tag = "verb";
                    break;
                default:
                    throw new ArgumentException("unknown grammar slot: " + slot);
            }
        }

        private string Verb(Rand rng, IReadOnlyList<Verb> pool, string form)
        {
            if (_inventor != null) return _inventor(rng, form);
            if (_source == Invented) return InventVerb(rng, form);
            bool useReal = _source == Real;
            if (_source == Mixed) useReal = rng.Chance(50);
            if (!useReal) return InventVerb(rng, form);
            var verb = pool[rng.NextInt(pool.Count)];
            return form == "past" ? verb.Past : form == "third" ? verb.Third : verb.Base;
        }

        private string Content(Rand rng, IReadOnlyList<string> realList, string kind)
        {
            if (_inventor != null) return _inventor(rng, kind);
            if (_source == Invented) return InventWord(rng, kind);
            bool useReal = _source == Real;
            if (_source == Mixed) useReal = rng.Chance(50);
            if (!useReal) return InventWord(rng, kind);
            return realList[rng.NextInt(realList.Count)];
        }

        private static string InventVerb(Rand rng, string form)
        {
            var w = Phonemes.NewWord(rng);
            return form == "third" ? Phonemes.ThirdPerson(w)
                 : form == "past" ? Phonemes.PastTense(w)
                 : w;
        }

        private static string InventWord(Rand rng, string kind)
        {
            var w = Phonemes.NewWord(rng);
            return kind == "plural" ? Phonemes.Pluralize(w) : w;
        }

        // ------------------------------------------------------------------ //
        //  Rendering helpers (pure functions - no RNG)                        //
        // ------------------------------------------------------------------ //

        public static string Render(IReadOnlyList<string> tokens)
        {
            var text = string.Join(" ", FixArticles(tokens));
            foreach (var p in Punct)
                text = text.Replace(" " + p, p);
            text = text.Replace(", \"", ",\"");
            if (text.StartsWith("\" "))
                text = "\"" + text.Substring(2);
            return Capitalize(text);
        }

        private static List<string> FixArticles(IReadOnlyList<string> tokens)
        {
            var result = new List<string>(tokens);
            for (int i = 0; i < result.Count - 1; i++)
            {
                if (result[i] == "a")
                {
                    var next = result[i + 1];
                    var first = '\0';
                    foreach (var ch in next)
                    {
                        if (char.IsLetter(ch)) { first = ch; break; }
                    }
                    if (first != '\0' && Vowels.IndexOf(char.ToLowerInvariant(first)) >= 0)
                        result[i] = "an";
                }
            }
            return result;
        }

        private static string Capitalize(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsLetter(text[i]))
                    return text.Substring(0, i) + char.ToUpperInvariant(text[i]) + text.Substring(i + 1);
            }
            return text;
        }
    }
}
