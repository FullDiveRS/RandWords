using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RandWords;

namespace RandWords.Demo
{
    /// <summary>
    /// RandWords demo: generate AI-training text from the command line.
    ///
    /// Examples:
    ///   dotnet run --project demo/RandWords.Demo -- --mode grammar --count 20 --seed 42
    ///   dotnet run --project demo/RandWords.Demo -- --mode invented --count 5
    ///   dotnet run --project demo/RandWords.Demo -- --mode markov --count 10 --corpus mytext.txt --stats
    ///   dotnet run --project demo/RandWords.Demo -- --words 12
    ///   dotnet run --project demo/RandWords.Demo -- --pairs 8 --strategy topic --out pairs.jsonl --chat
    ///   dotnet run --project demo/RandWords.Demo -- --count 1000 --format jsonl --out corpus.jsonl
    ///
    /// Learn from a sample:
    ///   dotnet run --project demo/RandWords.Demo -- --sample "Oh? thank you very much, please have these cookies i baked!" --count 6
    ///   dotnet run --project demo/RandWords.Demo -- --sample-file dialogue.txt --sample-strategy blend --bias 80
    ///   dotnet run --project demo/RandWords.Demo -- --sample "..." --sample-strategy invent --words 15
    /// </summary>
    public static class Program
    {
        private sealed class Args
        {
            public string Mode = "grammar";
            public string Source = "real";
            public string Sample;
            public string SampleFile;
            public string SampleStrategy = "markov";
            public int Bias = 60;
            public int Count = 10;
            public int Words;
            public string Pos;
            public int Paragraph;
            public int Pairs;
            public string Strategy = "complete";
            public bool Chat;
            public ulong Seed = 42;
            public string Corpus;
            public string Format = "txt";
            public string Out;
            public bool Stats;
        }

        private static Args ParseArgs(string[] argv)
        {
            var args = new Args();
            for (int i = 0; i < argv.Length; i++)
            {
                string a = argv[i];
                string Next() => ++i < argv.Length ? argv[i] : throw new ArgumentException($"missing value for {a}");
                switch (a)
                {
                    case "--mode": args.Mode = Next(); break;
                    case "--source": args.Source = Next(); break;
                    case "--sample": args.Sample = Next(); break;
                    case "--sample-file": args.SampleFile = Next(); break;
                    case "--sample-strategy": args.SampleStrategy = Next(); break;
                    case "--bias": args.Bias = int.Parse(Next()); break;
                    case "--count": args.Count = int.Parse(Next()); break;
                    case "--words": args.Words = int.Parse(Next()); break;
                    case "--pos": args.Pos = Next(); break;
                    case "--paragraph": args.Paragraph = int.Parse(Next()); break;
                    case "--pairs": args.Pairs = int.Parse(Next()); break;
                    case "--strategy": args.Strategy = Next(); break;
                    case "--chat": args.Chat = true; break;
                    case "--seed": args.Seed = ulong.Parse(Next()); break;
                    case "--corpus": args.Corpus = Next(); break;
                    case "--format": args.Format = Next(); break;
                    case "--out": args.Out = Next(); break;
                    case "--stats": args.Stats = true; break;
                    case "--help":
                    case "-h":
                        Console.WriteLine("Usage: RandWords.Demo [--mode grammar|markov|invented|sample] [--source real|invented|mixed]");
                        Console.WriteLine("  [--sample text | --sample-file file] [--sample-strategy markov|blend|invent] [--bias N]");
                        Console.WriteLine("  [--count N] [--words N [--pos ...]] [--paragraph N] [--pairs N --strategy s] [--chat]");
                        Console.WriteLine("  [--seed N] [--corpus file] [--format txt|jsonl|csv] [--out file] [--stats]");
                        Environment.Exit(0);
                        break;
                    default:
                        throw new ArgumentException($"unknown argument '{a}' (try --help)");
                }
            }
            return args;
        }

