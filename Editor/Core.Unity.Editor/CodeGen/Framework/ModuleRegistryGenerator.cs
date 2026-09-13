//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 扫描所有 <see cref="EjoyFramework.Core.FrameworkModule"/> 的具体子类，按"实现类与接口同名去 I 前缀"
    /// 约定匹配出其模块接口（如 <c>CoroutineManager</c> → <c>ICoroutineManager</c>），<b>按程序集</b>分别生成一份
    /// <c>FrameworkModuleRegistrations.g.cs</c>，内含静态工厂注册：
    /// <code>Framework.RegisterFactory(typeof(IXxx), static () =&gt; new Xxx());</code>
    ///
    /// 为何必须按程序集生成：很多模块实现类是 <c>internal</c>（如 <c>CoroutineManager</c>），
    /// 只有把注册代码编译进它<b>所在的同一程序集</b>才能 <c>new</c> 它。生成文件被放到该程序集 .asmdef 目录下的
    /// <c>Generated/</c> 子目录，从而归属同一程序集。
    ///
    /// 为何能修复 iOS：<c>static () =&gt; new Xxx()</c> 是<b>静态引用</b>，IL2CPP 托管裁剪会保留类型<b>及其构造函数</b>，
    /// 不再依赖反射 + <c>link.xml</c>（后者在本工程未被链接器采纳，导致 "Can not find module type" 崩溃）。
    /// 已接入统一 <see cref="CodeGenHub"/>。
    /// </summary>
    public sealed class ModuleRegistryGenerator : ICodeGenerator
    {
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.ModuleRegistryGenerator";
        private const string GeneratedFileName = "FrameworkModuleRegistrations.g.cs";
        private const string GeneratedClassName = "FrameworkModuleRegistrations";

        public string Id => "moduleregistry";
        public string DisplayName => "Framework Module Registry";
        public string Description =>
            "扫描 FrameworkModule 子类 → 每个程序集生成 FrameworkModuleRegistrations（静态 new 工厂注册），消除 GetModule 反射与 IL2CPP 裁剪。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate Module Registry")]
        public static void Generate()
        {
            CodeGenHub.RunOne(new ModuleRegistryGenerator());
        }

        /// <summary>
        /// 无头/CI 入口：
        /// <c>Unity -batchmode -quit -executeMethod EjoyFramework.Core.Unity.Editor.CodeGen.ModuleRegistryGenerator.GenerateCli</c>
        /// 写出生成文件并刷新资产数据库；批处理下失败以非零码退出。
        /// </summary>
        public static void GenerateCli()
        {
            try
            {
                CodeGenResult r = new ModuleRegistryGenerator().Run();
                AssetDatabase.Refresh();
                Debug.Log("[CodeGen] Module Registry (CLI): " + r);
                if (r.Messages != null)
                {
                    for (int i = 0; i < r.Messages.Length; i++) Debug.Log("  • " + r.Messages[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CodeGen] Module Registry (CLI) failed: " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public CodeGenResult Run()
        {
            var messages = new List<string>();
            List<GenTarget> plan = BuildPlan(messages, out int skipped);

            int written = 0, unchanged = 0, totalModules = 0;
            foreach (GenTarget t in plan)
            {
                totalModules += t.ModuleCount;
                if (t.Builder.WriteIfChanged(t.Path)) written++; else unchanged++;
            }

            messages.Insert(0, totalModules + " module(s) across " + plan.Count + " assembly(ies).");
            return new CodeGenResult { Written = written, Unchanged = unchanged, Skipped = skipped, Messages = messages.ToArray() };
        }

        /// <summary>
        /// 构建期校验：重新生成各程序集注册表内容（不落盘）并与磁盘 .g.cs 比对（忽略行尾差异）。
        /// 任一缺失或不一致即视为陈旧——通常是"新增模块后忘了重跑 CodeGen"。返回 false 表示陈旧，
        /// <paramref name="report"/> 列出缺失/陈旧文件，供构建前置 hook 阻断并提示。
        /// </summary>
        public static bool VerifyUpToDate(out string report)
        {
            var messages = new List<string>();
            List<GenTarget> plan = new ModuleRegistryGenerator().BuildPlan(messages, out _);
            var stale = new List<string>();
            foreach (GenTarget t in plan)
            {
                if (!File.Exists(t.Path)) { stale.Add("MISSING: " + t.Path); continue; }
                string expected = Normalize(t.Builder.ToString());
                string onDisk = Normalize(File.ReadAllText(t.Path, System.Text.Encoding.UTF8));
                if (expected != onDisk) stale.Add("STALE:   " + t.Path);
            }
            report = stale.Count == 0 ? "module registry up to date" : string.Join("\n", stale);
            return stale.Count == 0;
        }

        private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

        // 发现 → 分组 → 逐程序集构建 (path + CodeBuilder)；不落盘。messages 收集信息/告警；skipped = 无 asmdef 的程序集数。
        private List<GenTarget> BuildPlan(List<string> messages, out int skipped)
        {
            skipped = 0;
            var byAssembly = new Dictionary<Assembly, List<ModuleEntry>>();
            Type baseType = typeof(EjoyFramework.Core.FrameworkModule);

            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (!baseType.IsAssignableFrom(t) || t == baseType) continue;
                // 不为测试/编辑器程序集生成注册：其内的假模块（单测用）不进玩家构建，也不应被自动注册。
                if (CodeGenTypeUtil.IsTestOrEditorAssembly(t.Assembly)) continue;

                Type iface = FindModuleInterface(t);
                if (iface == null)
                {
                    // 无 'I<Name>' 模块接口：通常是显式 RegisterModule 注册的模块，按约定不自动注册，静默跳过。
                    messages.Add("skip (no matching 'I" + t.Name + "' interface): " + t.FullName);
                    continue;
                }
                if (!HasInAssemblyCallableParameterlessCtor(t))
                {
                    // 有模块接口却无可用无参 ctor：无法静态 new，必须显式 RegisterModule。值得显式提醒（非测试/编辑器程序集）。
                    string warn = "module '" + t.FullName + "' implements " + iface.Name +
                                  " but has no accessible parameterless constructor — NOT auto-registered. " +
                                  "Register it explicitly via Framework.RegisterModule, or it will hit the reflection fallback and may strip on IL2CPP.";
                    messages.Add("skip: " + warn);
                    if (!CodeGenTypeUtil.IsTestOrEditorAssembly(t.Assembly)) UnityEngine.Debug.LogWarning("[CodeGen][ModuleRegistry] " + warn);
                    continue;
                }

                if (!byAssembly.TryGetValue(t.Assembly, out List<ModuleEntry> list))
                {
                    list = new List<ModuleEntry>();
                    byAssembly[t.Assembly] = list;
                }
                list.Add(new ModuleEntry { Impl = t, Interface = iface });
            }

            var plan = new List<GenTarget>();
            foreach (KeyValuePair<Assembly, List<ModuleEntry>> kv in byAssembly)
            {
                Assembly asm = kv.Key;
                List<ModuleEntry> entries = kv.Value;
                entries.Sort((a, b) => string.CompareOrdinal(a.Impl.FullName, b.Impl.FullName));

                string asmName = asm.GetName().Name;
                string outPath;
                string reason;
                if (!CodeGenTypeUtil.TryGetAssemblyGeneratedPath(asm, null, GeneratedFileName, out outPath, out reason))
                {
                    skipped++;
                    messages.Add("SKIP '" + asmName + "' (" + entries.Count + " modules): " + reason);
                    continue;
                }

                bool engineRef = ReferencesUnityEngine(asm);
                string ns = CodeGenTypeUtil.GeneratedNamespaceForAssembly(asm);

                plan.Add(new GenTarget { Path = outPath, Builder = BuildFile(ns, engineRef, entries), ModuleCount = entries.Count });
                messages.Add(asmName + ": " + entries.Count + " module(s) → " + outPath +
                             (engineRef ? "  [self-triggers via RuntimeInitializeOnLoadMethod]"
                                        : "  [pure C# — RegisterAll() triggered by a Unity-side bootstrap]"));
            }
            return plan;
        }

        private static CodeBuilder BuildFile(string ns, bool engineRef, List<ModuleEntry> entries)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.BeginBlock("namespace " + ns);
            cb.BeginBlock("public static class " + GeneratedClassName);

            if (engineRef)
            {
                // 该程序集引用了 UnityEngine：直接挂 RuntimeInitializeOnLoadMethod 自触发（仍可被显式调用，幂等）。
                cb.Line("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            }
            cb.BeginBlock("public static void RegisterAll()");
            foreach (ModuleEntry e in entries)
            {
                string iface = "global::" + CodeGenTypeUtil.TypeName(e.Interface);
                string impl = "global::" + CodeGenTypeUtil.TypeName(e.Impl);
                cb.Line("global::EjoyFramework.Core.Framework.RegisterFactory(typeof(" + iface +
                        "), static () => new " + impl + "());");
            }
            cb.EndBlock();   // RegisterAll
            cb.EndBlock();   // class
            cb.EndBlock();   // namespace
            return cb;
        }

        // 在 impl 实现的接口里找名为 'I' + impl.Name 的那个（GetModule 调用时使用的接口）。
        private static Type FindModuleInterface(Type impl)
        {
            string want = "I" + impl.Name;
            Type[] interfaces = impl.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (string.Equals(interfaces[i].Name, want, StringComparison.Ordinal)) return interfaces[i];
            }
            return null;
        }

        // 是否存在可在"同程序集生成代码"中以 new() 调用的无参构造（public / internal / protected internal）。
        private static bool HasInAssemblyCallableParameterlessCtor(Type t)
        {
            ConstructorInfo c = t.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (c == null) return false;
            return c.IsPublic || c.IsAssembly || c.IsFamilyOrAssembly;
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

        private struct ModuleEntry
        {
            public Type Impl;
            public Type Interface;
        }

        // 一个待生成文件:目标路径 + 内容构建器 + 该程序集模块数。
        private struct GenTarget
        {
            public string Path;
            public CodeBuilder Builder;
            public int ModuleCount;
        }
    }
}
