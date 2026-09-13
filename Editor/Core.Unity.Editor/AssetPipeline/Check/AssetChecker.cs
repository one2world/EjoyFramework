//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 静态资源检查编排器：反射发现所有 <see cref="IAssetCheckRule"/> 实现（仿 CodeGenHub），
    /// 按配置开关筛选后依次执行，聚合为 <see cref="AssetCheckReport"/>。
    /// 上下文（候选集 + 反向引用索引）只构建一次，全规则共享。
    /// </summary>
    public static class AssetChecker
    {
        private static List<IAssetCheckRule> s_Rules;

        /// <summary>所有发现的规则（缓存，按 Category 排序）。</summary>
        public static IReadOnlyList<IAssetCheckRule> Rules
        {
            get { if (s_Rules == null) Discover(); return s_Rules; }
        }

        public static void RefreshRules() { s_Rules = null; }

        private static void Discover()
        {
            s_Rules = new List<IAssetCheckRule>();
            Type ruleType = typeof(IAssetCheckRule);

            // 复用 CodeGen 的全程序集类型遍历（同 asmdef）。
            foreach (Type t in CodeGen.CodeGenTypeUtil.EnumerateAllTypes())
            {
                if (t.IsAbstract || t.IsInterface) continue;
                if (!ruleType.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                try { s_Rules.Add((IAssetCheckRule)Activator.CreateInstance(t)); }
                catch (Exception ex) { Debug.LogWarning("AssetChecker: failed to instantiate '" + t.FullName + "': " + ex.Message); }
            }
            s_Rules.Sort((a, b) => a.Category.CompareTo(b.Category));
        }

        /// <summary>是否按配置启用某规则类别。</summary>
        private static bool IsEnabled(IAssetCheckRule rule, AssetCheckConfig config)
        {
            switch (rule.Category)
            {
                case AssetCheckCategory.ImportSettings: return config.CheckImportSettings;
                case AssetCheckCategory.Naming: return config.CheckNaming;
                case AssetCheckCategory.Reference: return config.CheckReferences;
                case AssetCheckCategory.Size: return config.CheckSizeRedundancy;
                default: return true;
            }
        }

        /// <summary>执行全部启用的检查规则，返回聚合报告。</summary>
        public static AssetCheckReport RunAll(AssetCheckConfig config)
        {
            if (config == null) throw new FrameworkException("AssetCheckConfig is null.");

            var context = new AssetCheckContext(config);
            var report = new AssetCheckReport { ScannedAssetCount = context.Candidates.Count };

            foreach (IAssetCheckRule rule in Rules)
            {
                if (!IsEnabled(rule, config)) continue;
                try
                {
                    foreach (AssetIssue issue in rule.Check(context, config))
                        report.Add(issue);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[AssetCheck] rule '" + rule.DisplayName + "' threw: " + ex);
                    report.Add(AssetIssue.Make(AssetCheckSeverity.Error, rule.Category, rule.Id, string.Empty,
                        "规则执行异常：" + ex.Message));
                }
            }

            return report;
        }
    }
}
