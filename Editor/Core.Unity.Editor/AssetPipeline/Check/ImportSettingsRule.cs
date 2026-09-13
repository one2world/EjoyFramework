//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 导入设置合规检查：读取 <see cref="AssetImportRuleConfig"/>，对命中导入规则的贴图/音频，
    /// 比对其<b>实际</b>导入设置与规则预设的偏差并告警。
    /// 与"资源导入管理"模块联动——存量资产被手动改偏、或未跑 Apply 的资产会在此暴露。
    /// 无导入规则配置时本规则不产出（no-op）。
    /// </summary>
    public sealed class ImportSettingsRule : IAssetCheckRule
    {
        public string Id => "import-settings";
        public string DisplayName => "导入设置合规";
        public AssetCheckCategory Category => AssetCheckCategory.ImportSettings;

        public IEnumerable<AssetIssue> Check(AssetCheckContext context, AssetCheckConfig config)
        {
            var issues = new List<AssetIssue>();

            AssetImportRuleConfig importConfig = AssetImportRuleConfig.FindExisting();
            if (importConfig == null) return issues;   // 无导入规则 → 不检查
            var resolver = new AssetImportRuleResolver(importConfig);

            foreach (string path in context.Candidates)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null) continue;

                if (importer is TextureImporter tex)
                {
                    AssetImportRule rule = resolver.Resolve(path, AssetImportKind.Texture);
                    if (rule != null) CompareTexture(tex, rule, path, issues);
                }
                else if (importer is AudioImporter audio)
                {
                    AssetImportRule rule = resolver.Resolve(path, AssetImportKind.Audio);
                    if (rule != null) CompareAudio(audio, rule, path, issues);
                }
            }

            return issues;
        }

        private void CompareTexture(TextureImporter tex, AssetImportRule rule, string path, List<AssetIssue> issues)
        {
            var p = rule.Texture;
            if (tex.textureType != p.TextureType)
                Add(issues, path, "贴图 TextureType 实际 " + tex.textureType + "，规则期望 " + p.TextureType);
            if (tex.maxTextureSize != p.MaxTextureSize)
                Add(issues, path, "贴图 maxTextureSize 实际 " + tex.maxTextureSize + "，规则期望 " + p.MaxTextureSize);
            if (tex.textureCompression != p.Compression)
                Add(issues, path, "贴图压缩 实际 " + tex.textureCompression + "，规则期望 " + p.Compression);
            if (tex.mipmapEnabled != p.MipmapEnabled)
                Add(issues, path, "贴图 mipmap 实际 " + tex.mipmapEnabled + "，规则期望 " + p.MipmapEnabled);
            if (tex.isReadable != p.IsReadable)
                Add(issues, path, "贴图 Read/Write 实际 " + tex.isReadable + "，规则期望 " + p.IsReadable);
            if (tex.sRGBTexture != p.SRGBTexture)
                Add(issues, path, "贴图 sRGB 实际 " + tex.sRGBTexture + "，规则期望 " + p.SRGBTexture);
        }

        private void CompareAudio(AudioImporter audio, AssetImportRule rule, string path, List<AssetIssue> issues)
        {
            var p = rule.Audio;
            AudioImporterSampleSettings s = audio.defaultSampleSettings;
            if (s.loadType != p.LoadType)
                Add(issues, path, "音频 LoadType 实际 " + s.loadType + "，规则期望 " + p.LoadType);
            if (s.compressionFormat != p.CompressionFormat)
                Add(issues, path, "音频压缩 实际 " + s.compressionFormat + "，规则期望 " + p.CompressionFormat);
            if (audio.forceToMono != p.ForceToMono)
                Add(issues, path, "音频 ForceToMono 实际 " + audio.forceToMono + "，规则期望 " + p.ForceToMono);
        }

        private void Add(List<AssetIssue> issues, string path, string message)
        {
            issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path, message));
        }
    }
}
