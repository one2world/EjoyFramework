//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.DataTable
{
    /// <summary>
    /// 默认数据表文本解析器（核心层，无 Unity 依赖）。
    /// 文本格式：每行一条记录；跳过空行与 # 注释行；
    /// TAB 分隔列由 <see cref="IDataRow.ParseDataRow"/> 自行解析。
    ///
    /// 列契约：仅去除行**首**的缩进空白（空格/制表混合的意外缩进），
    /// 避免它被并入第 0 列；行内 TAB 与列内尾随空白都原样保留交给 ParseDataRow。
    ///
    /// 零分配切行（<see cref="TextLineEnumerator"/>，与旧 Split 同语义、同行号）：行以切片交给
    /// <see cref="IDataTable{T}.AddDataRow(ReadOnlySpan{char}, object)"/>，实现 <see cref="ISpanDataRow"/> 的行
    /// （代码生成器生成的行都是）直接从切片解析，只有字符串列分配；旧式行退回为每行生成一个 string。
    /// </summary>
    public static class DataTableTextParser
    {
        public static bool Parse<T>(IDataTable<T> table, string text, object userData)
            where T : class, IDataRow, new()
        {
            if (table == null) throw new FrameworkException("DataTableTextParser.Parse: table is null.");
            if (string.IsNullOrEmpty(text)) return true;

            TextLineEnumerator lines = new TextLineEnumerator(text.AsSpan());
            while (lines.MoveNext())
            {
                // 仅去除行首缩进空白，避免它并入第 0 列；行内 TAB 与列内空白原样保留
                ReadOnlySpan<char> row = lines.Current.TrimStart();
                // 判空 / 注释仍以两端 Trim 后内容为准
                ReadOnlySpan<char> trimmed = row.TrimEnd();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                if (!table.AddDataRow(row, userData))
                {
                    FrameworkLog.Warning("DataTableTextParser: ParseDataRow failed or duplicate at line {0} ('{1}').", lines.LineNumber, table.FullName);
                }
            }
            return true;
        }
    }
}
