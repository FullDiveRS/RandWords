using System;
using System.Collections.Generic;

namespace RandWords
{
    /// <summary>One conjugated verb entry ("base|third|past|tag" in the shared data).</summary>
    public sealed class Verb
    {
        public readonly string Base;
        public readonly string Third;
        public readonly string Past;
        /// <summary>"t" transitive, "i" intransitive, "b" both.</summary>
        public readonly string Tag;

        public Verb(string raw)
        {
            var parts = raw.Split('|');
            Base = parts[0];
            Third = parts[1];
            Past = parts[2];
            Tag = parts[3];
        }
    }

    /// <summary>Holds all real-word tables (optionally theme-filtered) and parsed verbs.</summary>
    public sealed class Lexicon
    {
        public IReadOnlyList<string> Determiners { get; }
        public IReadOnlyList<string> DeterminersPlural { get; }
        public IReadOnlyList<string> Conjunctions { get; }
        public IReadOnlyList<string> Prepositions { get; }
        public IReadOnlyList<string> Nouns { get; }
        public IReadOnlyList<string> NounsPlural { get; }
        public IReadOnlyList<string> Adjectives { get; }
        public IReadOnlyList<string> Adverbs { get; }
        public IReadOnlyList<string> ProperNames { get; }

        public IReadOnlyList<Verb> Verbs { get; }
        public IReadOnlyList<Verb> VerbsTransitive { get; }
        public IReadOnlyList<Verb> VerbsIntransitive { get; }
        public IReadOnlyList<string> VerbBases { get; }

        public Lexicon(IReadOnlyList<string> themes = null)
        {
            var t = FilterThemes(themes);
            Determiners = LexiconData.Determiners;
            DeterminersPlural = LexiconData.DeterminersPlural;
            Conjunctions = LexiconData.Conjunctions;
            Prepositions = LexiconData.Prepositions;
            Nouns = FilterByThemes(LexiconData.Nouns, t);
            NounsPlural = FilterByThemes(LexiconData.NounsPlural, t);
            Adjectives = FilterByThemes(LexiconData.Adjectives, t);
            Adverbs = LexiconData.Adverbs;
            ProperNames = LexiconData.ProperNames;

            var verbs = new List<Verb>();
            foreach (var raw in LexiconData.VerbsRaw) verbs.Add(new Verb(raw));
            Verbs = verbs;

            var transitive = new List<Verb>();
            var intransitive = new List<Verb>();
            var bases = new List<string>();
            foreach (var v in verbs)
            {
                if (v.Tag == "t") transitive.Add(v);
                if (v.Tag == "i") intransitive.Add(v);
                bases.Add(v.Base);
            }
            VerbsTransitive = transitive;
            VerbsIntransitive = intransitive;
            VerbBases = bases;
        }

        /// <summary>
        /// Blended lexicon: copies `baseLexicon` glue tables but replaces the
        /// content pools with extended versions (built-in + sample vocabulary).
        /// Mirrors the Python Lexicon.blended classmethod.
        /// </summary>
        public Lexicon(Lexicon baseLexicon,
                       IReadOnlyList<string> nouns,
                       IReadOnlyList<string> nounsPlural,
                       IReadOnlyList<string> adjectives,
                       IReadOnlyList<string> adverbs,
                       IReadOnlyList<Verb> verbs)
        {
            Determiners = baseLexicon.Determiners;
            DeterminersPlural = baseLexicon.DeterminersPlural;
            Conjunctions = baseLexicon.Conjunctions;
            Prepositions = baseLexicon.Prepositions;
            Nouns = nouns;
            NounsPlural = nounsPlural;
            Adjectives = adjectives;
            Adverbs = adverbs;
            ProperNames = baseLexicon.ProperNames;

            Verbs = verbs;
            var transitive = new List<Verb>();
            var intransitive = new List<Verb>();
            var bases = new List<string>();
            foreach (var v in verbs)
            {
                if (v.Tag == "t") transitive.Add(v);
                if (v.Tag == "i") intransitive.Add(v);
                bases.Add(v.Base);
            }
            VerbsTransitive = transitive;
            VerbsIntransitive = intransitive;
            VerbBases = bases;
        }

        /// <summary>Fixed-order categories used by GenerateWords. Order matters for parity.</summary>
        public IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> ContentCategories()
        {
            return new[]
            {
                new KeyValuePair<string, IReadOnlyList<string>>("noun", Nouns),
                new KeyValuePair<string, IReadOnlyList<string>>("adjective", Adjectives),
                new KeyValuePair<string, IReadOnlyList<string>>("verb", VerbBases),
                new KeyValuePair<string, IReadOnlyList<string>>("adverb", Adverbs),
                new KeyValuePair<string, IReadOnlyList<string>>("plural", NounsPlural),
                new KeyValuePair<string, IReadOnlyList<string>>("name", ProperNames),
            };
        }

        private static string[] FilterThemes(IReadOnlyList<string> themes)
        {
            if (themes == null || themes.Count == 0) return Array.Empty<string>();
            var outp = new List<string>();
            foreach (var t in themes) outp.Add(t.ToLowerInvariant());
            return outp.ToArray();
        }

        private static IReadOnlyList<string> FilterByThemes(string[] words, string[] themes)
        {
            if (themes.Length == 0) return words;
            var outp = new List<string>();
            foreach (var w in words)
                foreach (var t in themes)
                    if (w.Contains(t))
                    {
                        outp.Add(w);
                        break;
                    }
            return outp.Count > 0 ? outp : words;
        }
    }
}
