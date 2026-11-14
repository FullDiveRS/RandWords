# RandWords (C#)

Random words that connect like sentences, for AI training data. C# twin of the Python `randwords` package - same seed, same output, verified by cross-language parity tests.

.NET 8 class library (`src/RandWords`), xunit test suite (`tests/RandWords.Tests`), demo CLI (`demo/RandWords.Demo`), all wired into `RandWords.sln`.

```bash
dotnet test RandWords.sln            # 37 tests, incl. Python-parity golden tests
dotnet run --project demo/RandWords.Demo -- --mode grammar --count 20 --seed 42
```

To package for NuGet: `dotnet pack src/RandWords -c Release` (PackageId `RandWords`, version 1.0.0).

## Usage

```csharp
using RandWords;

var gen = new WordGenerator(new GeneratorOptions
{
    Seed = 42,                                  // null = OS entropy, unseeded runs
    Mode = GenerationMode.Grammar,              // or Markov / Invented / Sample
    WordSource = WordSource.RealWords,          // or Invented / Mixed (grammar mode)
    MinWords = 3,
    MaxWords = 18,                              // sentence length filter
    MarkovOrder = 2,                            // Markov context size
    Corpus = null,                              // string[]: custom Markov training sentences
    Themes = null,                              // e.g. ["water", "wind"] filters noun/adj pools
});
```

### Sentences, words, paragraphs

```csharp
Sentence s = gen.GenerateSentence();
s.Text       // "This old valley traded every golden crow across his sailor."
s.Words      // ["this", "old", "valley", ...]      punctuation excluded
s.PosTags    // ["det", "adj", "noun", "verb", ...] parallel to Words
s.Rule       // "T06" (template id) or "markov"
s.Seed       // 42
s.Mode       // "grammar" / "markov" / "invented" / "sample"

List<Sentence> batch = gen.GenerateBatch(1000);   // bulk pretraining corpus
string paragraph = gen.GenerateParagraph(4);

List<GeneratedWord> ws = gen.GenerateWords(10);             // random standalone words
ws = gen.GenerateWords(10, pos: "noun");                    // noun/adjective/verb/adverb/plural/name
var strings = ws.Select(w => w.Word).ToList();

RandWordsCompat.Sentence(42);       // one-shot helpers
RandWordsCompat.Words(8, 1);
RandWordsCompat.Paragraph(4, 5);
```

### Modes

| Mode | Behavior |
| --- | --- |
| `GenerationMode.Grammar` | 30 weighted templates: declaratives, because/when/while clauses, compound sentences, Did/Does/Why/Where questions, exclamations, dialogue quotes. Verb agreement and a/an insertion handled. |
| `GenerationMode.Markov` | Order-2 chains over the built-in 64-sentence corpus, or `gen.TrainCorpus("file")` / `gen.TrainSentences(list)` with your own. Corpus formats: `.txt`, `.json`, `.jsonl`, `.csv`. |
| `GenerationMode.Invented` | Grammar scaffolding filled with phoneme-synthesized words (skealveas, pearswalt). Function words stay real so output still reads like sentences. |
| `GenerationMode.Sample` | Learn from example text (`SampleText = ...`, or `gen.TrainSample(text)`). Three strategies via `SampleStrategy`: `markov` - adaptive chain producing unique novel sentences that never echo the sample; `blend` - grammar templates filled from the sample's vocabulary (`SampleBias` 0-100); `invent` - new words weighted by the sample's letter patterns. Lowercase dialogue samples keep lowercase output. |

`WordSource.Mixed` blends real and invented content words per slot; `WordSource.Invented` is equivalent to invented mode inside grammar mode.

Sample mode quick example:

```csharp
var dialog = "\"Oh? thank you very much, please have these cookies i baked! " +
             "i made extra and didn't know what to do with them\", " +
             "\"You liked them? that's so good to hear! No no, i can't accept that, " +
             "please let me help you from now on.\"";

var gen = new WordGenerator(new GeneratorOptions
{
    Seed = 42, Mode = GenerationMode.Sample, SampleText = dialog
});
gen.GenerateSentence().Text;   // "no no, i can't accept that, please have these cookies i baked."

var wgen = new WordGenerator(new GeneratorOptions
{
    Seed = 7, Mode = GenerationMode.Sample,
    SampleText = dialog, SampleStrategy = "invent"
});
wgen.GenerateWords(10);        // novel words that sound like the sample
```

### Fine-tuning pairs

```csharp
var batch = gen.GenerateBatch(100);
var pairs = gen.BuildPairs(batch, 200, "complete");   // "topic" | "continue"
pairs[0].Prompt      // "Complete this sentence: The cloud..."
pairs[0].Completion  // "Greeted each crow so some apple widened."

gen.ExportPairsJsonl(pairs, "pairs.jsonl");                 // {"id","strategy","prompt","completion"}
gen.ExportPairsJsonl(pairs, "chat.jsonl", chatFormat: true);
// {"id": ..., "messages": [{"role":"user",...},{"role":"assistant",...}]}
```

Strategies: `complete` splits a sentence into prefix prompt / suffix completion; `topic` asks for a sentence mentioning a sampled word; `continue` chains sentence A to sentence B.

### Exports and stats

```csharp
gen.ExportTxt(batch, "out.txt");      // one sentence per line
gen.ExportJsonl(batch, "out.jsonl");  // {"id","seed","mode","rule","sentence","words","pos_tags"}
gen.ExportCsv(batch, "out.csv");      // sentence_id, word_index, word, pos_tag, sentence

StatsResult st = gen.Describe(batch);
st.SentenceCount; st.UniqueWords; st.TypeTokenRatio;
st.PosDistribution; st.TopBigrams; st.LongestSentence;
```

### Demo CLI

```bash
dotnet run --project demo/RandWords.Demo -- --mode grammar --count 20 --seed 42
dotnet run --project demo/RandWords.Demo -- --mode invented --count 5
dotnet run --project demo/RandWords.Demo -- --mode markov --corpus my.txt --count 10 --stats
dotnet run --project demo/RandWords.Demo -- --sample-file dialogue.txt --count 20 --seed 42
dotnet run --project demo/RandWords.Demo -- --sample "..." --sample-strategy blend --bias 80
dotnet run --project demo/RandWords.Demo -- --sample "..." --sample-strategy invent --words 15
dotnet run --project demo/RandWords.Demo -- --words 12 --pos noun --seed 1
dotnet run --project demo/RandWords.Demo -- --pairs 8 --strategy topic --chat --out pairs.jsonl
dotnet run --project demo/RandWords.Demo -- --count 5000 --format jsonl --out corpus.jsonl
```

### Determinism and parity

Seeded generators are fully reproducible, and identical to the Python package: both embed the same data tables (`LexiconData.cs` is generated from `shared/randwords-data.json`), run the same SplitMix64 PRNG (`Rand.cs`), and make the same integer-only random draws in the same order. `ParityTests.cs` asserts the golden sentence streams captured from Python. JSONL formatting may differ cosmetically from the Python exporter; data content is identical.
