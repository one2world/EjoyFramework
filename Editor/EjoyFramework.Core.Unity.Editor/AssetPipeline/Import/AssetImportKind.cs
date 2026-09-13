//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 导入规则适用的资产类别。决定一条规则匹配哪种 AssetImporter，以及套用哪组预设字段。
    /// </summary>
    public enum AssetImportKind
    {
        /// <summary>贴图（TextureImporter）。</summary>
        Texture = 0,
        /// <summary>音频（AudioImporter）。</summary>
        Audio = 1,
        /// <summary>模型（ModelImporter）。</summary>
        Model = 2,
    }
}
