using System;
using System.Collections.Generic;
using System.Linq;

namespace RandWords
{
    /// <summary>A prompt/completion pair for fine-tuning datasets.</summary>
    public sealed class TextPair
    {
        public int Id { get; }
        public string Strategy { get; }
        public string Prompt { get; }
        public string Completion { get; }

        public TextPair(int id, string strategy, string prompt, string completion)
        {
            Id = id;
            Strategy = strategy;
            Prompt = prompt;
            Completion = completion;
        }
    }

    /// <summary>
    /// Fine-tuning dataset builder: turns generated sentences into
    /// prompt/completion pairs. RNG call order mirrors the Python pairs module.
    /// </summary>
    public static class PairBuilder
    {
        public static readonly string[] Strategies = { "complete", "topic", "continue" };

        public static List<TextPair> Build(IReadOnlyList<Sentence> sentences, Rand rng,
                                           int count, string strategy = "complete")
        {
            if (!Strategies.Contains(strategy))
                throw new ArgumentException($"unknown strategy '{strategy}'; expected one of [{string.Join(", ", Strategies)}]");
            if (sentences == null || sentences.Count == 0)
                throw new ArgumentException("need at least one sentence to build pairs");

            var pairs = new List<TextPair>();
            for (int i = 0; i < count; i++)
            {
                string prompt, completion;
                if (strategy == "complete") (prompt, completion) = Complete(sentences, rng);
                else if (strategy == "topic") (prompt, completion) = Topic(sentences, rng);
                else (prompt, completion) = Continue(sentences, rng);
                pairs.Add(new TextPair(i, strategy, prompt, completion));
            }
            return pairs;
        }

        private static Tuple<string, string> Complete(IReadOnlyList<Sentence> sentences, Rand rng)
        {
            Sentence sentence = null;
            int split = 1;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                sentence = sentences[rng.NextInt(sentences.Count)];
                var words = sentence.Words;
                if (words.Count >= 3)
                {
                    split = 1 + rng.NextInt(words.Count - 2);
                    break;
                }
                split = words.Count >= 2 ? 1 : 0;
            }
            var words2 = sentence.Words;
            var prefix = RenderPart(words2.Take(split).ToList());
            var suffix = RenderPart(words2.Skip(split).ToList());
            var prompt = $"Complete this sentence: {prefix}...";
            var completion = suffix.Length > 0 ? suffix + "." : sentence.Text;
            return Tuple.Create(prompt, completion);
        }

        private static Tuple<string, string> Topic(IReadOnlyList<Sentence> sentences, Rand rng)
        {
            var sentence = sentences[rng.NextInt(sentences.Count)];
            var word = sentence.Words[rng.NextInt(sentence.Words.Count)];
            var prompt = $"Write a sentence that mentions '{word}'.";
            return Tuple.Create(prompt, sentence.Text);
        }

        private static Tuple<string, string> Continue(IReadOnlyList<Sentence> sentences, Rand rng)
        {
            var first = sentences[rng.NextInt(sentences.Count)];
            var second = sentences[rng.NextInt(sentences.Count)];
            return Tuple.Create(first.Text, second.Text);
        }

        private static string RenderPart(IReadOnlyList<string> words)
        {
            var text = string.Join(" ", words);
            if (text.Length == 0) return text;
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
