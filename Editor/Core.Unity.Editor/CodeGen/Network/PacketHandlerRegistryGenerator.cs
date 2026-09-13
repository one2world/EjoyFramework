//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EjoyFramework.Core.Network;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// Scans IPacketHandler implementations and generates a registry bootstrap next to each declaring assembly.
    /// Explicit-path construction remains available for old project-specific integration points.
    /// </summary>
    public sealed class PacketHandlerRegistryGenerator : ICodeGenerator
    {
        public const string DefaultOutputPath =
            CodeGenTypeUtil.DefaultAssemblyGeneratedRoot + "/Network/PacketRegistryAutoInit.g.cs";
        public const string DefaultNamespace = "EjoyFramework.Generated";
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.PacketHandlerRegistryGenerator";
        private const string Category = "Network";
        private const string FileName = "PacketRegistryAutoInit.g.cs";

        private readonly string m_OutputPath;
        private readonly string m_GeneratedNamespace;
        private readonly IEnumerable<Type> m_TargetTypes;
        private readonly bool m_UseAssemblyOutput;

        public PacketHandlerRegistryGenerator()
        {
            m_UseAssemblyOutput = true;
        }

        public PacketHandlerRegistryGenerator(string outputPath, string generatedNamespace, IEnumerable<Type> targetTypes = null)
        {
            m_OutputPath = string.IsNullOrEmpty(outputPath) ? DefaultOutputPath : outputPath;
            m_GeneratedNamespace = string.IsNullOrEmpty(generatedNamespace) ? DefaultNamespace : generatedNamespace;
            m_TargetTypes = targetTypes;
            m_UseAssemblyOutput = false;
        }

        public string Id => "packetregistry";
        public string DisplayName => "Packet Handler Registry";
        public string Description => "扫描 IPacketHandler 实现 -> PacketRegistryAutoInit.RegisterAll，按声明程序集生成。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate Packet Registry")]
        public static void Generate()
        {
            CodeGenHub.RunOne(new PacketHandlerRegistryGenerator());
        }

        public static void Generate(string outputPath, string generatedNamespace)
        {
            CodeGenResult result = new PacketHandlerRegistryGenerator(outputPath, generatedNamespace).Run();
            AssetDatabase.Refresh();
            Debug.Log("[CodeGen] Packet Handler Registry: " + result);
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
                warnings.Add("未发现可用的 IPacketHandler 类型。");
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
                pair.Value.Sort(CompareByTypeName);
                Entry first = pair.Value[0];
                if (!CodeGenTypeUtil.TryGetAssemblyGeneratedPath(
                    first.Type,
                    Category,
                    FileName,
                    out string path,
                    out string reason))
                {
                    skipped += pair.Value.Count;
                    warnings.Add(pair.Key.GetName().Name + ": " + reason + " 已跳过 PacketRegistry。");
                    continue;
                }

                string ns = CodeGenTypeUtil.GeneratedNamespaceForAssembly(pair.Key);
                CodeBuilder cb = BuildSource(ns, pair.Value);
                if (cb.WriteIfChanged(path)) written++;
                else unchanged++;
                warnings.Add(pair.Value.Count + " handlers -> " + path);
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
            entries.Sort(CompareByTypeName);

            CodeBuilder cb = BuildSource(m_GeneratedNamespace, entries);
            bool written = cb.WriteIfChanged(m_OutputPath);
            return new CodeGenResult
            {
                Written = written ? 1 : 0,
                Unchanged = written ? 0 : 1,
                Skipped = skipped,
                Messages = new[] { entries.Count + " handlers -> " + m_OutputPath },
            };
        }

        private List<Entry> CollectEntries(bool assemblyOutput, List<string> warnings, ref int skipped)
        {
            var entries = new List<Entry>();
            bool defaultScan = m_TargetTypes == null;
            foreach (Type type in m_TargetTypes ?? CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (defaultScan && CodeGenTypeUtil.IsTestOrEditorAssembly(type.Assembly)) continue;
                if (!typeof(IPacketHandler).IsAssignableFrom(type)) continue;
                if (!ValidateHandlerType(type, assemblyOutput, warnings, ref skipped)) continue;

                entries.Add(new Entry
                {
                    Type = type,
                    TypeName = CodeGenTypeUtil.TypeName(type),
                });
            }
            return entries;
        }

        private static bool ValidateHandlerType(Type type, bool assemblyOutput, List<string> warnings, ref int skipped)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                return false;
            }
            if (type.ContainsGenericParameters)
            {
                skipped++;
                return false;
            }

            Assembly generatedAssembly = assemblyOutput ? type.Assembly : null;
            bool canReference = assemblyOutput
                ? CodeGenTypeUtil.CanReferenceTypeFrom(type, generatedAssembly)
                : CanReferenceFromExplicitGeneratedRegistry(type);
            if (!canReference)
            {
                skipped++;
                warnings.Add(type.FullName + ": PacketRegistry 无法从生成代码可见地引用该 handler，已跳过。");
                return false;
            }

            bool hasConstructor = assemblyOutput
                ? CodeGenTypeUtil.HasCallableParameterlessConstructor(type, generatedAssembly)
                : type.GetConstructor(Type.EmptyTypes) != null;
            if (!hasConstructor)
            {
                skipped++;
                warnings.Add(type.FullName + ": 缺少生成代码可调用的无参构造，已跳过。");
                return false;
            }

            return true;
        }

        private static CodeBuilder BuildSource(string generatedNamespace, List<Entry> entries)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.Using("EjoyFramework.Core.Network");
            cb.BlankLine();
            cb.BeginBlock("namespace " + generatedNamespace);
            cb.BeginBlock("public static class PacketRegistryAutoInit");
            cb.BeginBlock("public static void RegisterAll(PacketRegistry registry)");

            for (int i = 0; i < entries.Count; i++)
            {
                cb.Line("registry.Register(new " + entries[i].TypeName + "());");
            }

            cb.EndBlock();
            cb.EndBlock();
            cb.EndBlock();
            return cb;
        }

        private static int CompareByTypeName(Entry left, Entry right)
        {
            return string.CompareOrdinal(left.TypeName, right.TypeName);
        }

        private static bool CanReferenceFromExplicitGeneratedRegistry(Type type)
        {
            if (type == null || type.IsGenericParameter || type.IsByRef || type.IsPointer) return false;
            if (type.IsArray) return CanReferenceFromExplicitGeneratedRegistry(type.GetElementType());
            if (type.IsGenericType)
            {
                if (!CanReferenceFromExplicitGeneratedRegistry(type.GetGenericTypeDefinition())) return false;
                Type[] args = type.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                {
                    if (!CanReferenceFromExplicitGeneratedRegistry(args[i])) return false;
                }
                return true;
            }
            if (!type.IsNested) return type.IsPublic;
            return type.IsNestedPublic && CanReferenceFromExplicitGeneratedRegistry(type.DeclaringType);
        }

        private struct Entry
        {
            public Type Type;
            public string TypeName;
        }
    }
}
