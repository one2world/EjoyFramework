//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.Unity.Editor.Config
{
    /// <summary>
    /// CSV 配置表期望的头部格式：
    /// Row 0: 字段名 (Id,Name,Hp,FireballRefId...)
    /// Row 1: 类型 (int,string,int,reference:FireballSkill...)
    /// Row 2+: 数据
    ///
    /// 类型支持：int / long / float / bool / string / reference:OtherTable
    /// </summary>
    public sealed class ConfigTableSchema
    {
        public string TableName;             // 来自文件名（不含扩展名）
        public List<ColumnSchema> Columns = new List<ColumnSchema>();

        public sealed class ColumnSchema
        {
            public string Name;
            public string Type;              // int / long / float / bool / string / reference:T
            public string ReferenceTarget;   // type == reference:T 时为 T
            public bool IsId => string.Equals(Name, "Id", System.StringComparison.OrdinalIgnoreCase);
        }

        public static ConfigTableSchema Parse(string tableName, List<string[]> rows)
        {
            if (rows == null || rows.Count < 2)
                throw new FrameworkException("ConfigTableSchema.Parse: at least 2 header rows (name + type) required.");
            var headers = rows[0];
            var types = rows[1];
            if (headers.Length != types.Length)
                throw new FrameworkException("ConfigTableSchema: header / type column count mismatch.");

            var schema = new ConfigTableSchema { TableName = tableName };
            for (int i = 0; i < headers.Length; i++)
            {
                var col = new ColumnSchema
                {
                    Name = headers[i].Trim(),
                    Type = types[i].Trim(),
                };
                if (col.Type.StartsWith("reference:", System.StringComparison.OrdinalIgnoreCase))
                {
                    col.ReferenceTarget = col.Type.Substring("reference:".Length).Trim();
                    col.Type = "reference";
                }
                schema.Columns.Add(col);
            }
            if (!HasIdColumn(schema)) throw new FrameworkException("ConfigTableSchema: must have an 'Id' column.");
            return schema;
        }

        public static bool HasIdColumn(ConfigTableSchema s)
        {
            foreach (var c in s.Columns) if (c.IsId) return true;
            return false;
        }
    }
}
