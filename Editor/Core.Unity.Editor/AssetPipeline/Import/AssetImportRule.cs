//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 单条资源导入规则：路径 glob 命中后，按 <see cref="Kind"/> 套用对应预设。
    /// 形状对齐既有 <c>BundleBuildRule</c>（Name / PathPatterns / Priority）。
    /// 多模式 OR；多规则按 Priority 降序，资产命中首条。
    /// </summary>
    [Serializable]
    public sealed class AssetImportRule
    {
        [Tooltip("规则名（仅用于显示）")]
        public string Name = "Rule";

        [Tooltip("是否启用该规则")]
        public bool Enabled = true;

        [Tooltip("路径 glob 模式，相对工程根（如 'Assets/GameMain/UI/**/*.png'）。多模式 OR。")]
        public List<string> PathPatterns = new List<string>();

        [Tooltip("数值越大越优先匹配")]
        public int Priority = 0;

        [Tooltip("规则适用的资产类别，决定套用哪组预设")]
        public AssetImportKind Kind = AssetImportKind.Texture;

        public TexturePreset Texture = new TexturePreset();
        public AudioPreset Audio = new AudioPreset();
        public ModelPreset Model = new ModelPreset();

        // ============================================================
        //  贴图预设
        // ============================================================
        [Serializable]
        public sealed class TexturePreset
        {
            public TextureImporterType TextureType = TextureImporterType.Sprite;
            public SpriteImportMode SpriteMode = SpriteImportMode.Single;
            public int MaxTextureSize = 2048;
            public TextureImporterCompression Compression = TextureImporterCompression.Compressed;
            public bool MipmapEnabled = false;
            public bool IsReadable = false;
            [Tooltip("sRGB（颜色贴图勾选；法线/数据贴图取消）")]
            public bool SRGBTexture = true;
            public bool AlphaIsTransparency = true;
            public TextureWrapMode WrapMode = TextureWrapMode.Clamp;
            public FilterMode FilterMode = FilterMode.Bilinear;

            [Tooltip("启用按平台覆盖 maxSize/格式（Android / iOS / Standalone）")]
            public bool OverridePerPlatform = false;
            public int AndroidMaxSize = 1024;
            public TextureImporterFormat AndroidFormat = TextureImporterFormat.ASTC_6x6;
            public int IOSMaxSize = 1024;
            public TextureImporterFormat IOSFormat = TextureImporterFormat.ASTC_6x6;
            public int StandaloneMaxSize = 2048;
            public TextureImporterFormat StandaloneFormat = TextureImporterFormat.DXT5;
        }

        // ============================================================
        //  音频预设
        // ============================================================
        [Serializable]
        public sealed class AudioPreset
        {
            public AudioClipLoadType LoadType = AudioClipLoadType.CompressedInMemory;
            public AudioCompressionFormat CompressionFormat = AudioCompressionFormat.Vorbis;
            [Range(0f, 1f)] public float Quality = 0.7f;
            public bool ForceToMono = false;
            public bool LoadInBackground = true;
        }

        // ============================================================
        //  模型预设（项目暂无模型，保持最小集）
        // ============================================================
        [Serializable]
        public sealed class ModelPreset
        {
            public float GlobalScale = 1f;
            public ModelImporterMaterialImportMode MaterialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            public ModelImporterMeshCompression MeshCompression = ModelImporterMeshCompression.Off;
            public bool OptimizeMesh = true;
            public bool ReadWriteEnabled = false;
            public bool ImportAnimation = true;
        }
    }
}
