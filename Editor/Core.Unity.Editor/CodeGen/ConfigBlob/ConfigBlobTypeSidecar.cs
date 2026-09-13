//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 类型推断的护栏。
    ///
    /// 没写 <c>#@types</c> 行的表，列类型是从数据推断出来的——今天整列都是整数就推成 int，
    /// 明天填进一个 1.5 就变成 float。schemaHash 会因此变化（数据文件会被拒，这没问题），
    /// 但**生成属性的 C# 类型也跟着变**，业务侧编译报错时根本看不出是配置表改动引起的。
    ///
    /// 所以每张走推断的表都落一份 <c>.types</c> 快照文件（**应当签入版本库**）：
    /// 下次生成时逐列比对，一旦某列的推断结果变了，就明确报出"表.列：int -> float"。
    /// 快照只是**告警**不是拒绝——策划确实要改类型时，让生成器覆盖即可。
    ///
    /// 存放位置刻意放在 <see cref="DefaultRootDirectory"/>（工程根下、<c>Assets/</c> 之外）而不是源表旁边：
    /// 放在 Assets 里的任何文件都会被 Unity 导入成资源（.types 会变成 TextAsset）并可能被打进包，
    /// 白白增加包体、还会出现在资源检索结果里。放到 ProjectSettings 同级目录既随版本库走，又不进构建。
    /// </summary>
    public static class ConfigBlobTypeSidecar
    {
        /// <summary>边车文件扩展名。</summary>
        public const string Extension = ".types";

        /// <summary>
        /// 默认存放目录：工程根下、Assets 之外，随版本库签入但不会被 Unity 当资源导入。
        /// 与 ProjectSettings 同级，语义上也确实是"工程级的导表元数据"。
        /// </summary>
        public const string DefaultRootDirectory = "ProjectSettings/ConfigBlobTypes";

        /// <summary>一列的类型记录。</summary>
        public struct ColumnType
        {
            /// <summary>列名。</summary>
            public string Name;

            /// <summary>类型串。</summary>
            public string Type;
        }

        /// <summary>
        /// 某表的边车路径。按**表名**而非源路径定位——表名在生成器里已保证全局唯一（重名整组拒绝），
        /// 因此不会撞车，且源表挪目录时快照不会失联。
        /// </summary>
        public static string PathFor(string tableName, string rootDirectory = null)
        {
            if (string.IsNullOrEmpty(tableName)) return null;
            string root = string.IsNullOrEmpty(rootDirectory) ? DefaultRootDirectory : rootDirectory;
            return Path.Combine(root, tableName + Extension).Replace('\\', '/');
        }

        /// <summary>把布局里的列类型序列化成边车内容（每行 "列名\t类型"）。</summary>
        public static string Serialize(ConfigBlobLayout.TableLayout layout)
        {
            var sb = new StringBuilder();
            sb.Append("# AUTO-GENERATED type snapshot for ").Append(layout.TableName)
              .Append(" — 请签入版本库。列类型由数据推断得来，本文件用于在推断结果变化时告警。\n");
            sb.Append("# 若要固定类型，请在源表里写一行 '#@types'，之后本文件即失效。\n");
            for (int i = 0; i < layout.FieldsInDeclarationOrder.Count; i++)
            {
                ConfigBlobLayout.FieldLayout f = layout.FieldsInDeclarationOrder[i];
                sb.Append(f.Name).Append('\t').Append(f.DeclaredType).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>解析边车内容；文件缺失或损坏返回空列表（视作"首次生成"）。</summary>
        public static List<ColumnType> Parse(string text)
        {
            var result = new List<ColumnType>();
            if (string.IsNullOrEmpty(text)) return result;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cols = line.Split('\t');
                if (cols.Length < 2) continue;
                result.Add(new ColumnType { Name = cols[0].Trim(), Type = cols[1].Trim() });
            }
            return result;
        }

        /// <summary>
        /// 与上次快照比对，返回人类可读的差异描述；无差异（或没有上次快照）返回空列表。
        /// </summary>
        public static List<string> Diff(ConfigBlobLayout.TableLayout layout, List<ColumnType> previous)
        {
            var messages = new List<string>();
            if (previous == null || previous.Count == 0) return messages;

            var previousByName = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < previous.Count; i++) previousByName[previous[i].Name] = previous[i].Type;

            var currentNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < layout.FieldsInDeclarationOrder.Count; i++)
            {
                ConfigBlobLayout.FieldLayout f = layout.FieldsInDeclarationOrder[i];
                currentNames.Add(f.Name);
                if (!previousByName.TryGetValue(f.Name, out string was))
                {
                    messages.Add(layout.TableName + "." + f.Name + "：新增列（推断为 " + f.DeclaredType + "）。");
                    continue;
                }
                if (!string.Equals(was, f.DeclaredType, StringComparison.Ordinal))
                {
                    messages.Add(layout.TableName + "." + f.Name + "：推断类型由 " + was + " 变为 " + f.DeclaredType +
                                 "——生成属性的 C# 类型随之改变，可能导致业务代码编译失败；" +
                                 "若这是有意的改动，本条可忽略（边车已更新）。");
                }
            }

            for (int i = 0; i < previous.Count; i++)
            {
                if (!currentNames.Contains(previous[i].Name))
                    messages.Add(layout.TableName + "." + previous[i].Name + "：列已消失（上次推断为 " + previous[i].Type + "）。");
            }

            return messages;
        }

        /// <summary>
        /// 读旧快照 → 比对 → 写新快照。返回差异描述（调用方负责 LogWarning）。
        /// 显式声明类型的表不落快照，已有的会被删掉（<c>#@types</c> 本身就是更强的护栏）。
        /// </summary>
        public static List<string> Sync(
            ConfigBlobLayout.TableLayout layout, bool typesWereInferred, string rootDirectory = null)
        {
            var messages = new List<string>();
            string path = PathFor(layout.TableName, rootDirectory);
            if (string.IsNullOrEmpty(path)) return messages;
            path = CodeGenPath.Resolve(path);
            CodeGenPath.RequireWritable(path);

            if (!typesWereInferred)
            {
                if (File.Exists(path)) File.Delete(path);
                return messages;
            }

            List<ColumnType> previous = File.Exists(path)
                ? Parse(File.ReadAllText(path, Encoding.UTF8))
                : new List<ColumnType>();

            messages.AddRange(Diff(layout, previous));

            string content = Serialize(layout);
            if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != content)
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content, Encoding.UTF8);
            }

            return messages;
        }
    }
}
