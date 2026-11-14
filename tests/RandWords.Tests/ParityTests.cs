using System.Collections.Generic;
using Xunit;

namespace RandWords.Tests
{
    /// <summary>
    /// Cross-language parity: the C# port must reproduce, seed for seed, the
    /// outputs captured from the verified Python implementation.
    /// </summary>
    public class ParityTests
    {
        private static List<string> Batch(GeneratorOptions options, int count)
        {
            var gen = new WordGenerator(options);
            return gen.GenerateBatch(count).ConvertAll(s => s.Text);
        }

        [Fact]
        public void GrammarModeMatchesPython()
        {
            var texts = Batch(new GeneratorOptions { Seed = GoldenData.GrammarSeed }, GoldenData.GoldenCount);
            Assert.Equal(GoldenData.GrammarTexts, texts);
        }

        [Fact]
        public void MarkovModeMatchesPython()
        {
            var texts = Batch(new GeneratorOptions
            {
                Seed = GoldenData.GrammarSeed,
                Mode = GenerationMode.Markov
            }, GoldenData.GoldenCount);
            Assert.Equal(GoldenData.MarkovTexts, texts);
        }

        [Fact]
        public void InventedModeMatchesPython()
        {
            var texts = Batch(new GeneratorOptions
            {
                Seed = GoldenData.GrammarSeed,
                Mode = GenerationMode.Invented
            }, GoldenData.GoldenCount);
            Assert.Equal(GoldenData.InventedTexts, texts);
        }

        [Fact]
        public void WordGenerationMatchesPython()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = 7 });
            var words = gen.GenerateWords(12).ConvertAll(w => w.Word);
            Assert.Equal(GoldenData.WordItems, words);
        }

        [Fact]
        public void PairBuildingMatchesPython()
        {
            var gen = new WordGenerator(new GeneratorOptions { Seed = GoldenData.PairSeed });
            var sentences = gen.GenerateBatch(GoldenData.PairBatch);
            var pairs = gen.BuildPairs(sentences, GoldenData.PairCount, GoldenData.PairStrategy);
            Assert.Equal(GoldenData.PairItems.Length, pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                Assert.Equal(GoldenData.PairItems[i][0], pairs[i].Prompt);
                Assert.Equal(GoldenData.PairItems[i][1], pairs[i].Completion);
            }
        }

        [Fact]
        public void SplitMixKnownVectors()
        {
            // Published SplitMix64 vectors (seed 0); identical in Python.
            var rand = new Rand(0);
            Assert.Equal(0xE220A8397B1DCDAFUL, rand.NextU64());
            Assert.Equal(0x6E789E6AA1B965F4UL, rand.NextU64());
            Assert.Equal(0x6C45D188009454FUL, rand.NextU64());
        }

        [Fact]
        public void SampleMarkovMatchesPython()
        {
            var texts = Batch(new GeneratorOptions
            {
                Seed = GoldenData.SampleMarkovSeed,
                Mode = GenerationMode.Sample,
                SampleText = GoldenData.SampleDialog,
                SampleStrategy = "markov"
            }, GoldenData.SampleGoldenCount);
            Assert.Equal(GoldenData.SampleMarkovTexts, texts);
        }

        [Fact]
        public void SampleBlendMatchesPython()
        {
            var texts = Batch(new GeneratorOptions
            {
                Seed = GoldenData.SampleMarkovSeed,
                Mode = GenerationMode.Sample,
                SampleText = GoldenData.SampleDialog,
                SampleStrategy = "blend"
            }, GoldenData.SampleGoldenCount);
            Assert.Equal(GoldenData.SampleBlendTexts, texts);
        }

        [Fact]
        public void SampleWordInventionMatchesPython()
        {
            var gen = new WordGenerator(new GeneratorOptions
            {
                Seed = GoldenData.SampleWordsSeed,
                Mode = GenerationMode.Sample,
                SampleText = GoldenData.SampleDialog,
                SampleStrategy = "invent"
            });
            var words = gen.GenerateWords(GoldenData.SampleWordItems.Length)
                           .ConvertAll(w => w.Word);
            Assert.Equal(GoldenData.SampleWordItems, words);
        }

        [Fact]
        public void ConvenienceApiMatchesPython()
        {
            Assert.Equal(GoldenData.GrammarTexts[0], RandWordsCompat.Sentence(GoldenData.GrammarSeed));
            var gen = new WordGenerator(new GeneratorOptions { Seed = 42 });
            var expected = gen.GenerateWords(5).ConvertAll(w => w.Word);
            Assert.Equal(expected, RandWordsCompat.Words(5, 42));
        }
    }
}
