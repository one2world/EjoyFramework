//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 把导入规则预设套用到具体 AssetImporter。被首次导入 postprocessor 与菜单批量重置共用，
    /// 集中一处避免两条路径设置不一致。所有方法只改 importer 内存态，不负责保存/重导入。
    /// </summary>
    internal static class AssetImportApplyOps
    {
        private const string PlatformAndroid = "Android";
        private const string PlatformIOS = "iPhone";        // Unity TextureImporter 平台名为 "iPhone"
        private const string PlatformStandalone = "Standalone";

        public static void ApplyTexture(TextureImporter importer, AssetImportRule.TexturePreset p)
        {
            if (importer == null || p == null) return;

            importer.textureType = p.TextureType;
            if (p.TextureType == TextureImporterType.Sprite) importer.spriteImportMode = p.SpriteMode;
            importer.maxTextureSize = p.MaxTextureSize;
            importer.textureCompression = p.Compression;
            importer.mipmapEnabled = p.MipmapEnabled;
            importer.isReadable = p.IsReadable;
            importer.sRGBTexture = p.SRGBTexture;
            importer.alphaIsTransparency = p.AlphaIsTransparency;
            importer.wrapMode = p.WrapMode;
            importer.filterMode = p.FilterMode;

            if (p.OverridePerPlatform)
            {
                SetPlatform(importer, PlatformAndroid, p.AndroidMaxSize, p.AndroidFormat);
                SetPlatform(importer, PlatformIOS, p.IOSMaxSize, p.IOSFormat);
                SetPlatform(importer, PlatformStandalone, p.StandaloneMaxSize, p.StandaloneFormat);
            }
        }

        private static void SetPlatform(TextureImporter importer, string platform, int maxSize, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings ps = importer.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = maxSize;
            ps.format = format;
            importer.SetPlatformTextureSettings(ps);
        }

        public static void ApplyAudio(AudioImporter importer, AssetImportRule.AudioPreset p)
        {
            if (importer == null || p == null) return;

            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = p.LoadType;
            s.compressionFormat = p.CompressionFormat;
            s.quality = p.Quality;
            importer.defaultSampleSettings = s;
            importer.forceToMono = p.ForceToMono;
            importer.loadInBackground = p.LoadInBackground;
        }

        public static void ApplyModel(ModelImporter importer, AssetImportRule.ModelPreset p)
        {
            if (importer == null || p == null) return;

            importer.globalScale = p.GlobalScale;
            importer.materialImportMode = p.MaterialImportMode;
            importer.meshCompression = p.MeshCompression;
            importer.optimizeMeshPolygons = p.OptimizeMesh;
            importer.optimizeMeshVertices = p.OptimizeMesh;
            importer.isReadable = p.ReadWriteEnabled;
            importer.importAnimation = p.ImportAnimation;
        }
    }
}
