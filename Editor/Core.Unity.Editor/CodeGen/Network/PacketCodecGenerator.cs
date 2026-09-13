//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EjoyFramework.Core.Network;
using EjoyFramework.Core.Serialization;
using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Scans [GeneratePacketCodec] packet types and generates a Packet codec next to the declaring assembly.
    /// Explicit-path construction is kept for migration/tests; the default framework path is assembly-owned.
    /// </summary>
    public sealed class PacketCodecGenerator : ICodeGenerator
    {
        public const string OutputPath = CodeGenTypeUtil.DefaultAssemblyGeneratedRoot + "/Network/GeneratedPacketCodec.g.cs";
        public const string GeneratedNamespace = "EjoyFramework.Generated";
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.PacketCodecGenerator";
        private const string Category = "Network";
        private const string FileName = "GeneratedPacketCodec.g.cs";

        private readonly string m_OutputPath;
        private readonly string m_GeneratedNamespace;
        private readonly IEnumerable<Type> m_TargetTypes;
        private readonly bool m_UseAssemblyOutput;

        public PacketCodecGenerator()
        {
            m_UseAssemblyOutput = true;
        }

        public PacketCodecGenerator(string outputPath, string generatedNamespace, IEnumerable<Type> targetTypes = null)
        {
            m_OutputPath = string.IsNullOrEmpty(outputPath) ? OutputPath : outputPath;
            m_GeneratedNamespace = string.IsNullOrEmpty(generatedNamespace) ? GeneratedNamespace : generatedNamespace;
            m_TargetTypes = targetTypes;
            m_UseAssemblyOutput = false;
        }

        public string Id => "packetcodec";
        public string DisplayName => "PacketCodec (网络包编解码)";
        public string Description => "[GeneratePacketCodec] -> GeneratedPacketCodec.SerializeBody/CreatePacket，按声明程序集生成。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate Packet Codec")]
        public static void GenerateMenu()
        {
            CodeGenHub.RunOne(new PacketCodecGenerator());
        }

        public CodeGenResult Run()
        {
            return m_UseAssemblyOutput ? RunAssemblyOutput() : RunExplicitOutput();
        }

        private CodeGenResult RunAssemblyOutput()
        {
            var warnings = new List<string>();
            int skipped = 0;
            List<Entry> entries = CollectEntries(true, warnings, ref skipped);
            if (entries.Count == 0)
            {
                warnings.Add("未发现可用的 [GeneratePacketCodec] 类型。");
                return new CodeGenResult { Written = 0, Unchanged = 0, Skipped = skipped, Messages = warnings.ToArray() };
            }

            var groups = new Dictionary<Assembly, List<Entry>>();
            for (int i = 0; i < entries.Count; i++)
            {
                Assembly assembly = entries[i].Type.Assembly;
                if (!groups.TryGetValue(assembly, out List<Entry> group))
                {
                    group = new List<Entry>();
                    groups.Add(assembly, group);
                }
                group.Add(entries[i]);
            }

            int written = 0;
            int unchanged = 0;
            foreach (KeyValuePair<Assembly, List<Entry>> pair in groups)
            {
                pair.Value.Sort(CompareByPacketId);
                Entry first = pair.Value[0];
                if (!CodeGenTypeUtil.TryGetAssemblyGeneratedPath(
                    first.Type,
                    Category,
                    FileName,
                    out string path,
                    out string reason))
                {
                    skipped += pair.Value.Count;
                    warnings.Add(pair.Key.GetName().Name + ": " + reason + " 已跳过 PacketCodec。");
                    continue;
                }

                string ns = CodeGenTypeUtil.GeneratedNamespaceForAssembly(pair.Key);
                CodeBuilder cb = BuildSource(ns, pair.Value);
                if (cb.WriteIfChanged(path)) written++;
                else unchanged++;
                warnings.Add(pair.Value.Count + " 个 Packet 编入 " + ns + ".GeneratedPacketCodec -> " + path);
            }

            return new CodeGenResult
            {
                Written = written,
                Unchanged = unchanged,
                Skipped = skipped,
                Messages = warnings.ToArray(),
            };
        }

        private CodeGenResult RunExplicitOutput()
        {
            var warnings = new List<string>();
            int skipped = 0;
            List<Entry> entries = CollectEntries(false, warnings, ref skipped);
            if (entries.Count == 0)
            {
                warnings.Add("未发现可用的 [GeneratePacketCodec] 类型。");
                return new CodeGenResult { Written = 0, Unchanged = 0, Skipped = skipped, Messages = warnings.ToArray() };
            }

            entries.Sort(CompareByPacketId);
            CodeBuilder cb = BuildSource(m_GeneratedNamespace, entries);
            int written = cb.WriteIfChanged(m_OutputPath) ? 1 : 0;
            warnings.Insert(0, entries.Count + " 个 Packet 编入 " + m_GeneratedNamespace + ".GeneratedPacketCodec。");

            return new CodeGenResult
            {
                Written = written,
                Unchanged = written == 0 ? 1 : 0,
                Skipped = skipped,
                Messages = warnings.ToArray(),
            };
        }

        private List<Entry> CollectEntries(bool assemblyOutput, List<string> warnings, ref int skipped)
        {
            var entries = new List<Entry>();
            var seenIds = new Dictionary<int, string>();
            bool defaultScan = m_TargetTypes == null;

            foreach (Type type in GetTargetTypes())
            {
                if (defaultScan && CodeGenTypeUtil.IsTestOrEditorAssembly(type.Assembly)) continue;
                if (!ValidatePacketType(type, assemblyOutput, warnings, ref skipped)) continue;

                var attribute = (GeneratePacketCodecAttribute)Attribute.GetCustomAttribute(
                    type,
                    typeof(GeneratePacketCodecAttribute),
                    false);
                if (attribute == null || !attribute.HasPacketId)
                {
                    warnings.Add(type.FullName + ": [GeneratePacketCodec] 必须声明协议 Id，避免 Editor 构造 Packet 读取 Id。");
                    skipped++;
                    continue;
                }

                int packetId = attribute.PacketId;
                if (seenIds.TryGetValue(packetId, out string existing))
                {
                    warnings.Add("协议 Id " + packetId + " 冲突：" + existing + " 与 " + type.FullName + "，后者已跳过。");
                    skipped++;
                    continue;
                }

                seenIds.Add(packetId, type.FullName);
                entries.Add(new Entry
                {
                    PacketId = packetId,
                    Type = type,
                    TypeName = CodeGenTypeUtil.TypeName(type),
                });
            }

            return entries;
        }

        private bool ValidatePacketType(Type type, bool assemblyOutput, List<string> warnings, ref int skipped)
        {
            if (type.IsAbstract || type.IsInterface) return false;

            if (!typeof(Packet).IsAssignableFrom(type))
            {
                warnings.Add(type.FullName + ": 非 Packet 子类，已跳过。");
                skipped++;
                return false;
            }
            if (type.ContainsGenericParameters)
            {
                warnings.Add(type.FullName + ": 泛型定义暂不支持 PacketCodec 生成，已跳过。");
                skipped++;
                return false;
            }

            Assembly generatedAssembly = assemblyOutput ? type.Assembly : null;
            bool canReference = assemblyOutput
                ? CodeGenTypeUtil.CanReferenceTypeFrom(type, generatedAssembly)
                : CanReferenceFromExplicitGeneratedCodec(type);
            if (!canReference)
            {
                warnings.Add(type.FullName + ": PacketCodec 无法从生成代码可见地引用该 Packet 类型，已跳过。");
                skipped++;
                return false;
            }

            bool implementsBinarySerializable = typeof(IBinarySerializable).IsAssignableFrom(type);
            bool generatedSerializer = Attribute.IsDefined(type, typeof(GenerateSerializerAttribute), false);
            if (!implementsBinarySerializable && !generatedSerializer)
            {
                warnings.Add(type.FullName + ": 未实现 IBinarySerializable，也未标 [GenerateSerializer]，已跳过。");
                skipped++;
                return false;
            }
            if (!implementsBinarySerializable && generatedSerializer && type.IsNested)
            {
                warnings.Add(type.FullName + ": 嵌套 Packet 依赖 [GenerateSerializer]，但 SerializerGenerator 不支持嵌套类型，已跳过。");
                skipped++;
                return false;
            }

            bool hasConstructor = assemblyOutput
                ? CodeGenTypeUtil.HasCallableParameterlessConstructor(type, generatedAssembly)
                : type.GetConstructor(Type.EmptyTypes) != null;
            if (!hasConstructor)
            {
                warnings.Add(type.FullName + ": 缺少生成代码可调用的无参构造，无法生成编解码，已跳过。");
                skipped++;
                return false;
            }

            return true;
        }

        private static CodeBuilder BuildSource(string generatedNamespace, List<Entry> entries)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.Using("EjoyFramework.Core.Network");
            cb.Using("EjoyFramework.Core.Serialization");
            cb.BlankLine();
            cb.BeginBlock("namespace " + generatedNamespace);
            cb.BeginBlock("public static class GeneratedPacketCodec");

            cb.BeginBlock("public static byte[] SerializeBody(Packet packet)");
            cb.Line("if (packet == null) throw new EjoyFramework.Core.FrameworkException(\"packet is null.\");");
            cb.Line("var buffer = ByteBuffer.Acquire();");
            cb.BeginBlock("try");
            cb.Line("((IBinarySerializable)packet).Serialize(buffer);");
            cb.Line("return buffer.ToArray();");
            cb.EndBlock();
            cb.BeginBlock("finally");
            cb.Line("buffer.Release();");
            cb.EndBlock();
            cb.EndBlock();
            cb.BlankLine();

            cb.BeginBlock("public static int GetPacketId(Packet packet)");
            cb.Line("if (packet == null) throw new EjoyFramework.Core.FrameworkException(\"packet is null.\");");
            for (int i = 0; i < entries.Count; i++)
            {
                cb.Line("if (packet is " + entries[i].TypeName + ") return " + entries[i].PacketId + ";");
            }
            cb.Line("throw new EjoyFramework.Core.FrameworkException(\"Unsupported packet type: \" + packet.GetType().FullName);");
            cb.EndBlock();
            cb.BlankLine();

            cb.BeginBlock("public static Packet CreatePacket(int packetId, ByteBuffer body)");
            cb.Line("Packet packet;");
            cb.BeginBlock("switch (packetId)");
            for (int i = 0; i < entries.Count; i++)
            {
                cb.Line("case " + entries[i].PacketId + ": packet = new " + entries[i].TypeName + "(); break;");
            }
            cb.Line("default: return null;");
            cb.EndBlock();
            cb.Line("((IBinarySerializable)packet).Deserialize(body);");
            cb.Line("return packet;");
            cb.EndBlock();

            cb.EndBlock();
            cb.EndBlock();
            return cb;
        }

        private IEnumerable<Type> GetTargetTypes()
        {
            return m_TargetTypes ?? CodeGenTypeUtil.FindTypesWithAttribute(typeof(GeneratePacketCodecAttribute));
        }

        private static int CompareByPacketId(Entry a, Entry b)
        {
            return a.PacketId.CompareTo(b.PacketId);
        }

        private static bool CanReferenceFromExplicitGeneratedCodec(Type type)
        {
            if (type == null || type.IsGenericParameter || type.IsByRef || type.IsPointer) return false;
            if (type.IsArray) return CanReferenceFromExplicitGeneratedCodec(type.GetElementType());
            if (type.IsGenericType)
            {
                if (!CanReferenceFromExplicitGeneratedCodec(type.GetGenericTypeDefinition())) return false;
                Type[] args = type.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (!CanReferenceFromExplicitGeneratedCodec(args[i])) return false;
                }
                return true;
            }
            if (!type.IsNested) return type.IsPublic;
            return type.IsNestedPublic && CanReferenceFromExplicitGeneratedCodec(type.DeclaringType);
        }

        private struct Entry
        {
            public int PacketId;
            public Type Type;
            public string TypeName;
        }
    }
}
