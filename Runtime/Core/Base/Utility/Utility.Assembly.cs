//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;

namespace EjoyFramework.Core
{
    public static partial class Utility
    {
        /// <summary>
        /// 程序集相关的实用函数。
        /// </summary>
        public static class Assembly
        {
            private static System.Reflection.Assembly[] s_Assemblies = null;
            private static readonly Dictionary<string, Type> s_CachedTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            // 负查找缓存：记录"已确认不存在"的类型名，避免重复全程序集扫描。
            // 有界（DefaultMaxNegativeCacheCount），防止恶意/海量未知类型名导致无限增长。
            private static readonly HashSet<string> s_NegativeTypeNames = new HashSet<string>(StringComparer.Ordinal);
            private const int DefaultMaxNegativeCacheCount = 1024;
            // 保护 s_CachedTypes / s_NegativeTypeNames / s_Assemblies 的并发读写。
            // GetType 可能从多个线程调用（如后台资源解析），普通 Dictionary/HashSet 的并发写会损坏内部结构。
            private static readonly object s_Lock = new object();

            static Assembly()
            {
                s_Assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }

            /// <summary>
            /// 获取已加载的程序集。
            /// </summary>
            /// <returns>已加载的程序集。</returns>
            public static System.Reflection.Assembly[] GetAssemblies()
            {
                return s_Assemblies;
            }

            /// <summary>
            /// 获取已加载的程序集中的所有类型。
            /// </summary>
            /// <returns>已加载的程序集中的所有类型。</returns>
            public static Type[] GetTypes()
            {
                List<Type> results = new List<Type>();
                foreach (System.Reflection.Assembly assembly in s_Assemblies)
                {
                    results.AddRange(assembly.GetTypes());
                }

                return results.ToArray();
            }

            /// <summary>
            /// 获取已加载的程序集中的所有类型。
            /// </summary>
            /// <param name="results">已加载的程序集中的所有类型。</param>
            public static void GetTypes(List<Type> results)
            {
                if (results == null)
                {
                    throw new FrameworkException("Results is invalid.");
                }

                results.Clear();
                foreach (System.Reflection.Assembly assembly in s_Assemblies)
                {
                    results.AddRange(assembly.GetTypes());
                }
            }

            /// <summary>
            /// 获取已加载的程序集中的指定类型。
            /// </summary>
            /// <param name="typeName">要获取的类型名。</param>
            /// <returns>已加载的程序集中的指定类型。</returns>
            public static Type GetType(string typeName)
            {
                if (string.IsNullOrEmpty(typeName))
                {
                    throw new FrameworkException("Type name is invalid.");
                }

                // 全程加锁：s_CachedTypes / s_NegativeTypeNames / s_Assemblies 均为非线程安全集合，
                // GetType 可能并发调用；读（TryGetValue/Contains）与冷路径写（Add）必须互斥，否则结构损坏。
                lock (s_Lock)
                {
                    Type type = null;
                    if (s_CachedTypes.TryGetValue(typeName, out type))
                    {
                        return type;
                    }

                    // 命中负缓存：上次扫描所有已加载程序集仍未找到。直接返回，避免重复全扫描。
                    if (s_NegativeTypeNames.Contains(typeName))
                    {
                        return null;
                    }

                    type = Type.GetType(typeName);
                    if (type != null)
                    {
                        s_CachedTypes.Add(typeName, type);
                        return type;
                    }

                    foreach (System.Reflection.Assembly assembly in s_Assemblies)
                    {
                        type = assembly.GetType(typeName);
                        if (type != null)
                        {
                            s_CachedTypes.Add(typeName, type);
                            return type;
                        }
                    }

                    // 冷路径：缓存的程序集列表可能已过期（热更 DLL 在静态初始化后加载）。
                    // 重新查询一次 AppDomain，若程序集集合增长则刷新 s_Assemblies 并仅扫描新增程序集后重试。
                    System.Reflection.Assembly[] current = AppDomain.CurrentDomain.GetAssemblies();
                    if (current.Length > s_Assemblies.Length)
                    {
                        int previousLength = s_Assemblies.Length;
                        s_Assemblies = current;
                        for (int i = previousLength; i < current.Length; i++)
                        {
                            type = current[i].GetType(typeName);
                            if (type != null)
                            {
                                s_CachedTypes.Add(typeName, type);
                                return type;
                            }
                        }
                    }

                    // 确认不存在：写入有界负缓存。超过上限则不再记录（保持正缓存快路径不受影响）。
                    if (s_NegativeTypeNames.Count < DefaultMaxNegativeCacheCount)
                    {
                        s_NegativeTypeNames.Add(typeName);
                    }

                    return null;
                }
            }
        }
    }
}
