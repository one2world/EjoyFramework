//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 代码生成中枢：反射发现所有 <see cref="ICodeGenerator"/> 实现，提供单个/全部运行与统一刷新。
    /// 新增生成器只需实现 <see cref="ICodeGenerator"/> 并提供无参构造，自动出现在
    /// <see cref="CodeGenWindow"/> 与 "Generate All" 菜单中——无需改动本类。
    /// </summary>
    public static class CodeGenHub
    {
        private static List<ICodeGenerator> s_Generators;

        /// <summary>所有已发现的生成器（按 DisplayName 排序，结果缓存）。</summary>
        public static IReadOnlyList<ICodeGenerator> Generators
        {
            get
            {
                if (s_Generators == null) Discover();
                return s_Generators;
            }
        }

        /// <summary>重新扫描程序集发现生成器（域重载后或新增生成器时调用）。</summary>
        public static void Refresh()
        {
            s_Generators = null;
        }

        private static void Discover()
        {
            s_Generators = new List<ICodeGenerator>();
            Type interfaceType = typeof(ICodeGenerator);

            foreach (Type t in CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!interfaceType.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                try
                {
                    s_Generators.Add((ICodeGenerator)Activator.CreateInstance(t));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("CodeGenHub: failed to instantiate generator '" + t.FullName + "': " + ex.Message);
                }
            }

            s_Generators.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        }

        /// <summary>运行单个生成器并统一刷新资产数据库。返回其结果。</summary>
        public static CodeGenResult RunOne(ICodeGenerator generator)
        {
            if (generator == null) throw new FrameworkException("generator is null.");
            CodeGenResult result = SafeRun(generator);
            AssetDatabase.Refresh();
            Debug.Log("[CodeGen] " + generator.DisplayName + ": " + result);
            return result;
        }

        /// <summary>运行全部生成器；聚合统计，最后统一刷新一次。</summary>
        public static CodeGenResult RunAll()
        {
            int written = 0, unchanged = 0, skipped = 0;
            var messages = new List<string>();

            foreach (ICodeGenerator g in Generators)
            {
                CodeGenResult r = SafeRun(g);
                written += r.Written;
                unchanged += r.Unchanged;
                skipped += r.Skipped;
                messages.Add(g.DisplayName + ": " + r);
                if (r.Messages != null)
                {
                    for (int i = 0; i < r.Messages.Length; i++)
                        messages.Add("  • " + r.Messages[i]);
                }
            }

            AssetDatabase.Refresh();

            var aggregate = new CodeGenResult
            {
                Written = written,
                Unchanged = unchanged,
                Skipped = skipped,
                Messages = messages.ToArray(),
            };
            Debug.Log("[CodeGen] Generate All — " + aggregate);
            return aggregate;
        }

        private static CodeGenResult SafeRun(ICodeGenerator generator)
        {
            try
            {
                return generator.Run();
            }
            catch (Exception ex)
            {
                Debug.LogError("[CodeGen] generator '" + generator.DisplayName + "' threw: " + ex);
                return CodeGenResult.Empty("ERROR: " + ex.Message);
            }
        }

        [MenuItem("EjoyFramework/Core/CodeGen/Generate All", priority = 1)]
        public static void GenerateAllMenu()
        {
            RunAll();
        }
    }
}
