//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Config
{
    /// <summary>
    /// 默认配置文本解析器（核心层，无 Unity 依赖）。
    /// 文本格式：每非空非注释（# 开头）行五列 TAB 分隔：
    ///   Name \t StringValue \t BoolValue \t IntValue \t FloatValue
    /// 兼容 LF/CRLF/CR；空白行与 # 注释行被跳过；
    /// 列数不足或重复 Name 被记录为 Warning 并跳过。
    /// </summary>
    public static class ConfigTextParser
    {
        private static readonly string[] s_LineSeparators = { "\r\n", "\n", "\r" };

        public static bool Parse(IConfigManager manager, string text)
        {
            if (manager == null) throw new FrameworkException("ConfigTextParser.Parse: manager is null.");
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

                string[] cols = line.Split('\t');
                if (cols.Length < 5)
                {
                    FrameworkLog.Warning("ConfigTextParser: line {0} has {1} cols, expected 5 (Name\\tString\\tBool\\tInt\\tFloat). Skipped.", lineNo, cols.Length);
                    continue;
                }
                if (!manager.AddConfig(cols[0], cols[1], cols[2], cols[3], cols[4]))
                {
                    FrameworkLog.Warning("ConfigTextParser: duplicate or invalid config '{0}' at line {1}.", cols[0], lineNo);
                }
            }
            return true;
        }
    }
}
