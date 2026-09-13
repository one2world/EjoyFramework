//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EjoyFramework.Core.Blobs;
using EjoyFramework.Core.Unity.Editor.Config;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// ConfigBlob 二进制布局的**唯一真源**：字段排序、行内偏移、行长、表名 hash、schema hash 全在这里算。
    /// Reader（运行时 .g.cs）与 Builder（编辑器 .g.cs）都由本类算出的常量生成，天然同源，不会各算各的。
    ///
    /// 格式契约：
    /// - 小端；
    /// - 行内字段按尺寸 8 → 4 → 2 → 1 降序排列，同尺寸按声明顺序（稳定排序）；
    /// - bool 占 1 字节（0/1）；
    /// - string 在行内占 4 字节，存 StringPool 内的 int 偏移，0 表示空串；
    /// - 行长 = 各字段尺寸之和，再向上补齐到 4 的倍数（ConfigBlobWriter.BeginTable 的硬性要求）；
    /// - 行序保持源表顺序，主键索引由 ConfigBlobWriter 另行按 Id 升序建立，读侧二分走该索引。
    ///
    /// 关于 8 字节字段的对齐：行长只补齐到 4，不补到 8，因此含 long/double 的表里，
    /// 奇数行的 8 字节字段会落在 4 字节边界上（非自然对齐）。这是刻意的取舍——
    /// 补到 8 会让"只有一个 long + 几个 int"的表白白多占 4 字节/行，而读侧一律走
    /// ConfigBlob.ReadInt64/ReadDouble，它们内部按字节拼装，不依赖自然对齐，
    /// 在 ARM64 与 x64 上都不会触发对齐陷阱。若将来读侧改成直接解引用指针，这里必须同步改成 Align8。
    /// </summary>
    public static class ConfigBlobLayout
    {
        /// <summary>
        /// 布局算法版本。**改动任何影响字节布局的规则（排序、尺寸、补齐粒度）都必须 +1**，
        /// 它参与 schemaHash，从而让旧数据文件在 Open 时立刻失败，而不是按新规则静默错读旧字节。
        /// </summary>
        public const int LayoutFormatVersion = 1;

        /// <summary>
        /// 会与生成代码撞名的成员名。列名最终只变成 <c>XxxRow</c> 的成员，所以这里**只列 Row 的自带成员**。
        ///
        /// 刻意不含 Count / Current / Enumerator / GetEnumerator / MoveNext / Reset / GetById / TryGetById：
        /// 那些是 <c>XxxTable</c> 与其嵌套 Enumerator 的成员，列名根本不会落到那两个类型上，把它们列进来
        /// 会误伤——本工程的 Stages 表就有一列叫 Current，它生成的是 StagesRow.Current，与 Enumerator.Current
        /// 分属两个类型，不构成冲突。禁掉它等于让一张在跑的表导不出来。
        /// </summary>
        private static readonly HashSet<string> s_ReservedMemberNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "IsValid", "RowOffset", "m_Blob", "m_RowOffset",
        };

        /// <summary>C# 关键字（含上下文关键字里易撞的那批），不能直接用作标识符。</summary>
        private static readonly HashSet<string> s_CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while",
        };

        /// <summary>字段的二进制种类。</summary>
        public enum FieldKind
        {
            /// <summary>32 位有符号整数（含 reference 外键）。</summary>
            Int32,

            /// <summary>64 位有符号整数。</summary>
            Int64,

            /// <summary>16 位有符号整数。</summary>
            Int16,

            /// <summary>8 位无符号整数。</summary>
            Byte,

            /// <summary>布尔，行内占 1 字节。</summary>
            Bool,

            /// <summary>单精度浮点。</summary>
            Single,

            /// <summary>双精度浮点。</summary>
            Double,

            /// <summary>字符串，行内占 4 字节的 StringPool 偏移。</summary>
            String,
        }

        /// <summary>一个已定位的字段。</summary>
        public sealed class FieldLayout
        {
            /// <summary>字段名（= 生成属性名）。</summary>
            public string Name;

            /// <summary>schema 里的原始类型串（参与 schemaHash 计算）。</summary>
            public string DeclaredType;

            /// <summary>二进制种类。</summary>
            public FieldKind Kind;

            /// <summary>行内字节偏移。</summary>
            public int Offset;

            /// <summary>行内字节尺寸。</summary>
            public int Size;

            /// <summary>该字段在源表里的列下标（Builder 解析时用）。</summary>
            public int SourceColumnIndex;

            /// <summary>是否主键列。</summary>
            public bool IsPrimaryKey;
        }

        /// <summary>一张表的完整布局。</summary>
        public sealed class TableLayout
        {
            /// <summary>表名。</summary>
            public string TableName;

            /// <summary>源文件路径。</summary>
            public string SourcePath;

            /// <summary>按行内偏移升序（即尺寸降序）排列的字段。</summary>
            public List<FieldLayout> Fields = new List<FieldLayout>();

            /// <summary>按源表列顺序排列的字段（生成 SourceRow / 解析代码时用）。</summary>
            public List<FieldLayout> FieldsInDeclarationOrder = new List<FieldLayout>();

            /// <summary>行字节长（已补齐到 4 的倍数，ConfigBlobWriter.BeginTable 的硬性要求）。</summary>
            public int RowSize;

            /// <summary>补齐前的字段总字节数（诊断用）。</summary>
            public int PayloadSize;

            /// <summary>表名 hash（FNV1a64(表名)）。</summary>
            public ulong NameHash;

            /// <summary>本表 schema hash。</summary>
            public ulong SchemaHash;

            /// <summary>主键字段。</summary>
            public FieldLayout PrimaryKey;
        }

        /// <summary>把 ConfigTableSchema 编译成二进制布局。</summary>
        public static TableLayout Build(ConfigTableSchema schema, string sourcePath = null)
        {
            if (schema == null) throw new FrameworkException("ConfigBlobLayout.Build: schema is null.");
            if (schema.Columns == null || schema.Columns.Count == 0)
                throw new FrameworkException("ConfigBlobLayout.Build: table '" + schema.TableName + "' has no columns.");

            var layout = new TableLayout { TableName = schema.TableName, SourcePath = sourcePath };

            RequireUsableIdentifier(schema.TableName, "table name", schema.TableName);

            // 列名按序数去重之外，还要按忽略大小写去重：ColumnSchema.IsId 用的是 OrdinalIgnoreCase，
            // 若同时存在 Id 与 id，两列都会被认成主键，后者静默覆盖前者。
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var seenNamesIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < schema.Columns.Count; i++)
            {
                ConfigTableSchema.ColumnSchema col = schema.Columns[i];
                RequireUsableIdentifier(col.Name, "field", schema.TableName);

                if (!seenNames.Add(col.Name))
                    throw new FrameworkException("ConfigBlobLayout: table '" + schema.TableName + "' has duplicate field '" + col.Name + "'.");
                if (seenNamesIgnoreCase.TryGetValue(col.Name, out string clash))
                    throw new FrameworkException("ConfigBlobLayout: table '" + schema.TableName + "' has fields '" + clash +
                                                 "' and '" + col.Name + "' differing only by case; 'Id' detection is case-insensitive, " +
                                                 "so this would silently pick the wrong primary key. Rename one of them.");
                seenNamesIgnoreCase.Add(col.Name, col.Name);

                FieldKind kind = ToFieldKind(col.Type, schema.TableName, col.Name);
                var field = new FieldLayout
                {
                    Name = col.Name,
                    DeclaredType = DescribeDeclaredType(col),
                    Kind = kind,
                    Size = SizeOf(kind),
                    SourceColumnIndex = i,
                    IsPrimaryKey = col.IsId,
                };
                layout.FieldsInDeclarationOrder.Add(field);
                if (field.IsPrimaryKey)
                {
                    if (kind != FieldKind.Int32)
                        throw new FrameworkException("ConfigBlobLayout: table '" + schema.TableName +
                                                     "' primary key 'Id' must be int, got '" + col.Type + "'.");
                    layout.PrimaryKey = field;
                }
            }

            if (layout.PrimaryKey == null)
                throw new FrameworkException("ConfigBlobLayout: table '" + schema.TableName + "' has no 'Id' column.");

            // 尺寸降序、同尺寸保持声明序（List.Sort 不稳定，故把声明序编进比较键）。
            layout.Fields.AddRange(layout.FieldsInDeclarationOrder);
            layout.Fields.Sort((a, b) =>
            {
                int bySize = b.Size.CompareTo(a.Size);
                return bySize != 0 ? bySize : a.SourceColumnIndex.CompareTo(b.SourceColumnIndex);
            });

            int offset = 0;
            for (int i = 0; i < layout.Fields.Count; i++)
            {
                layout.Fields[i].Offset = offset;
                offset += layout.Fields[i].Size;
            }

            // ConfigBlobWriter.BeginTable 要求 rowSize 为 4 的倍数，行尾在此补齐。
            layout.PayloadSize = offset;
            layout.RowSize = Align4(offset);

            layout.NameHash = Fnv1a64(layout.TableName);
            layout.SchemaHash = ComputeTableSchemaHash(layout);
            return layout;
        }

        /// <summary>
        /// 单表 schema hash 的输入（按声明顺序）：
        /// 表名 → "layout:v{版本}" → "rowSize:{行长}" → 每字段 "字段名:类型@行内偏移"。
        ///
        /// 字段用**声明顺序**而非布局顺序，是为了让"只改字段声明次序"也能被检出（布局顺序对同尺寸字段不敏感）。
        /// 偏移与行长同样入哈希：只有这样，改了排序算法或补齐粒度之后，旧 blob 才会在 Open 时明确报错，
        /// 而不是用新偏移去读旧字节——那种错读没有任何外部症状，是最难查的一类事故。
        /// </summary>
        public static ulong ComputeTableSchemaHash(TableLayout layout)
        {
            var parts = new List<string>(layout.FieldsInDeclarationOrder.Count + 3)
            {
                layout.TableName,
                "layout:v" + LayoutFormatVersion.ToString(CultureInfo.InvariantCulture),
                "rowSize:" + layout.RowSize.ToString(CultureInfo.InvariantCulture),
            };
            for (int i = 0; i < layout.FieldsInDeclarationOrder.Count; i++)
            {
                FieldLayout f = layout.FieldsInDeclarationOrder[i];
                parts.Add(f.Name + ":" + f.DeclaredType + "@" + f.Offset.ToString(CultureInfo.InvariantCulture));
            }
            return ComputeSchemaHash(parts);
        }

        /// <summary>
        /// 多表合并 schema hash：各表按表名序数排序后，把 "表名"、"表hash 十六进制" 依次喂给
        /// ComputeSchemaHash。用"再哈希"而非 xor，避免两表 hash 相同/对换时相互抵消。
        /// </summary>
        public static ulong ComputeBlobSchemaHash(IEnumerable<TableLayout> layouts)
        {
            var ordered = new List<TableLayout>(layouts);
            ordered.Sort((a, b) => string.CompareOrdinal(a.TableName, b.TableName));

            var parts = new List<string>(ordered.Count * 2);
            for (int i = 0; i < ordered.Count; i++)
            {
                parts.Add(ordered[i].TableName);
                parts.Add(ordered[i].SchemaHash.ToString("X16"));
            }
            return ComputeSchemaHash(parts);
        }

        /// <summary>
        /// schema hash 一律转交运行时 <see cref="ConfigBlobHash.ComputeSchemaHash"/>（UTF-16 逐字节 + 0x1F 分隔），
        /// 编辑器侧不另立一份实现——两份实现哪怕当下逐位相同，日后也会各自漂移。
        /// 注意它与 <see cref="ConfigBlobHash.Compute"/>（UTF-8）是两个不同函数，不可混用。
        /// </summary>
        public static ulong ComputeSchemaHash(IList<string> parts)
        {
            var array = new string[parts.Count];
            for (int i = 0; i < parts.Count; i++) array[i] = parts[i] ?? string.Empty;
            return ConfigBlobHash.ComputeSchemaHash(array);
        }

        /// <summary>表名 hash：转交运行时 <see cref="ConfigBlobHash.Compute"/>（FNV-1a 64 位 / UTF-8 字节）。</summary>
        public static ulong Fnv1a64(string value)
        {
            return ConfigBlobHash.Compute(value ?? string.Empty);
        }

        /// <summary>把 n 向上对齐到 4 的倍数。</summary>
        public static int Align4(int n)
        {
            return (n + 3) & ~3;
        }

        /// <summary>字段种类的行内字节尺寸。</summary>
        public static int SizeOf(FieldKind kind)
        {
            switch (kind)
            {
                case FieldKind.Int64:
                case FieldKind.Double:
                    return 8;
                case FieldKind.Int32:
                case FieldKind.Single:
                case FieldKind.String:
                    return 4;
                case FieldKind.Int16:
                    return 2;
                case FieldKind.Byte:
                case FieldKind.Bool:
                    return 1;
                default:
                    throw new FrameworkException("ConfigBlobLayout.SizeOf: unsupported kind " + kind + ".");
            }
        }

        /// <summary>生成代码里该字段暴露的 C# 类型名。</summary>
        public static string ClrTypeName(FieldKind kind)
        {
            switch (kind)
            {
                case FieldKind.Int32: return "int";
                case FieldKind.Int64: return "long";
                case FieldKind.Int16: return "short";
                case FieldKind.Byte: return "byte";
                case FieldKind.Bool: return "bool";
                case FieldKind.Single: return "float";
                case FieldKind.Double: return "double";
                case FieldKind.String: return "string";
                default: throw new FrameworkException("ConfigBlobLayout.ClrTypeName: unsupported kind " + kind + ".");
            }
        }

        /// <summary>表名/字段名必须是合法 C# 标识符，否则生成出来的代码无法编译。</summary>
        public static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;
            for (int i = 1; i < name.Length; i++)
            {
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
            }
            return true;
        }

        /// <summary>是否 C# 关键字（不能直接作标识符）。</summary>
        public static bool IsCSharpKeyword(string name)
        {
            return name != null && s_CSharpKeywords.Contains(name);
        }

        /// <summary>是否与生成的 Row/Table 成员撞名。</summary>
        public static bool IsReservedMemberName(string name)
        {
            return name != null && s_ReservedMemberNames.Contains(name);
        }

        /// <summary>
        /// 名字必须同时满足：合法标识符、非 C# 关键字、不与生成代码的既有成员撞名。
        /// 三条任一不满足都在这里抛，而不是留到生成后由 C# 编译器报一个指不回配置表的错。
        /// </summary>
        private static void RequireUsableIdentifier(string name, string what, string tableName)
        {
            if (!IsValidIdentifier(name))
                throw new FrameworkException("ConfigBlobLayout: table '" + tableName + "' " + what + " '" + name +
                                             "' is not a valid C# identifier.");
            if (IsCSharpKeyword(name))
                throw new FrameworkException("ConfigBlobLayout: table '" + tableName + "' " + what + " '" + name +
                                             "' is a C# keyword; rename it in the source table.");
            if (IsReservedMemberName(name))
                throw new FrameworkException("ConfigBlobLayout: table '" + tableName + "' " + what + " '" + name +
                                             "' collides with a generated member of " + tableName + "Row; rename it in the source table.");

            // C# 不允许成员与其所在类型同名（CS0542）。列名会同时成为 XxxRow 的属性与 XxxSourceRow 的字段，
            // 所以恰好叫 XxxRow / XxxSourceRow 的列会让生成代码编不过，且报错指向 .g.cs 而不是源表。
            if (string.Equals(name, tableName + "Row", StringComparison.Ordinal) ||
                string.Equals(name, tableName + "SourceRow", StringComparison.Ordinal))
                throw new FrameworkException("ConfigBlobLayout: table '" + tableName + "' " + what + " '" + name +
                                             "' has the same name as the generated type that would contain it (CS0542: 成员不能与其所在类型同名); " +
                                             "rename it in the source table.");
        }

        /// <summary>参与 schemaHash 的类型描述：外键列带上目标表名，避免改指向后 hash 不变。</summary>
        private static string DescribeDeclaredType(ConfigTableSchema.ColumnSchema col)
        {
            string normalized = NormalizeDeclaredType(col.Type);
            if (normalized == "reference" && !string.IsNullOrEmpty(col.ReferenceTarget))
                return "reference:" + col.ReferenceTarget.Trim();
            return normalized;
        }

        private static string NormalizeDeclaredType(string declaredType)
        {
            return string.IsNullOrEmpty(declaredType) ? "string" : declaredType.Trim().ToLowerInvariant();
        }

        private static FieldKind ToFieldKind(string declaredType, string tableName, string fieldName)
        {
            switch (NormalizeDeclaredType(declaredType))
            {
                case "int":
                case "reference":
                    return FieldKind.Int32;
                case "long":
                    return FieldKind.Int64;
                case "short":
                    return FieldKind.Int16;
                case "byte":
                    return FieldKind.Byte;
                case "bool":
                    return FieldKind.Bool;
                case "float":
                    return FieldKind.Single;
                case "double":
                    return FieldKind.Double;
                case "string":
                    return FieldKind.String;
                default:
                    throw new FrameworkException("ConfigBlobLayout: table '" + tableName + "' field '" + fieldName +
                                                 "' has unsupported type '" + declaredType + "'.");
            }
        }
    }
}
