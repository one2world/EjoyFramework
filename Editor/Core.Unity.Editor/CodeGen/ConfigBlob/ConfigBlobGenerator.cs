//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 配置表二进制（ConfigBlob）代码生成器 —— 框架第 7 条 codegen 线。
    ///
    /// 输入：配置表源文件（<c>Assets/GameMain/DataTables/**/*.txt</c> 的 TAB 表，或 <c>Assets/Configs/Source/*.csv</c>），
    /// 由 <see cref="ConfigBlobSchemaSource"/> 统一读成 schema + 数据行，再由 <see cref="ConfigBlobLayout"/> 编译成二进制布局。
    ///
    /// 输出两半，共用同一份布局常量，因此写侧与读侧天然同源：
    /// <list type="bullet">
    /// <item>Reader（运行时）：<c>Assets/Generated/ConfigBlob/</c>
    ///   —— 每表一个 <c>XxxRow</c>（readonly struct，属性 = 编译期常量偏移上的 ConfigBlob.ReadXxx）
    ///   与 <c>XxxTable</c>（GetById / TryGetById / 索引器 / 无装箱枚举器），外加
    ///   <c>ConfigBlobSchema.g.cs</c>（schemaHash、各表 nameHash / rowSize / 字段偏移常量）。</item>
    /// <item>Builder（编辑器）：<c>Assets/Editor/Generated/ConfigBlob/</c>
    ///   —— 每表一个 <c>XxxSourceRow</c> + <c>XxxTableBuilder</c>（文本解析 → 按同一批常量写二进制）。</item>
    /// </list>
    ///
    /// 本生成器不触碰现有 DataTable 运行时路径，是并行的新链路。
    /// </summary>
    public sealed class ConfigBlobGenerator : ICodeGenerator
    {
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.ConfigBlobGenerator";

        /// <summary>Reader（运行时）输出目录。</summary>
        public const string RuntimeOutputDir = CodeGenTypeUtil.DefaultAssemblyGeneratedRoot + "/ConfigBlob";

        /// <summary>Builder（编辑器）输出目录。</summary>
        public const string EditorOutputDir = CodeGenTypeUtil.DefaultEditorAssemblyGeneratedRoot + "/ConfigBlob";

        /// <summary>
        /// Reader 与 schema 常量所在命名空间。取 *.ConfigTables 而非 *.ConfigBlob：容器改名到
        /// EjoyFramework.Core.Blobs 后已无同名冲突，但"命名空间段与类名同名"本身就容易让名称解析出意外，故保持区分。
        /// </summary>
        public const string RuntimeNamespace = "EjoyFramework.Generated.ConfigTables";

        /// <summary>Builder 所在命名空间。</summary>
        public const string EditorNamespace = "EjoyFramework.Editor.Generated.ConfigTables";

        /// <summary>运行时容器所在命名空间（生成代码 using 它）。</summary>
        public const string ContainerNamespace = "EjoyFramework.Core.Blobs";

        /// <summary>默认扫描的 TAB 表根目录（递归）。</summary>
        public const string DefaultTableRoot = "Assets/GameMain/DataTables";

        /// <summary>schema 常量类名。</summary>
        public const string SchemaClassName = "ConfigBlobSchema";

        /// <summary>总装入口类名（编辑器侧）。</summary>
        public const string BuildEntryClassName = "ConfigBlobBuild";

        private readonly string[] m_SourceFiles;
        private readonly string m_RuntimeOutputDir;
        private readonly string m_EditorOutputDir;
        private readonly string m_SidecarDir;

        /// <summary>默认构造：扫描 <see cref="DefaultTableRoot"/> 下全部 .txt 表。</summary>
        public ConfigBlobGenerator()
        {
        }

        /// <summary>显式构造：指定源文件与输出目录（测试与迁移用）。</summary>
        public ConfigBlobGenerator(
            IEnumerable<string> sourceFiles,
            string runtimeOutputDir = null,
            string editorOutputDir = null,
            string sidecarDir = null)
        {
            m_SourceFiles = sourceFiles == null ? null : new List<string>(sourceFiles).ToArray();
            m_RuntimeOutputDir = string.IsNullOrEmpty(runtimeOutputDir) ? RuntimeOutputDir : runtimeOutputDir;
            m_EditorOutputDir = string.IsNullOrEmpty(editorOutputDir) ? EditorOutputDir : editorOutputDir;
            m_SidecarDir = sidecarDir;
        }

        /// <inheritdoc />
        public string Id => "configblob";

        /// <inheritdoc />
        public string DisplayName => "ConfigBlob (配置表二进制读写)";

        /// <inheritdoc />
        public string Description => "配置表 -> XxxRow/XxxTable(运行时零解析读) + XxxTableBuilder(编辑器写二进制)。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate ConfigBlob")]
        private static void GenerateMenu()
        {
            CodeGenHub.RunOne(new ConfigBlobGenerator());
        }

        /// <inheritdoc />
        public CodeGenResult Run()
        {
            var messages = new List<string>();
            string runtimeDir = CodeGenPath.Resolve(m_RuntimeOutputDir ?? RuntimeOutputDir);
            string editorDir = CodeGenPath.Resolve(m_EditorOutputDir ?? EditorOutputDir);
            // Check every output before sidecars or orphan cleanup can mutate anything.
            CodeGenPath.RequireWritable(runtimeDir);
            CodeGenPath.RequireWritable(editorDir);
            CodeGenPath.RequireWritable(string.IsNullOrEmpty(m_SidecarDir)
                ? ConfigBlobTypeSidecar.DefaultRootDirectory : m_SidecarDir);

            string[] sources = m_SourceFiles ?? DiscoverDefaultSources();
            if (sources.Length == 0)
            {
                return CodeGenResult.Empty("未发现配置表源文件（扫描根：" + DefaultTableRoot + "）。");
            }

            var layouts = new List<ConfigBlobLayout.TableLayout>();
            var inferredTables = new List<ConfigBlobLayout.TableLayout>();
            int skipped = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                try
                {
                    ConfigBlobSchemaSource.TableSource src = ConfigBlobSchemaSource.Load(sources[i]);
                    ConfigBlobLayout.TableLayout layout = ConfigBlobLayout.Build(src.Schema, NormalizePath(src.SourcePath));
                    layouts.Add(layout);
                    if (src.TypesWereInferred) inferredTables.Add(layout);
                }
                catch (Exception ex)
                {
                    skipped++;
                    Skip(messages, NormalizePath(sources[i]), ex.Message);
                }
            }

            skipped += RejectDuplicateTableNames(layouts, messages);

            if (layouts.Count == 0)
            {
                messages.Add("没有可用的表，未生成任何文件。");
                LogMessages(messages);
                return new CodeGenResult { Skipped = skipped, Messages = messages.ToArray() };
            }

            layouts.Sort((a, b) => string.CompareOrdinal(a.TableName, b.TableName));

            // 类型推断的表落 .types 边车并比对上次结果，推断结果变了要明确告警。
            for (int i = 0; i < inferredTables.Count; i++)
            {
                if (!layouts.Contains(inferredTables[i])) continue;
                try
                {
                    List<string> drift = ConfigBlobTypeSidecar.Sync(inferredTables[i], true, m_SidecarDir);
                    for (int d = 0; d < drift.Count; d++)
                    {
                        messages.Add("类型推断变化：" + drift[d]);
                        Debug.LogWarning("[CodeGen][ConfigBlob] 类型推断变化：" + drift[d]);
                    }
                }
                catch (Exception ex)
                {
                    messages.Add("边车写入失败（" + inferredTables[i].TableName + "）：" + ex.Message);
                }
            }
            for (int i = 0; i < layouts.Count; i++)
            {
                if (inferredTables.Contains(layouts[i])) continue;
                try { ConfigBlobTypeSidecar.Sync(layouts[i], false, m_SidecarDir); }
                catch (Exception ex) { messages.Add("边车清理失败（" + layouts[i].TableName + "）：" + ex.Message); }
            }

            Dictionary<string, CodeBuilder> files = BuildAllFiles(layouts, runtimeDir, editorDir);

            int written = 0, unchanged = 0;
            foreach (KeyValuePair<string, CodeBuilder> file in files)
            {
                if (file.Value.WriteIfChanged(file.Key)) written++;
                else unchanged++;
            }

            int deleted = DeleteOrphans(files.Keys, runtimeDir, editorDir, messages);

            messages.Insert(0, layouts.Count + " 张表 -> " + runtimeDir + "（Reader）+ " + editorDir +
                               "（Builder）" + (deleted > 0 ? "，清理孤儿文件 " + deleted + " 个。" : "。"));
            LogMessages(messages);
            return new CodeGenResult { Written = written, Unchanged = unchanged, Skipped = skipped, Messages = messages.ToArray() };
        }

        /// <summary>
        /// 表名冲突（不同目录下同名 .txt）会生成同名类型与重复常量，且后写的文件覆盖先写的，
        /// 全程无提示。这里把整组冲突表一并剔除，并把冲突的两个路径都打出来。
        /// </summary>
        private static int RejectDuplicateTableNames(List<ConfigBlobLayout.TableLayout> layouts, List<string> messages)
        {
            var byName = new Dictionary<string, List<ConfigBlobLayout.TableLayout>>(StringComparer.Ordinal);
            for (int i = 0; i < layouts.Count; i++)
            {
                if (!byName.TryGetValue(layouts[i].TableName, out List<ConfigBlobLayout.TableLayout> group))
                {
                    group = new List<ConfigBlobLayout.TableLayout>();
                    byName.Add(layouts[i].TableName, group);
                }
                group.Add(layouts[i]);
            }

            int removed = 0;
            foreach (KeyValuePair<string, List<ConfigBlobLayout.TableLayout>> pair in byName)
            {
                if (pair.Value.Count < 2) continue;

                var paths = new List<string>(pair.Value.Count);
                for (int i = 0; i < pair.Value.Count; i++) paths.Add(pair.Value[i].SourcePath ?? "<unknown>");
                Skip(messages, pair.Key,
                    "表名重复，整组跳过（同名会生成同名类型与重复常量，且后写的文件静默覆盖先写的）。冲突路径：" +
                    string.Join(" | ", paths.ToArray()));

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    layouts.Remove(pair.Value[i]);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// 删除输出目录里本次没生成的 *.g.cs（及其 .meta）。表被删名或改名后，旧的 .g.cs 会继续编译，
        /// 引用早已不存在的表，或者更糟——按过期布局常量读新数据。
        /// </summary>
        private static int DeleteOrphans(
            IEnumerable<string> generated, string runtimeDir, string editorDir, List<string> messages)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in generated) keep.Add(NormalizePath(path));

            int deleted = 0;
            string[] dirs = string.Equals(runtimeDir, editorDir, StringComparison.OrdinalIgnoreCase)
                ? new[] { runtimeDir }
                : new[] { runtimeDir, editorDir };

            for (int d = 0; d < dirs.Length; d++)
            {
                if (!Directory.Exists(dirs[d])) continue;
                string[] existing = Directory.GetFiles(dirs[d], "*.g.cs", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < existing.Length; i++)
                {
                    string path = NormalizePath(existing[i]);
                    if (keep.Contains(path)) continue;

                    try
                    {
                        File.Delete(path);
                        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                        deleted++;
                        messages.Add("删除孤儿文件 " + path + "（本次生成集里已无对应表）。");
                    }
                    catch (Exception ex)
                    {
                        messages.Add("孤儿文件删除失败 " + path + "：" + ex.Message);
                    }
                }
            }
            return deleted;
        }

        /// <summary>记一条跳过，并立刻 LogWarning——只塞进 CodeGenResult.Messages 的话，面板不展开就看不见。</summary>
        private static void Skip(List<string> messages, string what, string reason)
        {
            string line = "跳过 " + what + "：" + reason;
            messages.Add(line);
            Debug.LogWarning("[CodeGen][ConfigBlob] " + line);
        }

        private static void LogMessages(List<string> messages)
        {
            if (messages.Count > 0) Debug.Log("[CodeGen][ConfigBlob] " + string.Join("\n  ", messages.ToArray()));
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>产出"文件路径 -> 文件内容"的全集（测试可直接断言内容，不必落盘）。</summary>
        public static Dictionary<string, CodeBuilder> BuildAllFiles(
            List<ConfigBlobLayout.TableLayout> layouts, string runtimeDir, string editorDir)
        {
            var files = new Dictionary<string, CodeBuilder>(StringComparer.Ordinal);
            files[Combine(runtimeDir, SchemaClassName + ".g.cs")] = BuildSchemaFile(layouts);
            files[Combine(editorDir, BuildEntryClassName + ".g.cs")] = BuildAggregateFile(layouts);
            for (int i = 0; i < layouts.Count; i++)
            {
                ConfigBlobLayout.TableLayout layout = layouts[i];
                files[Combine(runtimeDir, layout.TableName + "Table.g.cs")] =
                    ConfigBlobReaderEmitter.Emit(layout, RuntimeNamespace, ContainerNamespace, SchemaClassName, ToolName);
                files[Combine(editorDir, layout.TableName + "TableBuilder.g.cs")] =
                    ConfigBlobBuilderEmitter.Emit(layout, EditorNamespace, RuntimeNamespace, ContainerNamespace, SchemaClassName, ToolName);
            }
            return files;
        }

        private static string Combine(string dir, string fileName)
        {
            return Path.Combine(dir, fileName).Replace('\\', '/');
        }

        /// <summary>
        /// 生成"总装"入口 <c>ConfigBlobBuild.g.cs</c>（编辑器侧）：用 <c>ConfigBlobSchema.SchemaHash</c> 开 writer、
        /// 按表名序调用各表 Builder.Write、产出 byte[]，另附 <c>WriteToFile</c>。
        ///
        /// 之所以要生成而不是让人手写：表增删时手写入口一定会漏改，而漏掉一张表在运行期的表现是
        /// TryGetTable 找不到——错误发生在离原因很远的地方。生成的入口天然与表集合同步。
        /// 挂到哪个构建管线阶段由调用方决定，本文件只提供入口。
        /// </summary>
        public static CodeBuilder BuildAggregateFile(List<ConfigBlobLayout.TableLayout> layouts)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.Using("System.Collections.Generic");
            cb.Using("System.IO");
            cb.Using(ContainerNamespace);
            cb.Using(RuntimeNamespace);
            cb.BlankLine();
            cb.BeginBlock("namespace " + EditorNamespace);

            cb.Line("/// <summary>");
            cb.Line("/// 配置表 blob 的总装入口：把全部 " + layouts.Count + " 张表写进一个 blob。");
            cb.Line("/// 表集合随代码生成同步更新，增删表无需手改本文件（它本来就会被覆盖）。");
            cb.Line("/// </summary>");
            cb.BeginBlock("public static partial class " + BuildEntryClassName);

            cb.Line("/// <summary>本次生成覆盖的表名（按表名序，与 Build 的写入顺序一致）。</summary>");
            cb.Line("public static readonly string[] TableNames =");
            cb.Indent();
            var names = new List<string>(layouts.Count);
            for (int i = 0; i < layouts.Count; i++) names.Add("\"" + layouts[i].TableName + "\"");
            cb.Line("{ " + string.Join(", ", names.ToArray()) + " };");
            cb.Outdent();
            cb.BlankLine();

            cb.Line("/// <summary>");
            cb.Line("/// 从各表源文件构建 blob 字节。sourceDirectory 为空时按各表生成时记录的源路径读取。");
            cb.Line("/// </summary>");
            cb.BeginBlock("public static byte[] Build(string sourceDirectory = null)");
            cb.Line("var writer = new ConfigBlobWriter(" + SchemaClassName + ".SchemaHash);");
            for (int i = 0; i < layouts.Count; i++)
            {
                ConfigBlobLayout.TableLayout layout = layouts[i];
                string builderType = layout.TableName + "TableBuilder";
                cb.Line(builderType + ".Write(" + builderType + ".ParseFile(ResolveSource(sourceDirectory, \"" +
                        layout.TableName + "\", \"" + (layout.SourcePath ?? string.Empty) + "\")), writer);");
            }
            cb.Line("return writer.Build();");
            cb.EndBlock();
            cb.BlankLine();

            cb.Line("/// <summary>构建并落盘；返回写入的字节数。</summary>");
            cb.BeginBlock("public static int WriteToFile(string path, string sourceDirectory = null)");
            cb.Line("byte[] bytes = Build(sourceDirectory);");
            cb.Line("string dir = Path.GetDirectoryName(path);");
            cb.Line("if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);");
            cb.Line("File.WriteAllBytes(path, bytes);");
            cb.Line("return bytes.Length;");
            cb.EndBlock();
            cb.BlankLine();

            cb.Line("/// <summary>定位某表的源文件：优先 sourceDirectory/表名.txt，否则用生成时记录的路径。</summary>");
            cb.BeginBlock("private static string ResolveSource(string sourceDirectory, string tableName, string recordedPath)");
            cb.BeginBlock("if (!string.IsNullOrEmpty(sourceDirectory))");
            cb.Line("string candidate = Path.Combine(sourceDirectory, tableName + \".txt\");");
            cb.Line("if (File.Exists(candidate)) return candidate;");
            cb.EndBlock();
            cb.BeginBlock("if (string.IsNullOrEmpty(recordedPath) || !File.Exists(recordedPath))");
            cb.Line("throw new EjoyFramework.Core.FrameworkException(\"" + BuildEntryClassName +
                    "：找不到表 '\" + tableName + \"' 的源文件（生成时记录的路径：\" + recordedPath + \"）。\");");
            cb.EndBlock();
            cb.Line("return recordedPath;");
            cb.EndBlock();

            cb.EndBlock();
            cb.EndBlock();
            return cb;
        }

        /// <summary>生成 schema 常量文件（schemaHash + 各表 nameHash / rowSize / 字段偏移）。</summary>
        public static CodeBuilder BuildSchemaFile(List<ConfigBlobLayout.TableLayout> layouts)
        {
            ulong blobSchemaHash = ConfigBlobLayout.ComputeBlobSchemaHash(layouts);

            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.BeginBlock("namespace " + RuntimeNamespace);
            cb.Line("/// <summary>");
            cb.Line("/// ConfigBlob 布局常量。Reader 与 Builder 共同引用本类，保证写侧与读侧偏移永远一致。");
            cb.Line("/// </summary>");
            cb.BeginBlock("public static class " + SchemaClassName);

            cb.Line("/// <summary>全量表 schema 合并 hash（Open 时校验；算法见 ConfigBlobLayout.ComputeBlobSchemaHash）。</summary>");
            cb.Line("public const ulong SchemaHash = 0x" + blobSchemaHash.ToString("X16") + "UL;");

            for (int i = 0; i < layouts.Count; i++)
            {
                ConfigBlobLayout.TableLayout layout = layouts[i];
                cb.BlankLine();
                cb.Line("/// <summary>表 " + layout.TableName + " 的表名字面量。</summary>");
                cb.Line("public const string " + layout.TableName + "TableName = \"" + layout.TableName + "\";");
                cb.Line("/// <summary>表 " + layout.TableName + " 的表名 hash（FNV1a64）。</summary>");
                cb.Line("public const ulong " + layout.TableName + "NameHash = 0x" + layout.NameHash.ToString("X16") + "UL;");
                cb.Line("/// <summary>表 " + layout.TableName + " 的单表 schema hash。</summary>");
                cb.Line("public const ulong " + layout.TableName + "SchemaHash = 0x" + layout.SchemaHash.ToString("X16") + "UL;");
                cb.Line("/// <summary>表 " + layout.TableName + " 的行字节长。</summary>");
                cb.Line("public const int " + layout.TableName + "RowSize = " + layout.RowSize + ";");
                cb.Line("/// <summary>表 " + layout.TableName + " 的列数。</summary>");
                cb.Line("public const int " + layout.TableName + "ColumnCount = " + layout.FieldsInDeclarationOrder.Count + ";");

                for (int f = 0; f < layout.Fields.Count; f++)
                {
                    ConfigBlobLayout.FieldLayout field = layout.Fields[f];
                    cb.Line("/// <summary>" + layout.TableName + "." + field.Name + " 的行内偏移（" +
                            field.DeclaredType + "，" + field.Size + " 字节）。</summary>");
                    cb.Line("public const int " + layout.TableName + "_" + field.Name + "_Offset = " + field.Offset + ";");
                }
            }

            cb.EndBlock();
            cb.EndBlock();
            return cb;
        }

        private static string[] DiscoverDefaultSources()
        {
            if (!Directory.Exists(DefaultTableRoot)) return Array.Empty<string>();
            string[] files = Directory.GetFiles(DefaultTableRoot, "*.txt", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++) files[i] = files[i].Replace('\\', '/');
            return files;
        }
    }
}
