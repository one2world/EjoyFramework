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
    /// </summary>
    public static class DataTableTextParser
    {
        private static readonly string[] s_LineSeparators = { "\r\n", "\n", "\r" };

        public static bool Parse<T>(IDataTable<T> table, string text, object userData)
            where T : class, IDataRow, new()
        {
            if (table == null) throw new FrameworkException("DataTableTextParser.Parse: table is null.");
            if (string.IsNullOrEmpty(text)) return true;

            string[] lines = text.Split(s_LineSeparators, StringSplitOptions.None);
            int lineNo = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                lineNo++;
                string raw = lines[i];
                if (raw == null) continue;
                // 仅去除行首缩进空白，避免它并入第 0 列；行内 TAB 与列内空白原样保留
                string row = raw.TrimStart();
                // 判空 / 注释仍以两端 Trim 后内容为准
                string trimmed = row.TrimEnd();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                if (!table.AddDataRow(row, userData))
                {
                    FrameworkLog.Warning("DataTableTextParser: ParseDataRow failed or duplicate at line {0} ('{1}').", lineNo, table.FullName);
                }
            }
            return true;
        }
    }
}
