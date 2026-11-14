using System.Collections.Generic;
using System.Text;

namespace RandWords
{
    /// <summary>
    /// Phoneme-based invented-word synthesis and pure morphology helpers.
    /// The RNG call order here (syllable count, then per-syllable onset/vowel/coda)
    /// is mirrored exactly by the Python implementation.
    /// </summary>
    public static class Phonemes
    {
        /// <summary>Generate one pronounceable invented word, e.g. "brantol" or "veskine".</summary>
        public static string NewWord(Rand rng)
        {
            int idx = rng.WeightedIndex(LexiconData.SyllableWeights);
            int n = LexiconData.SyllableCounts[idx];
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string onset = LexiconData.PhonemeOnsets[rng.NextInt(LexiconData.PhonemeOnsets.Length)];
                string vowel = LexiconData.PhonemeVowels[rng.NextInt(LexiconData.PhonemeVowels.Length)];
                string coda = (i == n - 1)
                    ? LexiconData.PhonemeCodasFinal[rng.NextInt(LexiconData.PhonemeCodasFinal.Length)]
                    : LexiconData.PhonemeCodasInner[rng.NextInt(LexiconData.PhonemeCodasInner.Length)];
                sb.Append(onset).Append(vowel).Append(coda);
            }
            return sb.ToString();
        }

        public static string Pluralize(string word)
        {
            return word + (EndsWithSibilant(word) ? "es" : "s");
        }

        public static string ThirdPerson(string word)
        {
            return Pluralize(word);
        }

        public static string PastTense(string word)
        {
            return word.EndsWith("e") ? word + "d" : word + "ed";
        }

        /// <summary>A token counts as a word when it contains at least one letter.</summary>
        public static bool IsWordToken(string token)
        {
            foreach (char c in token)
                if (char.IsLetter(c)) return true;
            return false;
        }

        private static bool EndsWithSibilant(string word)
        {
            return word.EndsWith("s") || word.EndsWith("x") || word.EndsWith("z")
                   || word.EndsWith("ch") || word.EndsWith("sh");
        }
    }
}
