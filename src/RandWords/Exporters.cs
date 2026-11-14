using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RandWords
{
    /// <summary>Minimal JSON text writer compatible with json.dumps(ensure_ascii=False).</summary>
    public static class Json
    {
        public static string Escape(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Str(string value)
        {
            return "\"" + Escape(value) + "\"";
        }
    }

    /// <summary>Exporters: plain text, JSONL, CSV, and pair datasets (flat or chat format).</summary>
    public static class Exporters
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>One sentence per line.</summary>
        public static string ExportTxt(IReadOnlyList<Sentence> sentences, string path)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sentences.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(sentences[i].Text);
            }
            if (sentences.Count > 0) sb.Append('\n');
            Write(path, sb.ToString());
            return path;
        }

        /// <summary>One JSON record per line with full metadata.</summary>
        public static string ExportJsonl(IReadOnlyList<Sentence> sentences, string path)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < sentences.Count; i++)
            {
                var s = sentences[i];
                sb.Append("{\"id\": ").Append(i)
                  .Append(", \"seed\": ").Append(s.Seed)
                  .Append(", \"mode\": ").Append(Json.Str(s.Mode))
                  .Append(", \"rule\": ").Append(Json.Str(s.Rule))
                  .Append(", \"sentence\": ").Append(Json.Str(s.Text))
                  .Append(", \"words\": ").Append(StringArray(s.Words))
                  .Append(", \"pos_tags\": ").Append(StringArray(s.PosTags))
                  .Append("}\n");
            }
            Write(path, sb.ToString());
            return path;
        }

        /// <summary>Word-level rows: sentence_id, word_index, word, pos_tag, sentence.</summary>
        public static string ExportCsv(IReadOnlyList<Sentence> sentences, string path)
        {
            var sb = new StringBuilder();
            sb.Append("sentence_id,word_index,word,pos_tag,sentence\n");
            for (int i = 0; i < sentences.Count; i++)
            {
                var s = sentences[i];
                for (int j = 0; j < s.Words.Count; j++)
                {
                    sb.Append(i).Append(',')
                      .Append(j).Append(',')
                      .Append(Csv.EscapeField(s.Words[j])).Append(',')
                      .Append(Csv.EscapeField(s.PosTags[j])).Append(',')
                      .Append(Csv.EscapeField(s.Text)).Append('\n');
                }
            }
            Write(path, sb.ToString());
            return path;
        }

        /// <summary>Prompt/completion pairs as JSONL; chatFormat=True writes messages-style records.</summary>
        public static string ExportPairsJsonl(IReadOnlyList<TextPair> pairs, string path,
                                              bool chatFormat = false)
        {
            var sb = new StringBuilder();
            foreach (var p in pairs)
            {
                if (chatFormat)
                {
                    sb.Append("{\"id\": ").Append(p.Id)
                      .Append(", \"messages\": [{\"role\": \"user\", \"content\": ")
                      .Append(Json.Str(p.Prompt))
                      .Append("}, {\"role\": \"assistant\", \"content\": ")
                      .Append(Json.Str(p.Completion))
                      .Append("}]}\n");
                }
                else
                {
                    sb.Append("{\"id\": ").Append(p.Id)
                      .Append(", \"strategy\": ").Append(Json.Str(p.Strategy))
                      .Append(", \"prompt\": ").Append(Json.Str(p.Prompt))
                      .Append(", \"completion\": ").Append(Json.Str(p.Completion))
                      .Append("}\n");
                }
            }
            Write(path, sb.ToString());
            return path;
        }

        private static string StringArray(IReadOnlyList<string> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Json.Str(items[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static void Write(string path, string content)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, Utf8NoBom);
        }
    }
}
