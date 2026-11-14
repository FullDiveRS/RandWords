using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RandWords
{
    /// <summary>
    /// Sample-based generation: learn vocabulary and style from user example text.
    ///
    /// Given a small sample (e.g. two lines of dialogue), the model supports three
    /// strategies:
    ///   markov : adaptive order-2 -> order-1 -> order-0 word chain trained on the
    ///            sample. Works even with a couple of sentences because every dead
    ///            end backs off to a lower order. Output never echoes a sample
    ///            sentence verbatim (duplicate candidates are re-rolled).
    ///   blend  : grammar templates whose content slots are filled from the sample's
    ///            own vocabulary, mixed into the built-in lexicon by `bias`.
    ///   invent : phoneme-synthesized words whose onset/vowel/coda weights are
    ///            learned from the sample's letter patterns.
    ///
    /// Parity contract (mirrors the Python randwords.sample module exactly):
    ///   - tables are insertion-ordered (first-seen) or sorted lists, never
    ///     enumerated dictionaries;
    ///   - selection always uses integer cumulative weighted walks (one rng draw
    ///     per pick, identical call order in both languages);
    ///   - sentence splitting, tokenization and phoneme parsing are pure functions;
    ///   - walk tier boundaries 75/95 are part of the contract.
    /// </summary>
    public sealed class SampleModel
    {
        private const string Pad = "<s>";
        private const string Term = "</s>";
        private static readonly string[] Terminal = { ".", "!", "?" };
        private static readonly char[] Edges = { ' ', '\t', '\r', '\n', ',', '"', '\'' };

        // Function words and dialogue glue are excluded from blend vocabulary
        // pools; they either exist in the built-in glue tables already or would
        // produce nonsense like "the them". Mirrors Python FUNCTION_WORDS.
        private static readonly HashSet<string> FunctionWords = new HashSet<string>(new[]
        {
            "a", "an", "the", "i", "you", "he", "she", "it", "we", "they",
            "me", "him", "us", "them", "my", "your", "his", "its", "our",
            "their", "mine", "yours", "hers", "this", "that", "these", "those",
            "is", "am", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "having", "do", "does", "did",
            "will", "would", "can", "could", "should", "may", "might", "must",
            "shall", "to", "of", "in", "on", "at", "for", "with", "from", "by",
            "and", "or", "but", "so", "if", "then", "than", "no", "not", "nor",
            "yes", "oh", "um", "uh", "very", "too", "also", "just", "now",
            "here", "there", "what", "when", "where", "who", "whom", "whose",
            "why", "how", "all", "any", "some", "such", "only", "own", "same",
            "again", "once", "because", "as", "until", "while", "about",
            "against", "between", "through", "during", "before", "after",
            "above", "below", "up", "down", "out", "off", "over", "under",
        });

        private sealed class Node
        {
            public readonly List<string> Words = new List<string>();
            public readonly List<int> Counts = new List<int>();
            private readonly Dictionary<string, int> _lookup = new Dictionary<string, int>();

            public void Add(string word)
            {
                if (_lookup.TryGetValue(word, out int idx))
                    Counts[idx]++;
                else
                {
                    _lookup[word] = Words.Count;
                    Words.Add(word);
                    Counts.Add(1);
                }
            }
        }

        private sealed class ClusterPair
        {
            public readonly string Cluster;
            public readonly int Count;
            public ClusterPair(string cluster, int count)
            {
                Cluster = cluster;
                Count = count;
            }
        }

        private readonly Lexicon _lexicon;

        private readonly Dictionary<string, Node> _n2 = new Dictionary<string, Node>();
        private readonly Dictionary<string, Node> _n1 = new Dictionary<string, Node>();
        private readonly Node _n0 = new Node();

        private readonly List<string> _vocab = new List<string>();
        private readonly HashSet<string> _vocabSet = new HashSet<string>();
        private readonly HashSet<string> _sentenceKeys = new HashSet<string>();

        private List<ClusterPair> _onsetPairs = new List<ClusterPair>();
        private List<ClusterPair> _vowelPairs = new List<ClusterPair>();
        private List<ClusterPair> _codaPairs = new List<ClusterPair>();

        public List<string> Pieces { get; } = new List<string>();
        public bool LowercaseStyle { get; }
        public IReadOnlyList<string> PoolNouns { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> PoolAdjectives { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> PoolAdverbs { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> PoolPlurals { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<Verb> PoolVerbs { get; private set; } = Array.Empty<Verb>();

        public SampleModel(string text, Lexicon lexicon = null)
        {
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
                throw new ArgumentException("sample text must not be empty");
            _lexicon = lexicon ?? new Lexicon();

            foreach (var piece in SplitPieces(text))
            {
                Pieces.Add(piece);
                var tokens = MarkovChain.Tokenize(piece);
                if (tokens.Count == 0) continue;
                _sentenceKeys.Add(WordKey(tokens));
                foreach (var t in tokens)
                {
                    if (Phonemes.IsWordToken(t))
                    {
                        _vocabSet.Add(t);
                        _n0.Add(t);
                    }
                    else if (t == "." || t == "!" || t == "?")
                    {
                        _n0.Add(t);
                    }
                }
                // order-2 transitions
                var seq2 = new List<string> { Pad, Pad };
                seq2.AddRange(tokens);
                seq2.Add(Term);
                for (int i = 2; i < seq2.Count; i++)
                {
                    var key = JoinKey(seq2[i - 2], seq2[i - 1]);
                    if (!_n2.TryGetValue(key, out var node))
                    {
                        node = new Node();
                        _n2[key] = node;
                    }
                    node.Add(seq2[i]);
                }
                // order-1 transitions
                var seq1 = new List<string> { Pad };
                seq1.AddRange(tokens);
                seq1.Add(Term);
                for (int i = 1; i < seq1.Count; i++)
                {
                    if (!_n1.TryGetValue(seq1[i - 1], out var node1))
                    {
                        node1 = new Node();
                        _n1[seq1[i - 1]] = node1;
                    }
                    node1.Add(seq1[i]);
                }
            }

            if (_vocabSet.Count == 0)
                throw new ArgumentException("sample contains no usable words");

            foreach (var w in _vocabSet) _vocab.Add(w);
            _vocab.Sort(StringComparer.Ordinal);

            LearnPhonemes();
            BuildPools();

            // Style echo: lowercase-majority samples keep lowercase output.
            int lower = 0, upper = 0;
            foreach (var piece in Pieces)
            {
                foreach (var ch in piece)
                {
                    if (char.IsLetter(ch))
                    {
                        if (char.IsLower(ch)) lower++;
                        else upper++;
                        break;
                    }
                }
            }
            LowercaseStyle = lower > upper;
        }

        // ------------------------------------------------------------------ //
        //  Pure helpers (no RNG)                                             //
        // ------------------------------------------------------------------ //

        /// <summary>Split raw text into sentence pieces at . ! ? boundaries.</summary>
        public static List<string> SplitPieces(string text)
        {
            var pieces = new List<string>();
            var buf = new StringBuilder();
            foreach (var ch in text)
            {
                if (ch == '.' || ch == '!' || ch == '?')
                {
                    var piece = buf.ToString().Trim(Edges);
                    buf.Clear();
                    if (piece.Length > 0) pieces.Add(piece);
                }
                else
                {
                    buf.Append(ch);
                }
            }
            var tail = buf.ToString().Trim(Edges);
            if (tail.Length > 0) pieces.Add(tail);
            return pieces;
        }

        /// <summary>Uniqueness key: word tokens joined (punctuation and case ignored).</summary>
        public static string WordKey(IReadOnlyList<string> tokens)
        {
            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                if (!Phonemes.IsWordToken(t)) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(t);
            }
            return sb.ToString();
        }

        private static string JoinKey(string a, string b)
        {
            return a + "\u0001" + b;
        }

        private static string LongestCluster(string[] clusters, string word)
        {
            string best = "";
            foreach (var cl in clusters)
                if (cl.Length > best.Length && word.StartsWith(cl, StringComparison.Ordinal))
                    best = cl;
            return best;
        }

        public bool InVocab(string word)
        {
            return _vocabSet.Contains(word);
        }

        public IReadOnlyCollection<string> SentenceKeys => _sentenceKeys;

        // ------------------------------------------------------------------ //
        //  Training helpers                                                  //
        // ------------------------------------------------------------------ //

        private void LearnPhonemes()
        {
            var on = new Dictionary<string, int>();
            var vw = new Dictionary<string, int>();
            var cd = new Dictionary<string, int>();
            foreach (var w in _vocab)
            {
                bool alphaOnly = true;
                foreach (var c in w)
                    if (!char.IsLetter(c)) { alphaOnly = false; break; }
                if (!alphaOnly) continue;  // skip contractions and mixed tokens
                var onset = LongestCluster(LexiconData.PhonemeOnsets, w);
                var rest = w.Substring(onset.Length);
                var vowel = LongestCluster(LexiconData.PhonemeVowels, rest);
                var rest2 = rest.Substring(vowel.Length);
                if (onset.Length > 0) on[onset] = on.TryGetValue(onset, out var a) ? a + 1 : 1;
                if (vowel.Length > 0) vw[vowel] = vw.TryGetValue(vowel, out var b) ? b + 1 : 1;
                if (Contains(LexiconData.PhonemeCodasFinal, rest2))
                    cd[rest2] = cd.TryGetValue(rest2, out var c) ? c + 1 : 1;
            }
            _onsetPairs = SortedPairs(on);
            _vowelPairs = SortedPairs(vw);
            _codaPairs = SortedPairs(cd);
        }

        private static bool Contains(string[] arr, string value)
        {
            foreach (var s in arr) if (s == value) return true;
            return false;
        }

        private static List<ClusterPair> SortedPairs(Dictionary<string, int> counts)
        {
            var list = new List<ClusterPair>();
            foreach (var kv in counts) list.Add(new ClusterPair(kv.Key, kv.Value));
            list.Sort((x, y) => string.CompareOrdinal(x.Cluster, y.Cluster));
            return list;
        }

        private void BuildPools()
        {
            var verbForms = new Dictionary<string, Verb>();
            foreach (var v in _lexicon.Verbs)
            {
                verbForms[v.Base] = v;
                verbForms[v.Third] = v;
                verbForms[v.Past] = v;
            }
            var glue = new HashSet<string>(_lexicon.Determiners);
            glue.UnionWith(_lexicon.DeterminersPlural);
            glue.UnionWith(_lexicon.Conjunctions);
            glue.UnionWith(_lexicon.Prepositions);
            glue.UnionWith(_lexicon.Adverbs);
            glue.UnionWith(_lexicon.ProperNames);
            var adjSet = new HashSet<string>(_lexicon.Adjectives);

            var nouns = new List<string>();
            var adjectives = new List<string>();
            var adverbs = new List<string>();
            var plurals = new List<string>();
            var verbs = new List<Verb>();
            var seenVerbs = new HashSet<string>();

            foreach (var w in _vocab)
            {
                if (w.Contains("'") || !IsAllLetters(w) || w.Length < 2) continue;
                if (FunctionWords.Contains(w) || glue.Contains(w)) continue;
                if (verbForms.TryGetValue(w, out var verb))
                {
                    if (seenVerbs.Add(verb.Base)) verbs.Add(verb);
                }
                else if (adjSet.Contains(w))
                {
                    adjectives.Add(w);
                }
                else if (w.Length >= 5 && w.EndsWith("ly", StringComparison.Ordinal))
                {
                    adverbs.Add(w);
                }
                else if (w.EndsWith("s", StringComparison.Ordinal)
                         && !w.EndsWith("ss", StringComparison.Ordinal)
                         && !w.EndsWith("us", StringComparison.Ordinal)
                         && !w.EndsWith("is", StringComparison.Ordinal))
                {
                    plurals.Add(w);
                }
                else
                {
                    nouns.Add(w);
                }
            }

            PoolNouns = nouns;
            PoolAdjectives = adjectives;
            PoolAdverbs = adverbs;
            PoolPlurals = plurals;
            PoolVerbs = verbs;
        }

        private static bool IsAllLetters(string w)
        {
            foreach (var c in w) if (!char.IsLetter(c)) return false;
            return true;
        }

        // ------------------------------------------------------------------ //
        //  Sentence generation (markov strategy)                             //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// One novel sentence; re-rolls candidates that echo the sample
        /// verbatim, duplicate `avoid` keys, or are shorter than `minWords`
        /// word tokens (best effort - the last candidate is returned when the
        /// attempt budget is exhausted).
        /// </summary>
        public Tuple<string, List<string>> GenerateSentence(Rand rng, int maxWords,
                                                            ISet<string> avoid = null,
                                                            int minWords = 3,
                                                            int attempts = 60)
        {
            if (_n2.Count == 0)
                throw new InvalidOperationException("sample model has no training data");
            var banned = new HashSet<string>(_sentenceKeys);
            if (avoid != null) banned.UnionWith(avoid);
            string text = "";
            var words = new List<string>();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var tokens = Walk(rng, maxWords);
                text = Render(tokens, LowercaseStyle);
                words = tokens.FindAll(Phonemes.IsWordToken);
                if (!banned.Contains(WordKey(tokens)) && words.Count >= minWords)
                    return Tuple.Create(text, words);
            }
            return Tuple.Create(text, words);
        }

        private List<string> Walk(Rand rng, int maxWords)
        {
            var ctx2 = new[] { Pad, Pad };
            var ctx1 = Pad;
            var output = new List<string>();
            int alpha = 0;
            while (true)
            {
                Node node;
                if (output.Count == 0)
                {
                    // First token always comes from the sentence-start distribution.
                    node = _n2[JoinKey(ctx2[0], ctx2[1])];
                }
                else
                {
                    int r = rng.NextInt(100);
                    if (r < 75)
                    {
                        if (!_n2.TryGetValue(JoinKey(ctx2[0], ctx2[1]), out node))
                            node = null;
                        if (node == null) _n1.TryGetValue(ctx1, out node);
                        if (node == null) node = _n0;
                    }
                    else if (r < 95)
                    {
                        if (!_n1.TryGetValue(ctx1, out node)) node = null;
                        if (node == null) node = _n0;
                    }
                    else
                    {
                        node = _n0;
                    }
                }
                var word = node.Words[_WeightedIndex(rng, node.Counts)];
                if (word == Term) break;
                output.Add(word);
                if (word == "." || word == "!" || word == "?") break;
                if (Phonemes.IsWordToken(word))
                {
                    alpha++;
                    if (alpha >= maxWords) break;
                }
                ctx2 = new[] { ctx2[1], word };
                ctx1 = word;
            }
            if (output.Count == 0)
                throw new InvalidOperationException("sample generation produced no tokens");
            var last = output[output.Count - 1];
            if (last != "." && last != "!" && last != "?") output.Add(".");
            return output;
        }

        private static int _WeightedIndex(Rand rng, List<int> counts)
        {
            int total = 0;
            for (int i = 0; i < counts.Count; i++) total += counts[i];
            int v = rng.NextInt(total);
            int cum = 0;
            for (int i = 0; i < counts.Count; i++)
            {
                cum += counts[i];
                if (cum > v) return i;
            }
            return counts.Count - 1;
        }

        private static string Render(IReadOnlyList<string> tokens, bool lowercaseStyle)
        {
            var text = string.Join(" ", tokens);
            foreach (var p in new[] { ",", ".", "?", "!", ";", ":" })
                text = text.Replace(" " + p, p);
            if (lowercaseStyle) return text;
            return Capitalize(text);
        }

        private static string Capitalize(string text)
        {
            for (int i = 0; i < text.Length; i++)
                if (char.IsLetter(text[i]))
                    return text.Substring(0, i) + char.ToUpperInvariant(text[i]) + text.Substring(i + 1);
            return text;
        }

        // ------------------------------------------------------------------ //
        //  Word generation (invent strategy)                                 //
        // ------------------------------------------------------------------ //

        /// <summary>Phoneme synthesis weighted by the sample's own letter patterns.</summary>
        public string NewWord(Rand rng)
        {
            int idx = rng.WeightedIndex(LexiconData.SyllableWeights);
            int n = LexiconData.SyllableCounts[idx];
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                var onset = PickCluster(rng, _onsetPairs, LexiconData.PhonemeOnsets);
                var vowel = PickCluster(rng, _vowelPairs, LexiconData.PhonemeVowels);
                var coda = (i == n - 1)
                    ? PickCluster(rng, _codaPairs, LexiconData.PhonemeCodasFinal)
                    : PickCluster(rng, _codaPairs, LexiconData.PhonemeCodasInner);
                sb.Append(onset).Append(vowel).Append(coda);
            }
            return sb.ToString();
        }

        private static string PickCluster(Rand rng, List<ClusterPair> pairs, string[] fallback)
        {
            if (pairs.Count > 0)
            {
                var weights = new List<int>(pairs.Count);
                foreach (var p in pairs) weights.Add(p.Count);
                return pairs[rng.WeightedIndex(weights)].Cluster;
            }
            return fallback[rng.NextInt(fallback.Length)];
        }

        /// <summary>GrammarEngine inventor hook: kind is a content kind or verb form.</summary>
        public string InventWord(Rand rng, string kind)
        {
            var word = NewWord(rng);
            if (kind == "plural") return Phonemes.Pluralize(word);
            if (kind == "third") return Phonemes.ThirdPerson(word);
            if (kind == "past") return Phonemes.PastTense(word);
            return word;
        }

        // ------------------------------------------------------------------ //
        //  Blend strategy                                                    //
        // ------------------------------------------------------------------ //

        /// <summary>Base lexicon with sample-vocab pools appended `copies` extra times.</summary>
        public Lexicon BlendedLexicon(Lexicon baseLexicon, int copies)
        {
            if (copies <= 0) return baseLexicon;
            return new Lexicon(baseLexicon,
                nouns: Extend(baseLexicon.Nouns, PoolNouns, copies),
                nounsPlural: Extend(baseLexicon.NounsPlural, PoolPlurals, copies),
                adjectives: Extend(baseLexicon.Adjectives, PoolAdjectives, copies),
                adverbs: Extend(baseLexicon.Adverbs, PoolAdverbs, copies),
                verbs: ConcatVerbs(baseLexicon.Verbs, PoolVerbs));
        }

        private static List<string> Extend(IReadOnlyList<string> current,
                                           IReadOnlyList<string> extra, int copies)
        {
            var output = new List<string>(current);
            for (int c = 0; c < copies; c++)
                output.AddRange(extra);
            return output;
        }

        private static List<Verb> ConcatVerbs(IReadOnlyList<Verb> current,
                                              IReadOnlyList<Verb> extra)
        {
            var output = new List<Verb>(current);
            output.AddRange(extra);
            return output;
        }
    }
}
