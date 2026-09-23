//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using EjoyFramework.Core;
using EjoyFramework.Core.Blobs;
using EjoyFramework.Core.Unity.Editor.CodeGen;
using NUnit.Framework;
using Debug = UnityEngine.Debug;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// ConfigBlob 代码生成（第 7 条 codegen 线）的验证。
    ///
    /// 生成出来的 .g.cs 在测试运行时还没进入编译，所以这里不直接调用生成代码，而是用
    /// <see cref="ConfigBlobLayout"/> 算出的**同一批常量**驱动真实的 ConfigBlobWriter / ConfigBlob，
    /// 逐字段与文本解析结果对照；生成代码把这些常量内联成字面量，语义等价。
    /// 另有一组测试对生成源码做结构断言（偏移常量取值、读写两侧引用同一 token、Set 调用齐全），
    /// 以及一组 Run() 级测试覆盖落盘、跳过、孤儿清理与重名拒绝。
    /// </summary>
    public sealed class ConfigBlobCodegenTests
    {
        private static string MonstersTablePath => Path.Combine(
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ConfigBlobCodegenTests).Assembly).resolvedPath,
            "Tests/Editor/Core.Tests/Fixtures/Monsters.txt");

        /// <summary>覆盖全部字段种类的合成表（真实表里没有 long/double/bool 的组合）。</summary>
        private const string SyntheticTableText =
            "# 合成表，覆盖全部字段种类\n" +
            "#@types\tstring\tlong\tdouble\tbool\tfloat\n" +
            "# Id\tName\tBigValue\tPrecise\tEnabled\tRatio\n" +
            "3\t丙\t9223372036854775807\t3.141592653589793\ttrue\t0.5\n" +
            "1\t甲\t-1\t-2.5\tfalse\t1.25\n" +
            "2\t\t0\t0\ttrue\t-0.75\n";

        private readonly List<string> m_TempDirs = new List<string>();

        [TearDown]
        public void CleanupTempDirs()
        {
            for (int i = 0; i < m_TempDirs.Count; i++)
            {
                try { if (Directory.Exists(m_TempDirs[i])) Directory.Delete(m_TempDirs[i], true); }
                catch (IOException) { /* 临时目录清理失败不该让测试红掉 */ }
            }
            m_TempDirs.Clear();
        }

        // ================================================================
        //  哈希转交
        // ================================================================

        [Test]
        public void NameHash_MatchesRuntimeConfigBlobHash()
        {
            // ConfigBlobLayout 的哈希已直接转交 ConfigBlobHash，这两条断言守的是「转交时没被包装层改坏」
            // （null 归一成空串、parts 顺序原样透传），而不是两份独立实现的对拍。
            string[] samples = { "Monsters", "Heroes", "Stages", "陈塘关", "" };
            foreach (string sample in samples)
            {
                Assert.AreEqual(ConfigBlobHash.Compute(sample), ConfigBlobLayout.Fnv1a64(sample),
                    "表名 hash 与运行时不一致：" + sample);
            }
            Assert.AreEqual(ConfigBlobHash.Compute(string.Empty), ConfigBlobLayout.Fnv1a64(null),
                "null 表名应按空串处理，而不是抛出。");
        }

        [Test]
        public void SchemaHash_PassesPartsThroughInOrder()
        {
            var parts = new List<string> { "Monsters", "Id:int", "Name:string", "Hp:int" };
            Assert.AreEqual(ConfigBlobHash.ComputeSchemaHash(parts.ToArray()),
                ConfigBlobLayout.ComputeSchemaHash(parts));

            var swapped = new List<string> { "Monsters", "Name:string", "Id:int", "Hp:int" };
            Assert.AreNotEqual(ConfigBlobLayout.ComputeSchemaHash(parts),
                ConfigBlobLayout.ComputeSchemaHash(swapped));
        }

        [Test]
        public void SchemaHash_CoversLayoutNotJustFieldNames()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            ulong baseline = layout.SchemaHash;

            // 偏移变了 hash 必须变，否则改了排序算法之后旧 blob 会被按新偏移静默错读。
            int savedOffset = layout.FieldsInDeclarationOrder[1].Offset;
            layout.FieldsInDeclarationOrder[1].Offset = savedOffset + 4;
            Assert.AreNotEqual(baseline, ConfigBlobLayout.ComputeTableSchemaHash(layout), "偏移变化未反映进 schemaHash。");
            layout.FieldsInDeclarationOrder[1].Offset = savedOffset;

            // 行长变了同理（补齐粒度改动会走到这条）。
            layout.RowSize += 4;
            Assert.AreNotEqual(baseline, ConfigBlobLayout.ComputeTableSchemaHash(layout), "行长变化未反映进 schemaHash。");
        }

        // ================================================================
        //  布局
        // ================================================================

        [Test]
        public void Layout_SortsFieldsBySizeDescending_AndPadsRowToMultipleOfFour()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();

            int previousSize = int.MaxValue;
            int expectedOffset = 0;
            for (int i = 0; i < layout.Fields.Count; i++)
            {
                ConfigBlobLayout.FieldLayout field = layout.Fields[i];
                Assert.LessOrEqual(field.Size, previousSize, "字段未按尺寸降序排列。");
                Assert.AreEqual(expectedOffset, field.Offset, "字段偏移不连续：" + field.Name);
                previousSize = field.Size;
                expectedOffset += field.Size;
            }

            // long(8) + double(8) + string(4) + float(4) + int(4) + bool(1) = 29 -> 补齐到 32
            Assert.AreEqual(29, layout.PayloadSize);
            Assert.AreEqual(32, layout.RowSize);
            Assert.AreEqual(0, layout.RowSize % 4, "rowSize 必须是 4 的倍数，否则 BeginTable 会拒绝。");
        }

        [Test]
        public void Layout_RejectsNonIntPrimaryKey()
        {
            ConfigBlobSchemaSource.TableSource bad = ConfigBlobSchemaSource.ParseTabTable(
                "BadPk", "# Id\tName\nx\ty\n");
            Assert.AreEqual("string", bad.Schema.Columns[0].Type);
            Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(bad.Schema));
        }

        [Test]
        public void Layout_RejectsUnknownFieldType()
        {
            ConfigBlobSchemaSource.TableSource bad = ConfigBlobSchemaSource.ParseTabTable(
                "BadType", "#@types\tvector3\n# Id\tPos\n1\t0,0,0\n");
            Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(bad.Schema));
        }

        [Test]
        public void Layout_RejectsRaggedRows()
        {
            Assert.Throws<FrameworkException>(() =>
                ConfigBlobSchemaSource.ParseTabTable("Ragged", "# Id\tName\n1\ta\n2\n"));
        }

        [Test]
        public void Layout_RejectsDuplicateColumnNames()
        {
            ConfigBlobSchemaSource.TableSource dup = ConfigBlobSchemaSource.ParseTabTable(
                "Dup", "#@types\tstring\tstring\n# Id\tName\tName\n1\ta\tb\n");
            FrameworkException ex = Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(dup.Schema));
            StringAssert.Contains("duplicate field", ex.Message);
        }

        [Test]
        public void Layout_RejectsColumnNamesDifferingOnlyByCase()
        {
            // Id 判定是 OrdinalIgnoreCase 的，Id 与 id 并存会让主键指向后一列，且全程无提示。
            ConfigBlobSchemaSource.TableSource clash = ConfigBlobSchemaSource.ParseTabTable(
                "Clash", "#@types\tint\n# Id\tid\n1\t2\n");
            FrameworkException ex = Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(clash.Schema));
            StringAssert.Contains("differing only by case", ex.Message);
        }

        [Test]
        public void Layout_RejectsMissingIdColumn()
        {
            // ConfigTableSchema 自己就要求有 Id 列，这条守的是这道防线没被绕过。
            Assert.Throws<FrameworkException>(() =>
                ConfigBlobSchemaSource.ParseTabTable("NoId", "# Name\tHp\na\t1\n"));
        }

        [Test]
        public void Layout_RejectsKeywordAndReservedNames()
        {
            ConfigBlobSchemaSource.TableSource keyword = ConfigBlobSchemaSource.ParseTabTable(
                "Kw", "#@types\tint\n# Id\tclass\n1\t2\n");
            StringAssert.Contains("C# keyword",
                Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(keyword.Schema)).Message);

            ConfigBlobSchemaSource.TableSource reserved = ConfigBlobSchemaSource.ParseTabTable(
                "Rs", "#@types\tint\n# Id\tRowOffset\n1\t2\n");
            StringAssert.Contains("collides with a generated member",
                Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(reserved.Schema)).Message);
        }

        [Test]
        public void Layout_AllowsColumnNamesThatOnlyClashWithTableMembers()
        {
            // 列名只会变成 XxxRow 的成员；Current/Count 是 XxxTable 与 Enumerator 的成员，不构成冲突。
            // 本工程的 Stages 表就有一列叫 Current——禁掉它会让一张在跑的表直接导不出来。
            ConfigBlobSchemaSource.TableSource ok = ConfigBlobSchemaSource.ParseTabTable(
                "Ok", "#@types\tbool\tint\n# Id\tCurrent\tCount\n1\ttrue\t2\n");
            Assert.DoesNotThrow(() => ConfigBlobLayout.Build(ok.Schema));
        }

        [Test]
        public void SchemaSource_InfersTypesFromData()
        {
            ConfigBlobSchemaSource.TableSource source = LoadMonstersOrIgnore();

            Assert.AreEqual("Monsters", source.TableName);
            Assert.IsTrue(source.TypesWereInferred, "Monsters 没写 #@types，应标记为推断。");
            AssertColumnType(source, "Id", "int");
            AssertColumnType(source, "Code", "string");
            AssertColumnType(source, "Hp", "int");
            AssertColumnType(source, "Speed", "float");
        }

        [Test]
        public void SchemaSource_PicksFirstIdentifierOnlyCommentRowAsHeader()
        {
            // 表尾的说明性注释列数可能碰巧相同，取「第一条全是合法标识符的注释行」才不会被它带偏。
            ConfigBlobSchemaSource.TableSource source = ConfigBlobSchemaSource.ParseTabTable(
                "T", "# 说明一\t备注\n# Id\tName\n1\ta\n# 表尾说明\t两列注释\n");
            Assert.AreEqual("Id", source.Schema.Columns[0].Name);
            Assert.AreEqual("Name", source.Schema.Columns[1].Name);
        }

        [Test]
        public void SchemaSource_RejectsExplicitTypesRowWhenFirstColumnIsNotId()
        {
            // 「#@types」标记占掉第 0 格，那一列的类型只能靠「第 0 列必是 Id（类型恒为 int）」顶位。
            // Id 存在但不在首列时，第 0 列的真实类型就被悄悄丢了，必须拒绝。
            FrameworkException ex = Assert.Throws<FrameworkException>(() =>
                ConfigBlobSchemaSource.ParseTabTable("T", "#@types\tint\n# Name\tId\nx\t1\n"));
            StringAssert.Contains("而非主键", ex.Message);
        }

        [Test]
        public void TypeSidecar_ReportsInferredTypeDrift()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            var previous = new List<ConfigBlobTypeSidecar.ColumnType>
            {
                new ConfigBlobTypeSidecar.ColumnType { Name = "Id", Type = "int" },
                new ConfigBlobTypeSidecar.ColumnType { Name = "Ratio", Type = "int" },
            };

            List<string> drift = ConfigBlobTypeSidecar.Diff(layout, previous);
            string joined = string.Join("\n", drift.ToArray());
            StringAssert.Contains("Ratio", joined);
            StringAssert.Contains("int", joined);
            StringAssert.Contains("float", joined);

            // 没有上次快照时不该报任何东西（首次生成）。
            Assert.IsEmpty(ConfigBlobTypeSidecar.Diff(layout, new List<ConfigBlobTypeSidecar.ColumnType>()));
        }

        // ================================================================
        //  生成源码的结构断言
        // ================================================================

        [Test]
        public void Generator_EmitsSchemaReaderBuilderAndAggregateFiles()
        {
            Dictionary<string, CodeBuilder> files = BuildSyntheticFiles();

            Assert.IsTrue(files.ContainsKey("RuntimeDir/ConfigBlobSchema.g.cs"));
            Assert.IsTrue(files.ContainsKey("RuntimeDir/SynthTable.g.cs"));
            Assert.IsTrue(files.ContainsKey("EditorDir/SynthTableBuilder.g.cs"));
            Assert.IsTrue(files.ContainsKey("EditorDir/ConfigBlobBuild.g.cs"));

            string reader = files["RuntimeDir/SynthTable.g.cs"].ToString();
            StringAssert.Contains("DO NOT EDIT", reader);
            StringAssert.Contains("public readonly partial struct SynthRow", reader);
            StringAssert.Contains("public readonly partial struct SynthTable", reader);
            StringAssert.Contains("public BlobStringHandle Name =>", reader);
            StringAssert.Contains("m_Blob.ReadBoolean(", reader);
            StringAssert.Contains("public bool TryGetById(int id, out SynthRow row)", reader);
            StringAssert.Contains("public Enumerator GetEnumerator()", reader);

            string builder = files["EditorDir/SynthTableBuilder.g.cs"].ToString();
            StringAssert.Contains("public partial struct SynthSourceRow", builder);
            StringAssert.Contains("public static readonly string[] SourceColumns", builder);
            StringAssert.Contains("writer.BeginTable(ConfigBlobSchema.SynthTableName, ConfigBlobSchema.SynthRowSize);", builder);
            StringAssert.Contains("int rowIndex = writer.AddRow(row.Id);", builder);
            StringAssert.Contains("writer.EndTable();", builder);

            string aggregate = files["EditorDir/ConfigBlobBuild.g.cs"].ToString();
            StringAssert.Contains("public static byte[] Build(string sourceDirectory = null)", aggregate);
            StringAssert.Contains("new ConfigBlobWriter(ConfigBlobSchema.SchemaHash)", aggregate);
            StringAssert.Contains("SynthTableBuilder.Write(", aggregate);
            StringAssert.Contains("public static int WriteToFile(", aggregate);
        }

        [Test]
        public void Generator_EmitsExactOffsetConstantForEveryField()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            string schema = BuildSyntheticFiles()["RuntimeDir/ConfigBlobSchema.g.cs"].ToString();

            for (int i = 0; i < layout.Fields.Count; i++)
            {
                ConfigBlobLayout.FieldLayout field = layout.Fields[i];
                string expected = "public const int Synth_" + field.Name + "_Offset = " +
                                  field.Offset.ToString(CultureInfo.InvariantCulture) + ";";
                StringAssert.Contains(expected, schema, "字段 " + field.Name + " 的偏移常量不正确。");
            }

            StringAssert.Contains("public const int SynthRowSize = " +
                layout.RowSize.ToString(CultureInfo.InvariantCulture) + ";", schema);
            StringAssert.Contains("public const ulong SynthNameHash = 0x" + layout.NameHash.ToString("X16") + "UL;", schema);
        }

        [Test]
        public void Generator_ReaderAndBuilderReferenceTheSameOffsetToken()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            Dictionary<string, CodeBuilder> files = BuildSyntheticFiles();
            string reader = files["RuntimeDir/SynthTable.g.cs"].ToString();
            string builder = files["EditorDir/SynthTableBuilder.g.cs"].ToString();

            for (int i = 0; i < layout.Fields.Count; i++)
            {
                string token = "ConfigBlobSchema.Synth_" + layout.Fields[i].Name + "_Offset";
                StringAssert.Contains(token, reader, "读侧未引用 " + token);
                StringAssert.Contains(token, builder, "写侧未引用 " + token);
            }
        }

        [Test]
        public void Generator_BuilderWritesEveryFieldExactlyOnce()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            string builder = BuildSyntheticFiles()["EditorDir/SynthTableBuilder.g.cs"].ToString();

            Assert.AreEqual(layout.Fields.Count, CountOccurrences(builder, "writer.Set"),
                "writer.SetXxx 的调用数应与字段数一致——少一次就是有列没写进 blob。");

            for (int i = 0; i < layout.Fields.Count; i++)
            {
                ConfigBlobLayout.FieldLayout field = layout.Fields[i];
                string call = "writer." + ConfigBlobBuilderEmitter.WriterSetMethod(field.Kind) +
                              "(rowIndex, ConfigBlobSchema.Synth_" + field.Name + "_Offset, row." + field.Name + ");";
                StringAssert.Contains(call, builder);
            }
        }

        [Test]
        public void Generator_BakesSourceColumnsForHeaderValidation()
        {
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            string builder = BuildSyntheticFiles()["EditorDir/SynthTableBuilder.g.cs"].ToString();

            var expected = new List<string>();
            for (int i = 0; i < layout.FieldsInDeclarationOrder.Count; i++)
                expected.Add("\"" + layout.FieldsInDeclarationOrder[i].Name + "\"");

            StringAssert.Contains("{ " + string.Join(", ", expected.ToArray()) + " };", builder);
            StringAssert.Contains("TryVerifyHeader", builder);
            StringAssert.Contains("if (cols.Length != ColumnCount)", builder);
        }

        [Test]
        public void Generator_HeaderRecognitionMatchesBetweenGeneratorAndGeneratedCode()
        {
            // 两侧判据必须同源：生成器侧 PickHeaderRow 要求「含主键列」，生成代码侧 TryVerifyHeader
            // 若少了这条，中文说明行（每格都是合法标识符，因为 C# 允许 Unicode 字母）在生成期被跳过、
            // 导表期却被当成表头，于是同一个文件生成得出来、导表报「表头被改」——诊断完全指错方向。
            ConfigBlobLayout.TableLayout layout = BuildSyntheticLayout();
            string builder = BuildSyntheticFiles()["EditorDir/SynthTableBuilder.g.cs"].ToString();

            StringAssert.Contains("public const int PrimaryKeyColumnIndex = " +
                layout.PrimaryKey.SourceColumnIndex.ToString(CultureInfo.InvariantCulture) + ";", builder);
            StringAssert.Contains("cells[PrimaryKeyColumnIndex], SourceColumns[PrimaryKeyColumnIndex]", builder);

            // 生成器侧对同一份「中文说明行 + 真表头」的输入，选中的必须是真表头。
            ConfigBlobSchemaSource.TableSource parsed = ConfigBlobSchemaSource.ParseTabTable(
                "T", "# 说明一\t备注\n# Id\tName\n1\ta\n");
            Assert.AreEqual("Id", parsed.Schema.Columns[0].Name);
        }

        [Test]
        public void Layout_RejectsColumnNamedAfterItsOwnGeneratedType()
        {
            // CS0542：成员不能与其所在类型同名。列名恰为 <表名>Row / <表名>SourceRow 时生成代码编不过，
            // 且编译错误指向 .g.cs 而不是源表，排查起来完全没有线索。
            ConfigBlobSchemaSource.TableSource rowClash = ConfigBlobSchemaSource.ParseTabTable(
                "Foo", "#@types\tint\n# Id\tFooRow\n1\t2\n");
            StringAssert.Contains("CS0542",
                Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(rowClash.Schema)).Message);

            ConfigBlobSchemaSource.TableSource sourceRowClash = ConfigBlobSchemaSource.ParseTabTable(
                "Foo", "#@types\tint\n# Id\tFooSourceRow\n1\t2\n");
            Assert.Throws<FrameworkException>(() => ConfigBlobLayout.Build(sourceRowClash.Schema));
        }

        [Test]
        public void Generator_IsIdempotent()
        {
            string first = BuildSyntheticFiles()["RuntimeDir/SynthTable.g.cs"].ToString();
            string second = BuildSyntheticFiles()["RuntimeDir/SynthTable.g.cs"].ToString();
            Assert.AreEqual(first, second);
        }

        // ================================================================
        //  Run() 级：落盘 / 跳过 / 孤儿清理 / 重名拒绝
        // ================================================================

        [Test]
        public void Run_WritesFilesAndReportsUnchangedOnSecondPass()
        {
            string dir = NewTempDir();
            string tables = Path.Combine(dir, "tables");
            string outDir = Path.Combine(dir, "out");
            string sidecarDir = Path.Combine(dir, "sidecar");
            Directory.CreateDirectory(tables);
            WriteTable(tables, "Alpha", "# Id\tName\n1\ta\n2\tb\n");

            var sources = new List<string> { Path.Combine(tables, "Alpha.txt") };
            CodeGenResult first = new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();
            Assert.Greater(first.Written, 0);
            Assert.AreEqual(0, first.Skipped);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "AlphaTable.g.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "AlphaTableBuilder.g.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "ConfigBlobSchema.g.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "ConfigBlobBuild.g.cs")));

            CodeGenResult second = new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();
            Assert.AreEqual(0, second.Written, "内容未变时不应重写（会触发无谓重编译）。");
            Assert.Greater(second.Unchanged, 0);
        }

        [Test]
        public void Run_CountsSkippedTablesAndKeepsGoing()
        {
            string dir = NewTempDir();
            string tables = Path.Combine(dir, "tables");
            string outDir = Path.Combine(dir, "out");
            string sidecarDir = Path.Combine(dir, "sidecar");
            Directory.CreateDirectory(tables);
            WriteTable(tables, "Good", "# Id\tName\n1\ta\n");
            WriteTable(tables, "Bad", "# Id\tName\nx\ta\n"); // Id 推断成 string -> 拒绝

            var sources = new List<string> { Path.Combine(tables, "Good.txt"), Path.Combine(tables, "Bad.txt") };
            CodeGenResult result = new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();

            Assert.AreEqual(1, result.Skipped);
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "GoodTable.g.cs")), "坏表不应连累好表。");
            Assert.IsFalse(File.Exists(Path.Combine(outDir, "BadTable.g.cs")));
            Assert.IsTrue(string.Join("\n", result.Messages).Contains("Bad"), "跳过原因里应能看到是哪张表。");
        }

        [Test]
        public void Run_DeletesOrphanGeneratedFiles()
        {
            string dir = NewTempDir();
            string tables = Path.Combine(dir, "tables");
            string outDir = Path.Combine(dir, "out");
            string sidecarDir = Path.Combine(dir, "sidecar");
            Directory.CreateDirectory(tables);
            WriteTable(tables, "Alpha", "# Id\tName\n1\ta\n");

            var sources = new List<string> { Path.Combine(tables, "Alpha.txt") };
            new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();

            // 模拟「表被删名/改名后遗留的旧产物」。
            string ghost = Path.Combine(outDir, "GhostTable.g.cs");
            File.WriteAllText(ghost, "// stale", Encoding.UTF8);
            File.WriteAllText(ghost + ".meta", "guid: stale", Encoding.UTF8);

            new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();

            Assert.IsFalse(File.Exists(ghost), "孤儿 .g.cs 应被删除，否则会继续编译并引用已不存在的表。");
            Assert.IsFalse(File.Exists(ghost + ".meta"), "孤儿 .meta 也要一并删掉。");
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "AlphaTable.g.cs")));
            Assert.IsTrue(File.Exists(Path.Combine(outDir, "ConfigBlobSchema.g.cs")), "schema 是本次产物，不能被当孤儿删掉。");
        }

        [Test]
        public void Run_RejectsDuplicateTableNamesAsAGroup()
        {
            string dir = NewTempDir();
            string a = Path.Combine(dir, "a");
            string b = Path.Combine(dir, "b");
            string outDir = Path.Combine(dir, "out");
            string sidecarDir = Path.Combine(dir, "sidecar");
            Directory.CreateDirectory(a);
            Directory.CreateDirectory(b);
            WriteTable(a, "Same", "# Id\tName\n1\ta\n");
            WriteTable(b, "Same", "# Id\tOther\n1\tb\n");

            var sources = new List<string> { Path.Combine(a, "Same.txt"), Path.Combine(b, "Same.txt") };
            CodeGenResult result = new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();

            Assert.AreEqual(2, result.Skipped, "重名的两张表都应被跳过，而不是让后者静默覆盖前者。");
            Assert.IsFalse(File.Exists(Path.Combine(outDir, "SameTable.g.cs")));

            string messages = string.Join("\n", result.Messages);
            StringAssert.Contains("表名重复", messages);
            StringAssert.Contains(a.Replace('\\', '/'), messages, "两个冲突路径都要报出来。");
            StringAssert.Contains(b.Replace('\\', '/'), messages);
        }

        [Test]
        public void Run_WritesTypeSidecarAndReportsDrift()
        {
            string dir = NewTempDir();
            string tables = Path.Combine(dir, "tables");
            string outDir = Path.Combine(dir, "out");
            string sidecarDir = Path.Combine(dir, "sidecar");
            Directory.CreateDirectory(tables);
            WriteTable(tables, "Alpha", "# Id\tScore\n1\t10\n");

            var sources = new List<string> { Path.Combine(tables, "Alpha.txt") };
            new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();

            string sidecar = Path.Combine(sidecarDir, "Alpha" + ConfigBlobTypeSidecar.Extension);
            Assert.IsTrue(File.Exists(sidecar), "推断类型的表应落 .types 快照。");
            StringAssert.Contains("Score\tint", File.ReadAllText(sidecar, Encoding.UTF8));

            // 快照不能落在源表旁边——Assets 里的任何文件都会被 Unity 导入成资源并可能进包。
            Assert.IsFalse(File.Exists(Path.Combine(tables, "Alpha" + ConfigBlobTypeSidecar.Extension)),
                "快照不应写在源表目录里。");
            StringAssert.StartsWith("ProjectSettings/", ConfigBlobTypeSidecar.DefaultRootDirectory,
                "默认存放位置必须在 Assets 之外。");

            // 数据里出现小数 -> 推断变成 float -> 应报出漂移。
            WriteTable(tables, "Alpha", "# Id\tScore\n1\t10.5\n");
            CodeGenResult drifted = new ConfigBlobGenerator(sources, outDir, outDir, sidecarDir).Run();
            string messages = string.Join("\n", drifted.Messages);
            StringAssert.Contains("类型推断变化", messages);
            StringAssert.Contains("Score", messages);
        }

        // ================================================================
        //  端到端：真实表 文本 -> blob -> 逐字段对照
        // ================================================================

        [Test]
        public void EndToEnd_MonstersRoundTripsThroughBlob()
        {
            ConfigBlobSchemaSource.TableSource source = LoadMonstersOrIgnore();
            ConfigBlobLayout.TableLayout layout = ConfigBlobLayout.Build(source.Schema, source.SourcePath);

            byte[] bytes = BuildBlob(layout, source.Rows);
            ulong schemaHash = ConfigBlobLayout.ComputeBlobSchemaHash(new[] { layout });
            ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(bytes), schemaHash);
            try
            {
                Assert.IsTrue(blob.TryGetTable(layout.NameHash, out ConfigTableView view));
                Assert.AreEqual(source.Rows.Count, view.RowCount);
                Assert.AreEqual(layout.RowSize, view.RowSize);

                for (int r = 0; r < source.Rows.Count; r++)
                {
                    AssertRowEquals(blob, layout, view.GetRowOffsetByIndex(r), source.Rows[r]);
                }

                for (int r = 0; r < source.Rows.Count; r++)
                {
                    int id = int.Parse(source.Rows[r][layout.PrimaryKey.SourceColumnIndex], CultureInfo.InvariantCulture);
                    int offset = view.FindRowOffset(id);
                    Assert.GreaterOrEqual(offset, 0, "主键 " + id + " 查不到。");
                    AssertRowEquals(blob, layout, offset, source.Rows[r]);
                }

                Assert.Less(view.FindRowOffset(int.MinValue), 0, "不存在的主键应返回负偏移。");
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void EndToEnd_SyntheticTableCoversAllFieldKinds()
        {
            ConfigBlobSchemaSource.TableSource source = ConfigBlobSchemaSource.ParseTabTable("Synth", SyntheticTableText);
            ConfigBlobLayout.TableLayout layout = ConfigBlobLayout.Build(source.Schema);

            byte[] bytes = BuildBlob(layout, source.Rows);
            ConfigBlob blob = ConfigBlob.Open(
                new NativeAllocBlobSource(bytes), ConfigBlobLayout.ComputeBlobSchemaHash(new[] { layout }));
            try
            {
                Assert.IsTrue(blob.TryGetTable(layout.NameHash, out ConfigTableView view));

                for (int r = 0; r < source.Rows.Count; r++)
                {
                    AssertRowEquals(blob, layout, view.GetRowOffsetByIndex(r), source.Rows[r]);
                }

                // 源表首行 Id=3，写入顺序不变；主键索引另行按升序建立，两条路径都要对得上。
                Assert.AreEqual(3, blob.ReadInt32(view.GetRowOffsetByIndex(0) + FieldOf(layout, "Id").Offset));
                Assert.AreEqual(view.GetRowOffsetByIndex(1), view.FindRowOffset(1));

                // 空串必须回读成空 handle（行内存 0 偏移）。
                int emptyRowOffset = view.FindRowOffset(2);
                Assert.IsTrue(blob.ReadString(emptyRowOffset + FieldOf(layout, "Name").Offset).IsEmpty);
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void Blob_RejectsMismatchedSchemaHash()
        {
            ConfigBlobSchemaSource.TableSource source = ConfigBlobSchemaSource.ParseTabTable("Synth", SyntheticTableText);
            ConfigBlobLayout.TableLayout layout = ConfigBlobLayout.Build(source.Schema);
            byte[] bytes = BuildBlob(layout, source.Rows);

            Assert.Throws<FrameworkException>(
                () => ConfigBlob.Open(new NativeAllocBlobSource(bytes), 0xDEADBEEFUL));
        }

        // ================================================================
        //  基准：文本解析 vs blob（只打日志，不做硬断言，避免机器差异抖 CI）
        // ================================================================

        [Test]
        [Category("Benchmark")]
        public void Benchmark_TextParseVersusBlobLoad()
        {
            if (!File.Exists(MonstersTablePath))
            {
                Assert.Fail("Missing committed fixture: " + MonstersTablePath);
            }

            string text = File.ReadAllText(MonstersTablePath, Encoding.UTF8);
            ConfigBlobSchemaSource.TableSource source = ConfigBlobSchemaSource.ParseTabTable("Monsters", text, MonstersTablePath);
            ConfigBlobLayout.TableLayout layout = ConfigBlobLayout.Build(source.Schema, source.SourcePath);
            byte[] bytes = BuildBlob(layout, source.Rows);

            // 循环体外算好，否则两侧都在测哈希而不是测加载。
            ulong schemaHash = ConfigBlobLayout.ComputeBlobSchemaHash(new[] { layout });

            // 先跑一遍 warmup 把 JIT 与首次分配摊掉，再按"总时长不少于 MinMillis"自适应迭代次数：
            // 小表单次只有几微秒，固定 200 次的话总时长落在亚毫秒级，量到的基本是噪声。
            const double MinMillis = 200.0;
            RunTextPass(text, 20);
            RunBlobPass(bytes, schemaHash, layout, 20);

            int iterations = CalibrateIterations(MinMillis,
                n => RunTextPass(text, n), n => RunBlobPass(bytes, schemaHash, layout, n));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long textBefore = GC.GetAllocatedBytesForCurrentThread();
            var textWatch = Stopwatch.StartNew();
            long textSink = RunTextPass(text, iterations);
            textWatch.Stop();
            long textAlloc = GC.GetAllocatedBytesForCurrentThread() - textBefore;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long blobBefore = GC.GetAllocatedBytesForCurrentThread();
            var blobWatch = Stopwatch.StartNew();
            long blobSink = RunBlobPass(bytes, schemaHash, layout, iterations);
            blobWatch.Stop();
            long blobAlloc = GC.GetAllocatedBytesForCurrentThread() - blobBefore;

            // blob 的行数据在托管堆之外（NativeAllocBlobSource 用 AllocHGlobal），
            // 托管分配量不体现它，单独按 blob 体积 x 次数记出来，免得读成「零成本」。
            long blobNative = (long)bytes.Length * iterations;

            // 计数器自校准：GC.GetAllocatedBytesForCurrentThread 在 Unity 编辑器的 Mono/Boehm
            // 运行时下是死计数器（恒零）。分配一块已知大小验证它活着；死了就明说，
            // 否则"两侧都 0 B"会被读成"文本解析零分配"这种荒谬结论。真实的分配断言
            // 见 ConfigBlobZeroGcTests（走 Profiler 的 GC.Alloc 事件，Boehm 下有效）。
            long calibrateBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] calibration = new byte[64 * 1024];
            calibration[0] = 1;
            bool allocCounterAlive = GC.GetAllocatedBytesForCurrentThread() - calibrateBefore >= 64 * 1024;
            GC.KeepAlive(calibration);

            string allocReport = allocCounterAlive
                ? string.Format(
                    "文本路径托管分配 {0} B；blob 路径托管分配 {1} B + 堆外 {2} B（{3} B/次）；分配量之比 {4:F0}x",
                    textAlloc, blobAlloc, blobNative, bytes.Length,
                    blobAlloc > 0 ? (double)textAlloc / blobAlloc : 0.0)
                : string.Format(
                    "托管分配计数器在本运行时不可用（Boehm 恒零，上面测得 text={0}/blob={1} 不可信，勿引用）；" +
                    "堆外 {2} B（{3} B/次）；零分配证据见 ConfigBlobZeroGcTests",
                    textAlloc, blobAlloc, blobNative, bytes.Length);

            Debug.Log(string.Format(
                "[ConfigBlob Benchmark] {0} 行 x {1} 次（两侧均为 加载 + 遍历全行读全字段；已 warmup，迭代数自适应到 >= {2} ms）\n" +
                "  文本路径 : {3,8:F1} ms\n" +
                "  blob 路径: {4,8:F1} ms（耗时受机器与 JIT 影响，仅供同机对比）\n" +
                "  {5}\n" +
                "  源文本 {6} B -> blob {7} B    sink {8}/{9}",
                source.Rows.Count, iterations, MinMillis,
                textWatch.Elapsed.TotalMilliseconds,
                blobWatch.Elapsed.TotalMilliseconds,
                allocReport,
                text.Length, bytes.Length,
                textSink, blobSink));
        }

        /// <summary>倍增迭代次数直到两侧单轮耗时都不低于 minMillis，避免在计时器分辨率附近做比较。</summary>
        private static int CalibrateIterations(double minMillis, Func<int, long> text, Func<int, long> blob)
        {
            int iterations = 64;
            while (iterations < 1 << 22)
            {
                var watch = Stopwatch.StartNew();
                text(iterations);
                double textMs = watch.Elapsed.TotalMilliseconds;

                watch.Restart();
                blob(iterations);
                double blobMs = watch.Elapsed.TotalMilliseconds;

                if (textMs >= minMillis && blobMs >= minMillis) return iterations;
                iterations *= 2;
            }
            return iterations;
        }

        private static long RunTextPass(string text, int iterations)
        {
            long sink = 0;
            for (int i = 0; i < iterations; i++)
            {
                ConfigBlobSchemaSource.TableSource parsed = ConfigBlobSchemaSource.ParseTabTable("Monsters", text);
                for (int r = 0; r < parsed.Rows.Count; r++)
                {
                    for (int c = 0; c < parsed.Rows[r].Length; c++) sink += parsed.Rows[r][c].Length;
                }
            }
            return sink;
        }

        private static long RunBlobPass(
            byte[] bytes, ulong schemaHash, ConfigBlobLayout.TableLayout layout, int iterations)
        {
            long sink = 0;
            for (int i = 0; i < iterations; i++)
            {
                ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(bytes), schemaHash);
                blob.TryGetTable(layout.NameHash, out ConfigTableView view);
                for (int r = 0; r < view.RowCount; r++)
                {
                    int rowOffset = view.GetRowOffsetByIndex(r);
                    for (int f = 0; f < layout.Fields.Count; f++)
                    {
                        ConfigBlobLayout.FieldLayout field = layout.Fields[f];
                        // 按真实类型分派：一律 ReadInt32 的话，指向含 bool/long/double 的表就在测垃圾。
                        sink += ReadFieldAsLong(blob, field, rowOffset + field.Offset);
                    }
                }
                blob.Release();
            }
            return sink;
        }

        /// <summary>按字段种类走对应的 ReadXxx，归一成 long 汇进 sink（防止整个循环被优化掉）。</summary>
        private static long ReadFieldAsLong(ConfigBlob blob, ConfigBlobLayout.FieldLayout field, int at)
        {
            switch (field.Kind)
            {
                case ConfigBlobLayout.FieldKind.Int32: return blob.ReadInt32(at);
                case ConfigBlobLayout.FieldKind.Int64: return blob.ReadInt64(at);
                case ConfigBlobLayout.FieldKind.Int16: return blob.ReadInt16(at);
                case ConfigBlobLayout.FieldKind.Byte: return blob.ReadByte(at);
                case ConfigBlobLayout.FieldKind.Bool: return blob.ReadBoolean(at) ? 1 : 0;
                case ConfigBlobLayout.FieldKind.Single: return (long)blob.ReadSingle(at);
                case ConfigBlobLayout.FieldKind.Double: return (long)blob.ReadDouble(at);
                case ConfigBlobLayout.FieldKind.String: return blob.ReadString(at).Utf8Length;
                default: throw new FrameworkException("未覆盖的字段种类：" + field.Kind);
            }
        }

        // ================================================================
        //  helpers —— 与生成代码的读写语义保持一一对应
        // ================================================================

        /// <summary>镜像生成的 XxxTableBuilder.Write：同样的常量偏移、同样的 SetXxx 调用。</summary>
        private static byte[] BuildBlob(ConfigBlobLayout.TableLayout layout, List<string[]> rows)
        {
            var writer = new ConfigBlobWriter(ConfigBlobLayout.ComputeBlobSchemaHash(new[] { layout }));
            writer.BeginTable(layout.TableName, layout.RowSize);
            for (int r = 0; r < rows.Count; r++)
            {
                string[] cols = rows[r];
                int primaryKey = int.Parse(cols[layout.PrimaryKey.SourceColumnIndex], CultureInfo.InvariantCulture);
                int rowIndex = writer.AddRow(primaryKey);
                for (int f = 0; f < layout.Fields.Count; f++)
                {
                    ConfigBlobLayout.FieldLayout field = layout.Fields[f];
                    string raw = cols[field.SourceColumnIndex];
                    switch (field.Kind)
                    {
                        case ConfigBlobLayout.FieldKind.Int32:
                            writer.SetInt32(rowIndex, field.Offset, int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.Int64:
                            writer.SetInt64(rowIndex, field.Offset, long.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.Int16:
                            writer.SetInt16(rowIndex, field.Offset, short.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.Byte:
                            writer.SetByte(rowIndex, field.Offset, byte.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.Bool:
                            writer.SetBoolean(rowIndex, field.Offset, bool.Parse(raw.Trim()));
                            break;
                        case ConfigBlobLayout.FieldKind.Single:
                            writer.SetSingle(rowIndex, field.Offset, float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.Double:
                            writer.SetDouble(rowIndex, field.Offset, double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
                            break;
                        case ConfigBlobLayout.FieldKind.String:
                            writer.SetString(rowIndex, field.Offset, raw);
                            break;
                        default:
                            throw new FrameworkException("未覆盖的字段种类：" + field.Kind);
                    }
                }
            }
            writer.EndTable();
            return writer.Build();
        }

        /// <summary>镜像生成的 XxxRow 属性：同样的常量偏移、同样的 ReadXxx 调用。</summary>
        private static void AssertRowEquals(
            ConfigBlob blob, ConfigBlobLayout.TableLayout layout, int rowOffset, string[] cols)
        {
            for (int f = 0; f < layout.Fields.Count; f++)
            {
                ConfigBlobLayout.FieldLayout field = layout.Fields[f];
                string raw = cols[field.SourceColumnIndex];
                int at = rowOffset + field.Offset;
                string what = layout.TableName + "." + field.Name;

                switch (field.Kind)
                {
                    case ConfigBlobLayout.FieldKind.Int32:
                        Assert.AreEqual(int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture), blob.ReadInt32(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Int64:
                        Assert.AreEqual(long.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture), blob.ReadInt64(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Int16:
                        Assert.AreEqual(short.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture), blob.ReadInt16(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Byte:
                        Assert.AreEqual(byte.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture), blob.ReadByte(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Bool:
                        Assert.AreEqual(bool.Parse(raw.Trim()), blob.ReadBoolean(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Single:
                        Assert.AreEqual(float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture), blob.ReadSingle(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.Double:
                        Assert.AreEqual(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture), blob.ReadDouble(at), what);
                        break;
                    case ConfigBlobLayout.FieldKind.String:
                        Assert.AreEqual(raw, blob.ReadString(at).ToString(), what);
                        break;
                    default:
                        throw new FrameworkException("未覆盖的字段种类：" + field.Kind);
                }
            }
        }

        private static ConfigBlobLayout.TableLayout BuildSyntheticLayout()
        {
            ConfigBlobSchemaSource.TableSource source = ConfigBlobSchemaSource.ParseTabTable("Synth", SyntheticTableText);
            return ConfigBlobLayout.Build(source.Schema, "memory://Synth");
        }

        private static Dictionary<string, CodeBuilder> BuildSyntheticFiles()
        {
            var layouts = new List<ConfigBlobLayout.TableLayout> { BuildSyntheticLayout() };
            return ConfigBlobGenerator.BuildAllFiles(layouts, "RuntimeDir", "EditorDir");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }
            return count;
        }

        private string NewTempDir()
        {
            string dir = Path.Combine(TestTempPaths.Root, "EjoyConfigBlobTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            m_TempDirs.Add(dir);
            return dir;
        }

        private static void WriteTable(string dir, string tableName, string content)
        {
            File.WriteAllText(Path.Combine(dir, tableName + ".txt"), content, Encoding.UTF8);
        }

        private static ConfigBlobLayout.FieldLayout FieldOf(ConfigBlobLayout.TableLayout layout, string name)
        {
            for (int i = 0; i < layout.Fields.Count; i++)
            {
                if (layout.Fields[i].Name == name) return layout.Fields[i];
            }
            throw new FrameworkException("字段不存在：" + name);
        }

        private static ConfigBlobSchemaSource.TableSource LoadMonstersOrIgnore()
        {
            if (!File.Exists(MonstersTablePath))
            {
                Assert.Fail("Missing committed fixture: " + MonstersTablePath);
            }
            return ConfigBlobSchemaSource.LoadTabTable(MonstersTablePath);
        }

        private static void AssertColumnType(ConfigBlobSchemaSource.TableSource source, string column, string expectedType)
        {
            for (int i = 0; i < source.Schema.Columns.Count; i++)
            {
                if (source.Schema.Columns[i].Name != column) continue;
                Assert.AreEqual(expectedType, source.Schema.Columns[i].Type, "列 " + column + " 的推断类型不符。");
                return;
            }
            Assert.Fail("未找到列 " + column);
        }
    }
}
