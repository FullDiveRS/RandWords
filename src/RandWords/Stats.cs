using System;
using System.Collections.Generic;
using System.Linq;

namespace RandWords
{
    /// <summary>Descriptive statistics over a batch of sentences (plain values).</summary>
    public sealed class StatsResult
    {
        public int SentenceCount;
        public int WordCount;
        public int UniqueWords;
        public double TypeTokenRatio;
        public double AvgSentenceLength;
        public int MinSentenceLength;
        public int MaxSentenceLength;
        /// <summary>POS tag -> count.</summary>
        public Dictionary<string, int> PosDistribution = new Dictionary<string, int>();
        /// <summary>Top bigrams as "w1 w2" -> count (count desc, then bigram asc, top 10).</summary>
        public List<KeyValuePair<string, int>> TopBigrams = new List<KeyValuePair<string, int>>();
        public string LongestSentence = "";
    }

    /// <summary>Corpus statistics: descriptive numbers over a batch of sentences.</summary>
    public static class Stats
    {
        public static StatsResult Describe(IReadOnlyList<Sentence> sentences)
        {
            var result = new StatsResult();
            if (sentences == null || sentences.Count == 0)
            {
                result.TypeTokenRatio = 0.0;
                return result;
            }

            var wordCounts = new Dictionary<string, int>();
            var posCounts = new Dictionary<string, int>();
            var bigrams = new Dictionary<string, int>();
            int totalWords = 0;
            int minLen = int.MaxValue;
            int maxLen = int.MinValue;

            foreach (var s in sentences)
            {
                var words = s.Words.Select(w => w.ToLowerInvariant()).ToList();
                totalWords += words.Count;
                if (words.Count < minLen) minLen = words.Count;
                if (words.Count > maxLen) { maxLen = words.Count; result.LongestSentence = s.Text; }
                foreach (var w in words) Increment(wordCounts, w);
                foreach (var t in s.PosTags) Increment(posCounts, t);
                for (int i = 0; i + 1 < words.Count; i++)
                    Increment(bigrams, words[i] + " " + words[i + 1]);
            }

            int n = sentences.Count;
            result.SentenceCount = n;
            result.WordCount = totalWords;
            result.UniqueWords = wordCounts.Count;
            result.TypeTokenRatio = totalWords > 0 ? Math.Round((double)wordCounts.Count / totalWords, 4) : 0.0;
            result.AvgSentenceLength = Math.Round((double)totalWords / n, 4);
            result.MinSentenceLength = minLen;
            result.MaxSentenceLength = maxLen;
            result.PosDistribution = posCounts;
            result.TopBigrams = bigrams
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(10)
                .ToList();
            return result;
        }

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            if (dict.TryGetValue(key, out int v)) dict[key] = v + 1;
            else dict[key] = 1;
        }
    }
}
