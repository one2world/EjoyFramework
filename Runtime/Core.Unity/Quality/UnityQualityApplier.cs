//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Quality;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 把内置旋钮落到 Unity：阴影距离、LOD 偏置、全局贴图 mip 下限、目标帧率（档位变化时），
    /// 渲染缩放经 <see cref="ScalableBufferManager"/>（仅对开启 allowDynamicResolution 的相机、且平台支持时生效）。
    /// HDRP / URP 的动态分辨率走各自管线：HDRP 用 <c>DynamicResolutionHandler.SetDynamicResScaler(() =&gt; quality.RenderScale * 100f, ReturnsPercentage)</c>，
    /// URP 写 <c>UniversalRenderPipelineAsset.renderScale</c>，由业务注册自己的 <see cref="IQualityApplier"/>，并关掉 <see cref="ApplyRenderScale"/>。
    /// </summary>
    public sealed class UnityQualityApplier : IQualityApplier
    {
        public bool ApplyShadowDistance = true;
        public bool ApplyLodBias = true;
        public bool ApplyTextureMipLimit = true;
        public bool ApplyTargetFrameRate = true;
        public bool ApplyRenderScale = true;

        public void OnQualityChanged(IQualityManager quality, QualityChange change)
        {
            if ((change & QualityChange.Level) != 0)
            {
                if (ApplyShadowDistance && quality.HasKnob(QualityKnobs.ShadowDistance)) QualitySettings.shadowDistance = quality.GetKnob(QualityKnobs.ShadowDistance);
                if (ApplyLodBias && quality.HasKnob(QualityKnobs.LodBias)) QualitySettings.lodBias = quality.GetKnob(QualityKnobs.LodBias);
                if (ApplyTextureMipLimit && quality.HasKnob(QualityKnobs.TextureMipLimit)) QualitySettings.globalTextureMipmapLimit = Mathf.RoundToInt(quality.GetKnob(QualityKnobs.TextureMipLimit));
                if (ApplyTargetFrameRate && quality.HasKnob(QualityKnobs.TargetFrameRate)) Application.targetFrameRate = Mathf.RoundToInt(quality.GetKnob(QualityKnobs.TargetFrameRate));
            }

            if (ApplyRenderScale && (change & (QualityChange.Level | QualityChange.RenderScale)) != 0)
            {
                float scale = quality.RenderScale;
                ScalableBufferManager.ResizeBuffers(scale, scale);
            }
        }
    }
}
