//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EjoyFramework.Core.Unity.Editor.Config;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// ConfigBlob 代码生成的 schema 输入来源。不发明新格式，沿用工程里已有的两种表约定：
    ///
    /// 1) CSV 源表（<c>Assets/Configs/Source/*.csv</c>）：第 0 行字段名、第 1 行类型、第 2 行起数据，
    ///    直接复用 <see cref="ConfigTableSchema"/> 与 <see cref="CsvParser"/>。
    ///
    /// 2) 运行时 TAB 表（<c>Assets/GameMain/DataTables/**/*.txt</c>）：以 <c>#</c> 开头的注释行 + TAB 数据行，
    ///    与 <see cref="Core.DataTable.DataTableTextParser"/> 的约定一致。该格式**没有**类型行，因此：
    ///    - 字段名取"最后一条列数与数据行一致的注释行"（现有表的惯例就是这么写的）；
    ///    - 类型由全量数据行推断（bool → int → long → float → string，取能容纳所有取值的最窄类型）；
    ///    - 若某条注释行以 <c>#@types</c> 开头，则其余列被当作显式类型行，跳过推断。
    ///
    /// 两条路径最终都产出 <see cref="ConfigTableSchema"/> + 原始数据行，交给
    /// <see cref="ConfigBlobLayout"/> 计算布局、<see cref="ConfigBlobGenerator"/> 生成代码。
    /// </summary>
    public static class ConfigBlobSchemaSource
    {
        /// <summary>显式类型注释行的前缀（可选，不写则走类型推断）。</summary>
        public const string ExplicitTypeMarker = "@types";

        /// <summary>一张表的解析结果：schema + 未做类型转换的原始列。</summary>
        public sealed class TableSource
        {
            /// <summary>表名（取自文件名，不含扩展名）。</summary>
            public string TableName;

            /// <summary>源文件路径（写进生成文件头，便于溯源）。</summary>
            public string SourcePath;

            /// <summary>字段名 + 类型。</summary>
            public ConfigTableSchema Schema;

            /// <summary>数据行（每行按列切分后的原始字符串）。</summary>
            public List<string[]> Rows = new List<string[]>();

            /// <summary>
            /// 类型是推断来的（源表没写 <c>#@types</c> 行）。推断结果会随数据变化而变化，
            /// 因此生成器要为这类表落一份 .types 边车文件并在下次生成时比对，见 <see cref="ConfigBlobTypeSidecar"/>。
            /// </summary>
            public bool TypesWereInferred;
        }

        /// <summary>按扩展名分派到 TAB 表 / CSV 表解析。</summary>
        public static TableSource Load(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new FrameworkException("ConfigBlobSchemaSource.Load: path is null or empty.");
            string ext = Path.GetExtension(path);
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)) return LoadCsv(path);
            return LoadTabTable(path);
        }

        /// <summary>解析 CSV 源表（字段名行 + 类型行 + 数据行）。</summary>
        public static TableSource LoadCsv(string path)
        {
            string tableName = Path.GetFileNameWithoutExtension(path);
            List<string[]> rows = CsvParser.ParseFile(path);
            ConfigTableSchema schema = ConfigTableSchema.Parse(tableName, rows);

            var source = new TableSource { TableName = tableName, SourcePath = path, Schema = schema };
            for (int i = 2; i < rows.Count; i++) source.Rows.Add(rows[i]);
            return source;
        }

        /// <summary>解析运行时 TAB 表（<c>#</c> 注释头 + TAB 数据行），类型缺省时按数据推断。</summary>
        public static TableSource LoadTabTable(string path)
        {
            string tableName = Path.GetFileNameWithoutExtension(path);
            string text = File.ReadAllText(path, Encoding.UTF8);
            return ParseTabTable(tableName, text, path);
        }

        /// <summary>纯文本入口（测试直接喂字符串，不落盘）。</summary>
        public static TableSource ParseTabTable(string tableName, string text, string sourcePath = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new FrameworkException("ConfigBlobSchemaSource: tableName is null or empty.");

            var source = new TableSource { TableName = tableName, SourcePath = sourcePath };
            var commentRows = new List<string[]>();
            string[] explicitTypes = null;

            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.TrimEnd().Length == 0) continue;

                if (line[0] == '#')
                {
                    string[] commentCols = SplitTab(line.Substring(1));
                    if (commentCols.Length > 0 && commentCols[0].Trim().StartsWith(ExplicitTypeMarker, StringComparison.OrdinalIgnoreCase))
                    {
                        // "#@types" 之后的列即类型；第 0 列本身是标记，用 Id 的类型顶位。
                        explicitTypes = new string[commentCols.Length];
                        explicitTypes[0] = "int";
                        for (int c = 1; c < commentCols.Length; c++) explicitTypes[c] = commentCols[c].Trim();
                        continue;
                    }
                    commentRows.Add(commentCols);
                    continue;
                }

                source.Rows.Add(SplitTab(line));
            }

            if (source.Rows.Count == 0)
                throw new FrameworkException("ConfigBlobSchemaSource: table '" + tableName + "' has no data rows.");

            int columnCount = source.Rows[0].Length;
            for (int i = 1; i < source.Rows.Count; i++)
            {
                if (source.Rows[i].Length != columnCount)
                    throw new FrameworkException("ConfigBlobSchemaSource: table '" + tableName + "' row " + i +
                                                 " has " + source.Rows[i].Length + " columns, expected " + columnCount + ".");
            }

            string[] names = PickHeaderRow(commentRows, columnCount);
            if (names == null)
                throw new FrameworkException("ConfigBlobSchemaSource: table '" + tableName +
                                             "' has no '#' comment header line with " + columnCount +
                                             " identifier cells including an 'Id' column (字段名头缺失或没有 Id 列)。");

            string[] types = explicitTypes;
            if (types != null)
            {
                if (types.Length != columnCount)
                    throw new FrameworkException("ConfigBlobSchemaSource: table '" + tableName + "' 的 '#" + ExplicitTypeMarker +
                                                 "' 行有 " + types.Length + " 列，数据行有 " + columnCount + " 列，对不上。");

                // "#@types" 标记本身占掉了第 0 列，那一列的类型是被顶掉的，只有当第 0 列确实是主键 Id
                // （其类型固定为 int）时这个顶位才成立；否则第 0 列的真实类型就被悄悄丢了。
                if (!string.Equals(names[0], "Id", StringComparison.OrdinalIgnoreCase))
                    throw new FrameworkException("ConfigBlobSchemaSource: table '" + tableName + "' 用了 '#" + ExplicitTypeMarker +
                                                 "' 行，但第 0 列是 '" + names[0] + "' 而非主键 'Id'。该行的第 0 格被标记本身占用，" +
                                                 "只有第 0 列是 Id（类型恒为 int）时才可省略；请把 Id 列移到首列。");
            }
            else
            {
                types = InferTypes(source.Rows, columnCount);
                source.TypesWereInferred = true;
            }

            var headerRows = new List<string[]> { names, types };
            source.Schema = ConfigTableSchema.Parse(tableName, headerRows);
            return source;
        }

        /// <summary>
        /// 取字段名头：**第一条**满足「列数与数据一致、每格都是合法标识符、且其中一格正是主键 Id」的注释行。
        ///
        /// 早先取的是最后一条，会把表末尾的说明性注释（列数碰巧相同）误当成字段名头。
        /// 只加「每格都是合法标识符」还不够——C# 标识符允许 Unicode 字母，中文说明行（如 "说明一	备注"）
        /// 照样能通过。所以再要求必须出现 Id 列：每张表都必须有 Id，而说明文字几乎不可能恰好有一格写着 Id。
        /// </summary>
        private static string[] PickHeaderRow(List<string[]> commentRows, int columnCount)
        {
            for (int i = 0; i < commentRows.Count; i++)
            {
                if (commentRows[i].Length != columnCount) continue;
                var names = new string[columnCount];
                bool valid = true;
                bool hasId = false;
                for (int c = 0; c < columnCount; c++)
                {
                    names[c] = commentRows[i][c].Trim();
                    if (!ConfigBlobLayout.IsValidIdentifier(names[c])) { valid = false; break; }
                    if (string.Equals(names[c], "Id", StringComparison.OrdinalIgnoreCase)) hasId = true;
                }
                if (valid && hasId) return names;
            }
            return null;
        }

        /// <summary>按全量数据推断每列类型：bool → int → long → float → string（取能容纳所有取值的最窄类型）。</summary>
        public static string[] InferTypes(List<string[]> rows, int columnCount)
        {
            var types = new string[columnCount];
            for (int c = 0; c < columnCount; c++)
            {
                bool allBool = true, allInt = true, allLong = true, allFloat = true;
                for (int r = 0; r < rows.Count; r++)
                {
                    string v = rows[r][c].Trim();
                    if (v.Length == 0)
                    {
                        // 空串只能落到 string；数值列不允许留空（缺省值语义未定义）。
                        allBool = allInt = allLong = allFloat = false;
                        break;
                    }
                    if (allBool && !(v.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                     v.Equals("false", StringComparison.OrdinalIgnoreCase))) allBool = false;
                    if (allInt && !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) allInt = false;
                    if (allLong && !long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) allLong = false;
                    if (allFloat && !float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) allFloat = false;
                    if (!allBool && !allInt && !allLong && !allFloat) break;
                }

                if (allBool) types[c] = "bool";
                else if (allInt) types[c] = "int";
                else if (allLong) types[c] = "long";
                else if (allFloat) types[c] = "float";
                else types[c] = "string";
            }
            return types;
        }

        private static string[] SplitTab(string line)
        {
            string[] cols = line.Split('\t');
            // 行首注释符后常带一个空格（"# Id\tCode"），去掉首列的引导空白即可，列内空白原样保留。
            if (cols.Length > 0) cols[0] = cols[0].TrimStart();
            return cols;
        }
    }
}
