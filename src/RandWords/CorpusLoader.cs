using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RandWords
{
    /// <summary>
    /// Minimal RFC4180 CSV field parsing/escaping shared by the corpus loader
    /// and the CSV exporter. Mirrors the Python csv module for well-formed files.
    /// </summary>
    public static class Csv
    {
        public static List<List<string>> Parse(string content)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c == '\r')
                {
                    // skip; LF handled separately
                }
                else
                {
                    field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            // drop trailing fully-empty rows (content ending with newline)
            while (rows.Count > 0 && rows[rows.Count - 1].Count == 1 && rows[rows.Count - 1][0].Length == 0)
                rows.RemoveAt(rows.Count - 1);
            return rows;
        }

        public static string EscapeField(string value)
        {
            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }

    /// <summary>Corpus loading for Markov training: .txt / .json / .jsonl / .csv.</summary>
    public static class CorpusLoader
    {
        private static readonly string[] TextKeys = { "sentence", "text", "line", "value" };

        public static List<string> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("corpus file not found: " + path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".txt": return LoadTxt(path);
                case ".json": return LoadJson(File.ReadAllText(path));
                case ".jsonl": return LoadJsonl(path);
                case ".csv": return LoadCsv(path);
                default:
                    throw new ArgumentException("unsupported corpus format: " + ext + " (use .txt, .json, .jsonl or .csv)");
            }
        }

        public static List<string> LoadTxt(string path)
        {
            var output = new List<string>();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) output.Add(trimmed);
            }
            return output;
        }

        public static List<string> LoadJson(string content)
        {
            var output = new List<string>();
            var node = System.Text.Json.JsonDocument.Parse(content).RootElement;
            if (node.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                ExtractArray(node, output);
            }
            else if (node.ValueKind == System.Text.Json.JsonValueKind.Object &&
                     node.TryGetProperty("sentences", out var inner) &&
                     inner.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                ExtractArray(inner, output);
            }
            return output;
        }

        private static void ExtractArray(System.Text.Json.JsonElement array, List<string> output)
        {
            foreach (var item in array.EnumerateArray())
            {
                var text = Extract(item);
                if (text.Length > 0) output.Add(text);
            }
        }

        public static List<string> LoadJsonl(string path)
        {
            var output = new List<string>();
            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var text = Extract(System.Text.Json.JsonDocument.Parse(trimmed).RootElement);
                if (text.Length > 0) output.Add(text);
            }
            return output;
        }

        public static List<string> LoadCsv(string path)
        {
            var rows = Csv.Parse(File.ReadAllText(path));
            var output = new List<string>();
            if (rows.Count == 0) return output;

            int? col = null;
            int start = 0;
            var header = new List<string>();
            foreach (var h in rows[0]) header.Add(h.Trim().ToLower(CultureInfo.InvariantCulture));
            foreach (var key in TextKeys)
            {
                int idx = header.IndexOf(key);
                if (idx >= 0) { col = idx; break; }
            }
            if (col != null) start = 1;
            else col = 0;

            for (int r = start; r < rows.Count; r++)
            {
                var row = rows[r];
                if (col.Value < row.Count && row[col.Value].Trim().Length > 0)
                    output.Add(row[col.Value].Trim());
            }
            return output;
        }

        private static string Extract(System.Text.Json.JsonElement element)
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var s = element.GetString();
                return s != null ? s.Trim() : "";
            }
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var key in TextKeys)
                {
                    if (element.TryGetProperty(key, out var value) &&
                        value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = value.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    }
                }
            }
            return "";
        }
    }
}
