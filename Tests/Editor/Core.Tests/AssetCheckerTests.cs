//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EjoyFramework.Core.Unity.Editor.AssetPipeline;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// AssetChecker 编排器测试：规则反射发现 + 在真实工程子目录上跑出报告。
    /// </summary>
    public class AssetCheckerTests
    {
        [Test]
        public void Rules_Discovers_AllFourCategories()
        {
            AssetChecker.RefreshRules();
            var categories = new HashSet<AssetCheckCategory>();
            foreach (IAssetCheckRule rule in AssetChecker.Rules) categories.Add(rule.Category);

            Assert.IsTrue(categories.Contains(AssetCheckCategory.ImportSettings), "缺 ImportSettings 规则");
            Assert.IsTrue(categories.Contains(AssetCheckCategory.Naming), "缺 Naming 规则");
            Assert.IsTrue(categories.Contains(AssetCheckCategory.Reference), "缺 Reference 规则");
            Assert.IsTrue(categories.Contains(AssetCheckCategory.Size), "缺 Size 规则");
        }

        [Test]
        public void RunAll_OnUiTextures_ProducesConsistentReport()
        {
            var cfg = ScriptableObject.CreateInstance<AssetCheckConfig>();
            cfg.IncludeDirectories = new List<string> { "Assets/GameMain/UI/Textures" };
            cfg.DetectOrphans = false;          // 跳过全工程反向索引，保证测试快
            cfg.DetectDuplicates = false;       // 跳过逐文件 hash，保证测试快

            AssetCheckReport report = AssetChecker.RunAll(cfg);

            Assert.IsNotNull(report);
            Assert.GreaterOrEqual(report.ScannedAssetCount, 0);
            // 计数一致性：三档之和 == 问题总数
            Assert.AreEqual(report.Issues.Count, report.ErrorCount + report.WarningCount + report.InfoCount);
        }
    }
}
