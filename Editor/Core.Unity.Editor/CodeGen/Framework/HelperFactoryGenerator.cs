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
    /// 扫描 helper / procedure 基类型的具体实现（<c>FrameworkLog.ILogHelper</c> / <c>Utility.Json.IJsonHelper</c> /
    /// <c>UI.IUIFormHelper</c> / <c>Procedure.ProcedureBase</c>），<b>按程序集</b>生成
    /// <c>HelperFactoryRegistrations.g.cs</c>，把「类型全名 → 实例工厂」登记进
    /// <see cref="EjoyFramework.Core.Unity.GeneratedHelperFactory"/>：
    /// <code>GeneratedHelperFactory.Register("Ns.X", static _ =&gt; new Ns.X());           // 纯类
    /// GeneratedHelperFactory.Register("Ns.Y", static p =&gt; { var go=new GameObject(); ... AddComponent&lt;Ns.Y&gt;() }); // MonoBehaviour</code>
    ///
    /// 目的：消除 BaseComponent / ProcedureComponent / UIComponent 按 Inspector 字符串类型名
    /// <c>Utility.Assembly.GetType + Activator.CreateInstance</c> 的反射创建。Inspector 里的类型名字符串
    /// （即 <c>Type.FullName</c>）保持不变 —— 仅替换「字符串 → 实例」的解析机制，无需迁移序列化数据。
    /// 仅对引用了 EjoyFramework.Core.Unity 的程序集生成（否则无法调用 GeneratedHelperFactory）。已接入 <see cref="CodeGenHub"/>。
    /// </summary>
    public sealed class HelperFactoryGenerator : ICodeGenerator
    {
        private const string ToolName = "EjoyFramework.Core.Unity.Editor.CodeGen.HelperFactoryGenerator";
        private const string GeneratedFileName = "HelperFactoryRegistrations.g.cs";
        private const string GeneratedClassName = "HelperFactoryRegistrations";
        private const string CoreUnityAssemblyName = "EjoyFramework.Core.Unity";

        // 待扫描的 helper / procedure 基类型简单名（均定义于 EjoyFramework.Core 程序集）。
        private static readonly string[] BaseTypeSimpleNames = { "ILogHelper", "IJsonHelper", "IUIFormHelper", "ProcedureBase" };

        public string Id => "helperfactory";
        public string DisplayName => "Helper Factory Registry";
        public string Description =>
            "扫描 log/json/UIForm helper 与 ProcedureBase 实现 → 每程序集生成 GeneratedHelperFactory 登记，消除按类型名字符串的反射创建。";

        [MenuItem("EjoyFramework/Core/CodeGen/Generate Helper Factories")]
        public static void Generate()
        {
            CodeGenHub.RunOne(new HelperFactoryGenerator());
        }

        public static void GenerateCli()
        {
            try
            {
                CodeGenResult r = new HelperFactoryGenerator().Run();
                AssetDatabase.Refresh();
                Debug.Log("[CodeGen] Helper Factories (CLI): " + r);
                if (r.Messages != null)
                {
                    for (int i = 0; i < r.Messages.Length; i++) Debug.Log("  • " + r.Messages[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CodeGen] Helper Factories (CLI) failed: " + ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public CodeGenResult Run()
        {
            var messages = new List<string>();
            List<GenTarget> plan = BuildPlan(messages, out int skipped);

            int written = 0, unchanged = 0, total = 0;
            foreach (GenTarget t in plan)
            {
                total += t.Count;
                if (t.Builder.WriteIfChanged(t.Path)) written++; else unchanged++;
            }

            messages.Insert(0, total + " helper/procedure impl(s) across " + plan.Count + " assembly(ies).");
            return new CodeGenResult { Written = written, Unchanged = unchanged, Skipped = skipped, Messages = messages.ToArray() };
        }

        private List<GenTarget> BuildPlan(List<string> messages, out int skipped)
        {
            skipped = 0;
            List<Type> baseTypes = ResolveBaseTypes();
            if (baseTypes.Count == 0)
            {
                messages.Add("No helper/procedure base types found; nothing to generate.");
                return new List<GenTarget>();
            }

            var byAssembly = new Dictionary<Assembly, List<Type>>();
            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition) continue;
                if (baseTypes.Contains(t)) continue;                 // 跳过基类型本身
                if (!IsImplOfAny(t, baseTypes)) continue;
                if (!IsTopLevelAccessible(t)) continue;              // 私有/嵌套不可在生成代码中命名
                bool isMono = IsMonoBehaviour(t);
                if (!isMono && !HasInAssemblyCallableParameterlessCtor(t)) continue; // 纯类需可 new

                if (!byAssembly.TryGetValue(t.Assembly, out List<Type> list))
                {
                    list = new List<Type>();
                    byAssembly[t.Assembly] = list;
                }
                list.Add(t);
            }

            var plan = new List<GenTarget>();
            foreach (KeyValuePair<Assembly, List<Type>> kv in byAssembly)
            {
                Assembly asm = kv.Key;
                List<Type> impls = kv.Value;
                impls.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

                string asmName = asm.GetName().Name;
                if (!ReferencesCoreUnity(asm))
                {
                    skipped++;
                    messages.Add("SKIP '" + asmName + "': does not reference " + CoreUnityAssemblyName +
                                 " — cannot call GeneratedHelperFactory (relies on Editor reflection fallback).");
                    continue;
                }

                string outPath;
                string reason;
                if (!CodeGenTypeUtil.TryGetAssemblyGeneratedPath(asm, null, GeneratedFileName, out outPath, out reason))
                {
                    skipped++;
                    messages.Add("SKIP '" + asmName + "': " + reason);
                    continue;
                }

                string ns = CodeGenTypeUtil.GeneratedNamespaceForAssembly(asm);

                plan.Add(new GenTarget { Path = outPath, Builder = BuildFile(ns, impls), Count = impls.Count });
                messages.Add(asmName + ": " + impls.Count + " impl(s) → " + outPath);
            }
            return plan;
        }

        private static CodeBuilder BuildFile(string ns, List<Type> impls)
        {
            var cb = new CodeBuilder();
            cb.WriteAutoGeneratedHeader(ToolName);
            cb.BeginBlock("namespace " + ns);
            cb.BeginBlock("public static class " + GeneratedClassName);
            cb.Line("[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            cb.BeginBlock("public static void RegisterAll()");

            foreach (Type t in impls)
            {
                string full = "global::" + CodeGenTypeUtil.TypeName(t);
                string key = t.FullName; // Inspector 存储的即 Type.FullName（Utility.Assembly.GetType 按 FullName 解析）
                string factory = IsMonoBehaviour(t)
                    ? "static parent => { var go = new global::UnityEngine.GameObject(" + Quote(t.Name) +
                      "); if (parent != null) go.transform.SetParent(parent, false); return go.AddComponent<" + full + ">(); }"
                    : "static _ => new " + full + "()";
                cb.Line("global::EjoyFramework.Core.Unity.GeneratedHelperFactory.Register(" + Quote(key) + ", " + factory + ");");
            }

            cb.EndBlock();   // RegisterAll
            cb.EndBlock();   // class
            cb.EndBlock();   // namespace
            return cb;
        }

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static List<Type> ResolveBaseTypes()
        {
            var result = new List<Type>();
            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                string asmName = t.Assembly.GetName().Name;
                if (asmName != "EjoyFramework.Core") continue;
                for (int i = 0; i < BaseTypeSimpleNames.Length; i++)
                {
                    if (t.Name == BaseTypeSimpleNames[i] && !result.Contains(t)) { result.Add(t); break; }
                }
            }
            return result;
        }

        private static bool IsImplOfAny(Type t, List<Type> baseTypes)
        {
            for (int i = 0; i < baseTypes.Count; i++)
            {
                if (baseTypes[i].IsAssignableFrom(t)) return true;
            }
            return false;
        }

        private static bool IsMonoBehaviour(Type t)
        {
            for (Type cur = t; cur != null; cur = cur.BaseType)
            {
                if (cur.FullName == "UnityEngine.MonoBehaviour") return true;
            }
            return false;
        }

        private static bool IsTopLevelAccessible(Type t)
        {
            if (t.IsNested) return false; // 生成代码无法命名嵌套私有/受保护类型；helper/procedure 实现均为顶层
            return t.IsPublic || t.IsNotPublic; // 顶层 public 或 internal（同程序集生成代码可访问）
        }

        private static bool HasInAssemblyCallableParameterlessCtor(Type t)
        {
            ConstructorInfo c = t.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (c == null) return false;
            return c.IsPublic || c.IsAssembly || c.IsFamilyOrAssembly;
        }

        private static bool ReferencesCoreUnity(Assembly asm)
        {
            if (asm.GetName().Name == CoreUnityAssemblyName) return true;
            AssemblyName[] refs = asm.GetReferencedAssemblies();
            for (int i = 0; i < refs.Length; i++)
            {
                if (refs[i].Name == CoreUnityAssemblyName) return true;
            }
            return false;
        }

        private struct GenTarget
        {
            public string Path;
            public CodeBuilder Builder;
            public int Count;
        }
    }
}
