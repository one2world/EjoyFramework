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
    /// </summary>
    public static class LocalizationTextParser
    {
        private static readonly string[] s_LineSeparators = { "\r\n", "\n", "\r" };

        public static bool Parse(ILocalizationManager manager, string text)
        {
            if (manager == null) throw new FrameworkException("LocalizationTextParser.Parse: manager is null.");
            if (string.IsNullOrEmpty(text)) return true;

            string[] lines = text.Split(s_LineSeparators, StringSplitOptions.None);
            int lineNo = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                lineNo++;
                string raw = lines[i];
                if (raw == null) continue;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int tab = line.IndexOf('\t');
                if (tab <= 0)
                {
                    FrameworkLog.Warning("LocalizationTextParser: line {0} missing TAB delimiter. Skipped.", lineNo);
                    continue;
                }
                string key = line.Substring(0, tab);
                string value = line.Substring(tab + 1);
                if (!manager.AddRawString(key, value))
                {
                    FrameworkLog.Warning("LocalizationTextParser: duplicate key '{0}' at line {1}.", key, lineNo);
                }
            }
            return true;
        }
    }
}
