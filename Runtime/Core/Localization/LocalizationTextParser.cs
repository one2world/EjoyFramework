//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Localization
{
    /// <summary>
    /// 默认本地化文本解析器（核心层，无 Unity 依赖）。
    /// 文本格式：每非空非注释行两列 TAB 分隔： Key \t Value
    /// 兼容 LF/CRLF/CR；空白行与 # 注释行被跳过；
    /// 无 TAB 分隔符或重复 Key 被记录为 Warning 并跳过。
    /// 零分配切行（<see cref="TextLineEnumerator"/>），每条只为入库的 Key 与 Value 各分配一个 string。
    /// </summary>
    public static class LocalizationTextParser
    {
        public static bool Parse(ILocalizationManager manager, string text)
        {
            if (manager == null) throw new FrameworkException("LocalizationTextParser.Parse: manager is null.");
            if (string.IsNullOrEmpty(text)) return true;

            TextLineEnumerator lines = new TextLineEnumerator(text.AsSpan());
            while (lines.MoveNext())
            {
                ReadOnlySpan<char> line = lines.Current.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int tab = line.IndexOf('\t');
                if (tab <= 0)
                {
                    FrameworkLog.Warning("LocalizationTextParser: line {0} missing TAB delimiter. Skipped.", lines.LineNumber);
                    continue;
                }
                string key = line.Slice(0, tab).ToString();
                string value = line.Slice(tab + 1).ToString();
                if (!manager.AddRawString(key, value))
                {
                    FrameworkLog.Warning("LocalizationTextParser: duplicate key '{0}' at line {1}.", key, lines.LineNumber);
                }
            }
            return true;
        }
    }
}
