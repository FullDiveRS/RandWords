using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RandWords
{
    /// <summary>
    /// Order-2 (configurable) Markov chain over tokenized sentences with
    /// insertion-ordered successor lists. Parity contract: training walks the
    /// corpus in given order and appends successors in first-seen order;
    /// generation selects via cumulative weighted walk over those lists.
    /// The Python MarkovChain mirrors this exactly.
    /// </summary>
    public sealed class MarkovChain
    {
        private const string Term = "</s>";
        private const string Pad = "<s>";
        private static readonly string[] TerminalPunct = { ".", "!", "?" };

        private readonly int _order;
        private readonly List<string[]> _starts = new List<string[]>();
        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>();

        /// <summary>Context size (previous words) used for transitions.</summary>
        public int Order => _order;

        private sealed class Node
        {
            public readonly List<string> Words = new List<string>();
            public readonly List<int> Counts = new List<int>();
            private readonly Dictionary<string, int> _lookup = new Dictionary<string, int>();

            public void Add(string word)
            {
                if (_lookup.TryGetValue(word, out int idx))
                {
                    Counts[idx]++;
                }
                else
                {
                    _lookup[word] = Words.Count;
                    Words.Add(word);
                    Counts.Add(1);
                }
            }
        }

        public MarkovChain(int order = 2)
        {
            if (order < 1)
                throw new ArgumentOutOfRangeException(nameof(order), "order must be >= 1");
            _order = order;
        }

        // ------------------------------------------------------------------ //
        //  Training                                                          //
        // ------------------------------------------------------------------ //

        public void Train(IReadOnlyList<string> sentences)
        {
            foreach (var line in sentences)
            {
                var tokens = Tokenize(line);
                if (tokens.Count == 0) continue;
                var seq = new List<string>();
                for (int i = 0; i < _order; i++) seq.Add(Pad);
                seq.AddRange(tokens);
                seq.Add(Term);
                for (int i = _order; i < seq.Count; i++)
                {
                    var context = seq.GetRange(i - _order, _order).ToArray();
                    var key = ContextKey(context);
                    if (!_nodes.TryGetValue(key, out var node))
                    {
                        node = new Node();
                        _nodes[key] = node;
                    }
                    node.Add(seq[i]);
                    if (i == _order && !StartExists(key)) _starts.Add(context);
                }
            }
        }

        private bool StartExists(string key)
        {
            foreach (var s in _starts)
                if (ContextKey(s) == key) return true;
            return false;
        }

        private static string ContextKey(string[] context)
        {
            return string.Join("\u0001", context);
        }

        // ------------------------------------------------------------------ //
        //  Generation                                                        //
        // ------------------------------------------------------------------ //

        /// <summary>Returns (text, wordList).</summary>
        public Tuple<string, List<string>> Generate(Rand rng, int maxWords)
        {
            if (_starts.Count == 0)
                throw new InvalidOperationException("Markov chain has no training data; call Train() first");
            var context = _starts[rng.NextInt(_starts.Count)];
            var output = new List<string>();
            int alpha = 0;
            while (true)
            {
                if (!_nodes.TryGetValue(ContextKey(context), out var node)) break;
                int idx = WeightedIndex(rng, node.Counts);
                var word = node.Words[idx];
                if (word == Term) break;
                output.Add(word);
                if (Phonemes.IsWordToken(word))
                {
                    alpha++;
                    if (alpha >= maxWords) break;
                }
                var shifted = new string[context.Length - 1];
                Array.Copy(context, 1, shifted, 0, context.Length - 1);
                context = shifted.Append(word).ToArray();
            }
            if (output.Count == 0)
                throw new InvalidOperationException("Markov generation produced no tokens");
            var last = output[output.Count - 1];
            if (last != "." && last != "!" && last != "?") output.Add(".");
            var text = Render(output);
            var words = output.FindAll(Phonemes.IsWordToken);
            return Tuple.Create(text, words);
        }

        private static int WeightedIndex(Rand rng, List<int> counts)
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

        // ------------------------------------------------------------------ //
        //  Tokenization + rendering (pure functions - no RNG)                //
        // ------------------------------------------------------------------ //

        /// <summary>Mirrors the Python regex [a-z']+|[.,!?;] over line.lower().</summary>
        public static List<string> Tokenize(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            foreach (var c in line.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || c == '\'')
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length > 0)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                    }
                    if (c == '.' || c == ',' || c == '!' || c == '?' || c == ';')
                        result.Add(c.ToString());
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        private static string Render(IReadOnlyList<string> tokens)
        {
            var text = string.Join(" ", tokens);
            foreach (var p in new[] { ",", ".", "?", "!", ";", ":" })
                text = text.Replace(" " + p, p);
            return Capitalize(text);
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
