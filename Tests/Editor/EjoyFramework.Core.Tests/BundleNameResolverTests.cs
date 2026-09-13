//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Unity.Editor.Resource;

namespace EjoyFramework.Tests
{
    public class BundleNameResolverTests
    {
        private static AssetBundleBuildConfig NewConfig()
        {
            // ScriptableObject 在 EditMode 测试中可 CreateInstance
            return UnityEngine.ScriptableObject.CreateInstance<AssetBundleBuildConfig>();
        }

        [Test]
        public void PerDirectory_Default()
        {
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.PerDirectory;
            var r = new BundleNameResolver(cfg);
            Assert.AreEqual("gamemain_ui_mainmenu", r.Resolve("Assets/GameMain/UI/MainMenu/MainMenu.prefab", 1024));
            Assert.AreEqual("gamemain_effect", r.Resolve("Assets/GameMain/Effect/Boom.prefab", 1024));
        }

        [Test]
        public void PerFile_Strategy()
        {
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.PerFile;
            var r = new BundleNameResolver(cfg);
            Assert.AreEqual("gamemain_ui_mainmenu_mainmenu", r.Resolve("Assets/GameMain/UI/MainMenu/MainMenu.prefab", 1024));
        }

        [Test]
        public void SizeLimit_SplitsWhenAccumulated()
        {
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.SizeLimit;
            cfg.SizeLimitBytes = 1000; // 1KB
            var r = new BundleNameResolver(cfg);
            string a = r.Resolve("Assets/Effects/E1.prefab", 400);
            string b = r.Resolve("Assets/Effects/E2.prefab", 400);
            string c = r.Resolve("Assets/Effects/E3.prefab", 400); // 累计 1200 > 1000，切割
            Assert.AreEqual("effects", a);
            Assert.AreEqual("effects", b);
            Assert.AreEqual("effects_1", c);
        }

        [Test]
        public void SizeLimit_SingleAssetExceeds_StillEmits()
        {
            // 单 asset 大于 limit 也应能入包（不无限切割）
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.SizeLimit;
            cfg.SizeLimitBytes = 100;
            var r = new BundleNameResolver(cfg);
            string a = r.Resolve("Assets/Huge.fbx", 99999);
            Assert.AreEqual("huge_fbx", "huge_fbx"); // sanity
            Assert.AreEqual("root", "root"); // sanity
            // 第一个 asset 应进入第 0 chunk
            Assert.AreEqual("root", a);
        }

        [Test]
        public void Rule_HighPriorityWins()
        {
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.PerDirectory;
            cfg.BuildRules = new List<BundleBuildRule>
            {
                new BundleBuildRule
                {
                    Name = "UISingleBundle",
                    PathPatterns = new List<string> { "Assets/GameMain/UI/**" },
                    Priority = 100,
                    Strategy = BundleSplitStrategy.SingleBundle,
                    SingleBundleName = "ui_shared",
                },
                new BundleBuildRule
                {
                    Name = "AllPerDir",
                    PathPatterns = new List<string> { "**" },
                    Priority = 0,
                    Strategy = BundleSplitStrategy.PerDirectory,
                },
            };
            var r = new BundleNameResolver(cfg);
            Assert.AreEqual("ui_shared", r.Resolve("Assets/GameMain/UI/MainMenu/MainMenu.prefab", 1));
            Assert.AreEqual("gamemain_effect", r.Resolve("Assets/GameMain/Effect/Boom.prefab", 1));
        }

        [Test]
        public void Rule_PerFileOverridesDefault()
        {
            var cfg = NewConfig();
            cfg.DefaultStrategy = BundleSplitStrategy.PerDirectory;
            cfg.BuildRules = new List<BundleBuildRule>
            {
                new BundleBuildRule
                {
                    Name = "ScenesPerFile",
                    PathPatterns = new List<string> { "**/*.unity" },
                    Priority = 10,
                    Strategy = BundleSplitStrategy.PerFile,
                },
            };
            var r = new BundleNameResolver(cfg);
            Assert.AreEqual("scenes_main", r.Resolve("Assets/Scenes/Main.unity", 1));
            Assert.AreEqual("gamemain_ui_mainmenu", r.Resolve("Assets/GameMain/UI/MainMenu/MainMenu.prefab", 1));
        }
    }
}
