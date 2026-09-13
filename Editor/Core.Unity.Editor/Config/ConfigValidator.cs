//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;

namespace EjoyFramework.Core.Unity.Editor.Config
{
    /// <summary>
    /// 配置表数据校验：重复 Id / 类型错误 / 外键引用不存在。
    /// 校验失败返回错误列表（空列表表示通过）。
    /// </summary>
    public static class ConfigValidator
    {
        public sealed class ValidationContext
        {
            /// <summary>表名 → 该表内已知 Id 集合（用于外键检查）。</summary>
            public Dictionary<string, HashSet<int>> AllTableIds = new Dictionary<string, HashSet<int>>();
        }

        /// <summary>
        /// 第一遍：仅收集本表 Id 集合到 ctx，供第二遍跨表外键引用检查。
        /// 必须对<b>所有</b>表先各跑一次 CollectIds，再各跑 Validate——否则字母序在前的表引用在后的表会被静默跳过。
        /// </summary>
        public static void CollectIds(ConfigTableSchema schema, List<string[]> dataRows, ValidationContext ctx)
        {
            int idCol = FindIdColumn(schema);
            if (idCol < 0) return;
            if (!ctx.AllTableIds.TryGetValue(schema.TableName, out var ids))
            {
                ids = new HashSet<int>();
                ctx.AllTableIds[schema.TableName] = ids;
            }
            for (int r = 0; r < dataRows.Count; r++)
            {
                var row = dataRows[r];
                if (idCol < row.Length &&
                    int.TryParse(row[idCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idValue))
                {
                    ids.Add(idValue);
                }
            }
        }

        public static List<string> Validate(ConfigTableSchema schema, List<string[]> dataRows, ValidationContext ctx)
        {
            var errors = new List<string>();
            int idCol = FindIdColumn(schema);
            if (idCol < 0)
            {
                errors.Add("[" + schema.TableName + "] missing Id column.");
                return errors;
            }
            var seenIds = new HashSet<int>();
            for (int r = 0; r < dataRows.Count; r++)
            {
                var row = dataRows[r];
                int realLineNo = r + 3;   // header + types + 1-based
                if (row.Length < schema.Columns.Count)
                {
                    errors.Add("[" + schema.TableName + " row " + realLineNo + "] column count " + row.Length + " < expected " + schema.Columns.Count);
                    continue;
                }
                // Id 解析 + 唯一性
                if (!int.TryParse(row[idCol], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idValue))
                {
                    errors.Add("[" + schema.TableName + " row " + realLineNo + "] Id is not int: '" + row[idCol] + "'");
                    continue;
                }
                if (!seenIds.Add(idValue))
                {
                    errors.Add("[" + schema.TableName + " row " + realLineNo + "] duplicate Id " + idValue);
                }
                // 每列类型检查
                for (int c = 0; c < schema.Columns.Count; c++)
                {
                    var col = schema.Columns[c];
                    var cell = row[c];
                    string err = ValidateCell(col, cell, ctx, schema.TableName, realLineNo);
                    if (err != null) errors.Add(err);
                }
            }
            // 把本表 Id 集合写入 ctx，供后续表外键引用检查
            ctx.AllTableIds[schema.TableName] = seenIds;
            return errors;
        }

        private static int FindIdColumn(ConfigTableSchema s)
        {
            for (int i = 0; i < s.Columns.Count; i++) if (s.Columns[i].IsId) return i;
            return -1;
        }

        private static string ValidateCell(ConfigTableSchema.ColumnSchema col, string cell, ValidationContext ctx, string tableName, int lineNo)
        {
            switch (col.Type.ToLowerInvariant())
            {
                case "int":
                    if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' not int: '" + cell + "'";
                    break;
                case "long":
                    if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' not long: '" + cell + "'";
                    break;
                case "float":
                    if (!float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' not float: '" + cell + "'";
                    break;
                case "bool":
                    if (!bool.TryParse(cell, out _))
                        return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' not bool: '" + cell + "'";
                    break;
                case "reference":
                    if (!int.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rid))
                        return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' reference not int: '" + cell + "'";
                    if (ctx.AllTableIds.TryGetValue(col.ReferenceTarget, out var targetIds))
                    {
                        if (!targetIds.Contains(rid))
                            return "[" + tableName + " row " + lineNo + "] column '" + col.Name + "' references " + col.ReferenceTarget + "." + rid + " which does not exist";
                    }
                    // 目标表未加载也算 warn-not-error（业务可决定）
                    break;
                case "string":
                default:
                    break;   // 任何字符串都合法
            }
            return null;
        }
    }
}
