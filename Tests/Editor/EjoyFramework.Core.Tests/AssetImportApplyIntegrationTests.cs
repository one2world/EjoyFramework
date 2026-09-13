//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EjoyFramework.Core.Unity.Editor.AssetPipeline;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 导入规则批量 Apply 集成测试：自建临时贴图 → Apply 规则 → 断言 TextureImporter 被改 → 删除临时资产。
    /// 创建并清理自身资产，不污染工程。
    /// </summary>
    public class AssetImportApplyIntegrationTests
    {
        private const string TempDir = "Assets/__ejoy_import_test";
        private const string TempPng = TempDir + "/probe.png";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "__ejoy_import_test");

            var tex = new Texture2D(8, 8);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(TempPng, png);
            AssetDatabase.ImportAsset(TempPng, ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempDir);
        }

        [Test]
        public void ApplyToPaths_AppliesTexturePreset()
        {
            var rule = new AssetImportRule { Name = "probe", Priority = 0, Kind = AssetImportKind.Texture, Enabled = true };
            rule.PathPatterns.Clear();
            rule.PathPatterns.Add(TempDir + "/**");
            rule.Texture.TextureType = TextureImporterType.Sprite;
            rule.Texture.MaxTextureSize = 32;
            rule.Texture.MipmapEnabled = false;

            var cfg = ScriptableObject.CreateInstance<AssetImportRuleConfig>();
            cfg.ExcludePatterns.Clear();
            cfg.Rules.Clear();
            cfg.Rules.Add(rule);

            int applied = AssetImportRulesApplier.ApplyToPaths(new[] { TempPng }, cfg);
            Assert.AreEqual(1, applied, "应有 1 个资产被套用");

            var importer = (TextureImporter)AssetImporter.GetAtPath(TempPng);
            Assert.IsNotNull(importer);
            Assert.AreEqual(32, importer.maxTextureSize, "maxTextureSize 应被规则改为 32");
            Assert.AreEqual(TextureImporterType.Sprite, importer.textureType, "textureType 应被改为 Sprite");
            Assert.IsFalse(importer.mipmapEnabled, "mipmap 应被关闭");
        }
    }
}