        public static void Main(string[] argv)
        {
            var args = ParseArgs(argv);

            string sampleText = args.Sample;
            if (!string.IsNullOrEmpty(args.SampleFile))
                sampleText = File.ReadAllText(args.SampleFile);
            string mode = sampleText != null ? "sample" : args.Mode;

            var options = new GeneratorOptions
            {
                Seed = args.Seed,
                Mode = ParseMode(mode),
                WordSource = ParseSource(args.Source),
                SampleText = sampleText,
                SampleStrategy = args.SampleStrategy,
                SampleBias = args.Bias,
            };
            var gen = new WordGenerator(options);

            if (!string.IsNullOrEmpty(args.Corpus))
                gen.TrainCorpus(args.Corpus);

            if (args.Words > 0)
            {
                var items = gen.GenerateWords(args.Words, args.Pos);
                Console.WriteLine(string.Join(" ", items.Select(w => w.Word)));
                return;
            }

            if (args.Paragraph > 0)
            {
                Console.WriteLine(gen.GenerateParagraph(args.Paragraph));
                return;
            }

            if (args.Pairs > 0)
            {
                var sentences = gen.GenerateBatch(Math.Max(args.Count, 10));
                var pairs = gen.BuildPairs(sentences, args.Pairs, args.Strategy);
                if (!string.IsNullOrEmpty(args.Out))
                {
                    gen.ExportPairsJsonl(pairs, args.Out, args.Chat);
                    Console.WriteLine($"wrote {pairs.Count} pairs -> {args.Out}");
                }
                else
                {
                    foreach (var p in pairs)
                    {
                        if (args.Chat)
                            Console.WriteLine($"{{\"id\": {p.Id}, \"messages\": [{{\"role\": \"user\", \"content\": {Json.Str(p.Prompt)}}}, {{\"role\": \"assistant\", \"content\": {Json.Str(p.Completion)}}}]}}");
                        else
                            Console.WriteLine($"{{\"id\": {p.Id}, \"prompt\": {Json.Str(p.Prompt)}, \"completion\": {Json.Str(p.Completion)}}}");
                    }
                }
                return;
            }

            var batch = gen.GenerateBatch(args.Count);

            if (args.Stats)
            {
                Console.WriteLine(FormatStats(gen.Describe(batch)));
                if (string.IsNullOrEmpty(args.Out)) return;
            }

            if (string.IsNullOrEmpty(args.Out))
            {
                foreach (var s in batch) Console.WriteLine(s.Text);
            }
            else
            {
                if (args.Format == "jsonl") gen.ExportJsonl(batch, args.Out);
                else if (args.Format == "csv") gen.ExportCsv(batch, args.Out);
                else gen.ExportTxt(batch, args.Out);
                Console.WriteLine($"wrote {batch.Count} sentences -> {args.Out} ({args.Format})");
            }
        }

        private static GenerationMode ParseMode(string mode)
        {
            switch (mode)
            {
                case "grammar": return GenerationMode.Grammar;
                case "markov": return GenerationMode.Markov;
                case "invented": return GenerationMode.Invented;
                case "sample": return GenerationMode.Sample;
                default: throw new ArgumentException($"unknown mode '{mode}'");
            }
        }

        private static WordSource ParseSource(string source)
        {
            switch (source)
            {
                case "real": return WordSource.RealWords;
                case "invented": return WordSource.Invented;
                case "mixed": return WordSource.Mixed;
                default: throw new ArgumentException($"unknown source '{source}'");
            }
        }

        private static string FormatStats(StatsResult st)
        {
            var pos = string.Join(", ", st.PosDistribution.Select(kv => $"\"{Json.Escape(kv.Key)}\": {kv.Value}"));
            var bigrams = string.Join(",\n    ", st.TopBigrams.Select(kv =>
                $"[\n      \"{Json.Escape(kv.Key)}\",\n      {kv.Value}\n    ]"));
            return "{\n" +
                   $"  \"sentence_count\": {st.SentenceCount},\n" +
                   $"  \"word_count\": {st.WordCount},\n" +
                   $"  \"unique_words\": {st.UniqueWords},\n" +
                   $"  \"type_token_ratio\": {st.TypeTokenRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                   $"  \"avg_sentence_length\": {st.AvgSentenceLength.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                   $"  \"min_sentence_length\": {st.MinSentenceLength},\n" +
                   $"  \"max_sentence_length\": {st.MaxSentenceLength},\n" +
                   $"  \"pos_distribution\": {{ {pos} }},\n" +
                   "  \"top_bigrams\": [\n    " + bigrams + "\n  ],\n" +
                   $"  \"longest_sentence\": {Json.Str(st.LongestSentence)}\n" +
                   "}";
        }
    }
}
