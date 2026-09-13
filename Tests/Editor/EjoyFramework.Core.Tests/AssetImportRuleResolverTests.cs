//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using UnityEngine;
using EjoyFramework.Core.Unity.Editor.AssetPipeline;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// AssetImportRuleResolver 纯逻辑测试：优先级排序、glob 命中、Kind 过滤、Exclude。
    /// 不依赖 AssetDatabase（仅构造 ScriptableObject 配置）。
    /// </summary>
    public class AssetImportRuleResolverTests
    {
        private static AssetImportRule Rule(string name, int priority, AssetImportKind kind, params string[] patterns)
        {
            var r = new AssetImportRule { Name = name, Priority = priority, Kind = kind, Enabled = true };
            r.PathPatterns.Clear();
            r.PathPatterns.AddRange(patterns);
            return r;
        }

        private static AssetImportRuleConfig Config(params AssetImportRule[] rules)
        {
            var cfg = ScriptableObject.CreateInstance<AssetImportRuleConfig>();
            cfg.ExcludePatterns.Clear();
            cfg.Rules.Clear();
            cfg.Rules.AddRange(rules);
            return cfg;
        }

        [Test]
        public void Resolve_HigherPriority_WinsOnOverlap()
        {
            var cfg = Config(
                Rule("low", 0, AssetImportKind.Texture, "Assets/GameMain/**"),
                Rule("high", 10, AssetImportKind.Texture, "Assets/GameMain/UI/**"));
            var resolver = new AssetImportRuleResolver(cfg);

            AssetImportRule hit = resolver.Resolve("Assets/GameMain/UI/Textures/a.png", AssetImportKind.Texture);
            Assert.IsNotNull(hit);
            Assert.AreEqual("high", hit.Name);
        }

        [Test]
        public void Resolve_KindMismatch_DoesNotMatch()
        {
            var cfg = Config(Rule("tex", 0, AssetImportKind.Texture, "Assets/**"));
            var resolver = new AssetImportRuleResolver(cfg);

            Assert.IsNull(resolver.Resolve("Assets/sfx/a.wav", AssetImportKind.Audio));
            Assert.IsNotNull(resolver.Resolve("Assets/img/a.png", AssetImportKind.Texture));
        }

        [Test]
        public void Resolve_DisabledRule_Skipped()
        {
            var disabled = Rule("off", 100, AssetImportKind.Texture, "Assets/**");
            disabled.Enabled = false;
            var cfg = Config(disabled, Rule("on", 0, AssetImportKind.Texture, "Assets/**"));
            var resolver = new AssetImportRuleResolver(cfg);

            AssetImportRule hit = resolver.Resolve("Assets/x.png", AssetImportKind.Texture);
            Assert.IsNotNull(hit);
            Assert.AreEqual("on", hit.Name);
        }

        [Test]
        public void Resolve_Excluded_ReturnsNull()
        {
            var cfg = Config(Rule("tex", 0, AssetImportKind.Texture, "Assets/**"));
            cfg.ExcludePatterns.Add("**/Editor/**");
            var resolver = new AssetImportRuleResolver(cfg);

            Assert.IsTrue(resolver.IsExcluded("Assets/Foo/Editor/a.png"));
            Assert.IsNull(resolver.Resolve("Assets/Foo/Editor/a.png", AssetImportKind.Texture));
            Assert.IsNotNull(resolver.Resolve("Assets/Foo/a.png", AssetImportKind.Texture));
        }

        [Test]
        public void Resolve_NoMatch_ReturnsNull()
        {
            var cfg = Config(Rule("ui", 0, AssetImportKind.Texture, "Assets/GameMain/UI/**"));
            var resolver = new AssetImportRuleResolver(cfg);
            Assert.IsNull(resolver.Resolve("Assets/Other/a.png", AssetImportKind.Texture));
        }
    }
}
