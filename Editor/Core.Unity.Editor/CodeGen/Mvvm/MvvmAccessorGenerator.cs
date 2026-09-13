//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 扫描所有 <see cref="EjoyFramework.Core.UI.Mvvm.BindableObject"/> 的具体子类，为其可绑定成员（public 实例
    /// 属性/字段）<b>按程序集</b>生成一份 <c>MvvmAccessorRegistrations.g.cs</c>，内含强类型 get/set 委托登记：
    /// <code>PropertyAccessor.Register(typeof(VM), "Prop", static o =&gt; ((VM)o).Prop, static (o,v) =&gt; ((VM)o).Prop = (T)v);</code>
    ///
    /// 目的：消除 MVVM 绑定在 <see cref="EjoyFramework.Core.UI.Mvvm.PropertyAccessor"/> 中的运行时反射
    /// （GetProperty/GetField + Get/SetValue）。生成的是<b>静态强类型 delegate</b>，IL2CPP 不裁剪、性能更好。
    ///
    /// 按程序集生成：很多 VM 是 <c>internal</c>，登记代码必须编译进其所在程序集才能访问。生成文件落到该程序集
    /// .asmdef 目录下的 <c>Generated/</c> 子目录。引用 UnityEngine 的程序集自挂
    /// <c>RuntimeInitializeOnLoadMethod</c> 自触发；纯 C# 程序集需由 Unity 侧 bootstrap 调用其 RegisterAll()。
    /// 已接入统一 <see cref="CodeGenHub"/>。
    /// </summary>
    public sealed class MvvmAccessorGenerator : ICodeGenerator
    {
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.MvvmAccessorGenerator";
        private const string GeneratedFileName = "MvvmAccessorRegistrations.g.cs";
        private const string GeneratedClassName = "MvvmAccessorRegistrations";
        private const string BindableObjectFullName = "EjoyFramework.Core.UI.Mvvm.BindableObject";

        public string Id => "mvvmaccessors";
        public string DisplayName => "MVVM Property Accessors";
        public string Description =>
            "扫描 BindableObject 子类 → 每程序集生成强类型 get/set 注册（PropertyAccessor.Register），消除 MVVM 绑定运行时反射与 IL2CPP 裁剪。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate Mvvm Accessors")]
        public static void Generate()
        {
            CodeGenHub.RunOne(new MvvmAccessorGenerator());
        }

        /// <summary>无头/CI 入口：Unity -batchmode -quit -executeMethod ...MvvmAccessorGenerator.GenerateCli</summary>
        public static void GenerateCli()
        {
            try
            {
                CodeGenResult r = new MvvmAccessorGenerator().Run();
                AssetDatabase.Refresh();
                Debug.Log("[CodeGen] MVVM Accessors (CLI): " + r);
                if (r.Messages != null)
                {
                    for (int i = 0; i < r.Messages.Length; i++) Debug.Log("  • " + r.Messages[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CodeGen] MVVM Accessors (CLI) failed: " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public CodeGenResult Run()
        {
            var messages = new List<string>();
            List<GenTarget> plan = BuildPlan(messages, out int skipped);

            int written = 0, unchanged = 0, totalMembers = 0;
            foreach (GenTarget t in plan)
            {
                totalMembers += t.MemberCount;
                if (t.Builder.WriteIfChanged(t.Path)) written++; else unchanged++;
            }

            messages.Insert(0, totalMembers + " accessor(s) across " + plan.Count + " assembly(ies).");
            return new CodeGenResult { Written = written, Unchanged = unchanged, Skipped = skipped, Messages = messages.ToArray() };
        }

        private List<GenTarget> BuildPlan(List<string> messages, out int skipped)
        {
            skipped = 0;
            Type baseType = ResolveBindableObjectType();
            if (baseType == null)
            {
                messages.Add("BindableObject base type not found; nothing to generate.");
                return new List<GenTarget>();
            }

            var byAssembly = new Dictionary<Assembly, List<VmEntry>>();
            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!baseType.IsAssignableFrom(t) || t == baseType) continue;
                if (!CanReferenceType(t, t.Assembly)) continue; // 私有/受保护嵌套等无法在同程序集生成代码中命名 → 跳过（Editor 反射兜底）

                List<MemberAccessor> members = CollectMembers(t, t.Assembly);
                if (members.Count == 0) continue;

                if (!byAssembly.TryGetValue(t.Assembly, out List<VmEntry> list))
                {
                    list = new List<VmEntry>();
                    byAssembly[t.Assembly] = list;
                }
                list.Add(new VmEntry { Vm = t, Members = members });
            }

            var plan = new List<GenTarget>();
            foreach (KeyValuePair<Assembly, List<VmEntry>> kv in byAssembly)
            {
                Assembly asm = kv.Key;
                List<VmEntry> entries = kv.Value;
                entries.Sort((a, b) => string.CompareOrdinal(a.Vm.FullName, b.Vm.FullName));

                string asmName = asm.GetName().Name;
                string outPath;
                string reason;
                if (!CodeGenTypeUtil.TryGetAssemblyGeneratedPath(asm, null, GeneratedFileName, out outPath, out reason))
                {
                    skipped++;
                    messages.Add("SKIP '" + asmName + "': " + reason);
                    continue;
                }

                int memberCount = 0;
                for (int i = 0; i < entries.Count; i++) memberCount += entries[i].Members.Count;

                bool engineRef = ReferencesUnityEngine(asm);
                string ns = CodeGenTypeUtil.GeneratedNamespaceForAssembly(asm);

                plan.Add(new GenTarget { Path = outPath, Builder = BuildFile(ns, engineRef, entries), MemberCount = memberCount });
                messages.Add(asmName + ": " + entries.Count + " VM(s), " + memberCount + " accessor(s) → " + outPath +
                             (engineRef ? "  [self-triggers via RuntimeInitializeOnLoadMethod]"
                                        : "  [pure C# — RegisterAll() triggered by a Unity-side bootstrap]"));
            }
            return plan;
        }

        private static CodeBuilder BuildFile(string ns, bool engineRef, List<VmEntry> entries)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.BeginBlock("namespace " + ns);
            cb.BeginBlock("public static class " + GeneratedClassName);

            if (engineRef)
            {
                cb.Line("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            }
            cb.BeginBlock("public static void RegisterAll()");

            foreach (VmEntry e in entries)
            {
                string vm = "global::" + CodeGenTypeUtil.TypeName(e.Vm);
                foreach (MemberAccessor m in e.Members)
                {
                    string getter = m.CanGet
                        ? "static o => ((" + vm + ")o)." + m.Name
                        : "null";
                    string setter = m.CanSet
                        ? "static (o, v) => ((" + vm + ")o)." + m.Name + " = (global::" + CodeGenTypeUtil.TypeName(m.MemberType) + ")v"
                        : "null";
                    cb.Line("global::EjoyFramework.Core.UI.Mvvm.PropertyAccessor.Register(typeof(" + vm + "), \"" + m.Name + "\",");
                    cb.Line("    " + getter + ",");
                    cb.Line("    " + setter + ");");
                }
            }

            cb.EndBlock();   // RegisterAll
            cb.EndBlock();   // class
            cb.EndBlock();   // namespace
            return cb;
        }

        // 收集一个 VM 的可绑定成员：public 实例属性（public getter → 可取；public setter 且类型可命名 → 可写）与
        // public 实例字段（可取；非 readonly/const 且类型可命名 → 可写）。跳过索引器/静态/指针-byref/重名。
        private static List<MemberAccessor> CollectMembers(Type vm, Assembly asm)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<MemberAccessor>();

            foreach (PropertyInfo p in vm.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                if (!seen.Add(p.Name)) continue;
                Type pt = p.PropertyType;
                if (pt.IsByRef || pt.IsPointer) continue;
                bool canGet = p.GetGetMethod(false) != null;
                bool canSet = p.GetSetMethod(false) != null && CanReferenceType(pt, asm);
                if (!canGet && !canSet) continue;
                result.Add(new MemberAccessor { Name = p.Name, MemberType = pt, CanGet = canGet, CanSet = canSet });
            }

            foreach (FieldInfo f in vm.GetFields(flags))
            {
                if (f.IsStatic) continue;
                if (!seen.Add(f.Name)) continue;
                Type ft = f.FieldType;
                if (ft.IsByRef || ft.IsPointer) continue;
                bool canSet = !f.IsInitOnly && !f.IsLiteral && CanReferenceType(ft, asm);
                result.Add(new MemberAccessor { Name = f.Name, MemberType = ft, CanGet = true, CanSet = canSet });
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        // 该类型能否在「位于 fromAssembly 的生成代码」中被命名引用（用于 setter 的 (T)v 强转）。
        private static bool CanReferenceType(Type t, Assembly fromAssembly)
        {
            if (t == null || t.IsByRef || t.IsPointer || t.IsGenericParameter) return false;
            if (t.IsArray) return CanReferenceType(t.GetElementType(), fromAssembly);
            if (t.IsGenericType && !t.IsGenericTypeDefinition)
            {
                if (!CanReferenceType(t.GetGenericTypeDefinition(), fromAssembly)) return false;
                foreach (Type a in t.GetGenericArguments())
                    if (!CanReferenceType(a, fromAssembly)) return false;
                return true;
            }

            bool visible;
            if (t.IsNested)
            {
                visible = t.IsNestedPublic || ((t.IsNestedAssembly || t.IsNestedFamORAssem) && t.Assembly == fromAssembly);
                if (visible && !CanReferenceType(t.DeclaringType, fromAssembly)) return false;
            }
            else
            {
                visible = t.IsPublic || (t.IsNotPublic && t.Assembly == fromAssembly);
            }
            return visible;
        }

        private static Type ResolveBindableObjectType()
        {
            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.FullName == BindableObjectFullName) return t;
            }
            return null;
        }

        private static bool ReferencesUnityEngine(Assembly asm)
        {
            AssemblyName[] refs = asm.GetReferencedAssemblies();
            for (int i = 0; i < refs.Length; i++)
            {
                string n = refs[i].Name;
                if (n != null && n.StartsWith("UnityEngine", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private struct MemberAccessor
        {
            public string Name;
            public Type MemberType;
            public bool CanGet;
            public bool CanSet;
        }

        private struct VmEntry
        {
            public Type Vm;
            public List<MemberAccessor> Members;
        }

        private struct GenTarget
        {
            public string Path;
            public CodeBuilder Builder;
            public int MemberCount;
        }
    }
}
