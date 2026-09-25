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
    /// 零分配切行切列（<see cref="TextLineEnumerator"/> / <see cref="TextFieldReader"/>），布尔 / 整数 / 浮点列直接从切片解析
    /// （<see cref="SpanParse"/>，语义同 <see cref="IConfigManager.AddConfig(string, string, string, string, string)"/>），
    /// 每行只为入库的 Name 与原始值各分配一个 string。
    /// </summary>
    public static class ConfigTextParser
    {
        public static bool Parse(IConfigManager manager, string text)
        {
            if (manager == null) throw new FrameworkException("ConfigTextParser.Parse: manager is null.");
            if (string.IsNullOrEmpty(text)) return true;

            TextLineEnumerator lines = new TextLineEnumerator(text.AsSpan());
            while (lines.MoveNext())
            {
                ReadOnlySpan<char> line = lines.Current.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int columns = TextFieldReader.CountFields(line, '\t');
                if (columns < 5)
                {
                    FrameworkLog.Warning("ConfigTextParser: line {0} has {1} cols, expected 5 (Name\\tString\\tBool\\tInt\\tFloat). Skipped.", lines.LineNumber, columns);
                    continue;
                }

                TextFieldReader reader = new TextFieldReader(line, '\t');
                ReadOnlySpan<char> name, raw, boolText, intText, floatText;
                reader.TryReadField(out name);
                reader.TryReadField(out raw);
                reader.TryReadField(out boolText);
                reader.TryReadField(out intText);
                reader.TryReadField(out floatText);

                bool boolValue;
                int intValue;
                float floatValue;
                SpanParse.TryParseBoolean(boolText, out boolValue);   // 无法识别为 false
                SpanParse.TryParseInt32(intText, out intValue);       // 失败为 0
                SpanParse.TryParseSingle(floatText, out floatValue);  // 失败为 0
                string configName = name.ToString();
                if (!manager.AddConfig(configName, raw.ToString(), boolValue, intValue, floatValue))
                {
                    FrameworkLog.Warning("ConfigTextParser: duplicate or invalid config '{0}' at line {1}.", configName, lines.LineNumber);
                }
            }
            return true;
        }
    }
}
