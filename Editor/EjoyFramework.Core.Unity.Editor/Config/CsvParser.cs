//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Unity.Editor.Config
{
    /// <summary>
    /// 简易 CSV 解析器：支持引号包裹、引号转义（""）、CRLF/LF/CR 换行。
    /// 不支持嵌入换行的 cell（生产数据表通常无此需求）。
    /// </summary>
    public static class CsvParser
    {
        public static List<string[]> ParseFile(string path)
        {
            using (var sr = new StreamReader(path, Encoding.UTF8))
                return Parse(sr.ReadToEnd());
        }

        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                rows.Add(ParseLine(line));
            }
            return rows;
        }

        private static string[] ParseLine(string line)
        {
            var cols = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { cols.Add(sb.ToString()); sb.Length = 0; }
                    else if (c == '"' && sb.Length == 0) inQuotes = true;
                    else sb.Append(c);
                }
            }
            cols.Add(sb.ToString());
            return cols.ToArray();
        }
    }
}
