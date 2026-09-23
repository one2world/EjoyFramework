//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Quality
{
    /// <summary>
    /// 内置画质旋钮 id 与默认档位表（级别 0..4）。业务自定义旋钮从 <see cref="Custom"/> 起，用 <see cref="IQualityManager.DefineKnob"/> 注册。
    /// 旋钮只是"每档一个数"，由 <see cref="IQualityApplier"/> 落到引擎 / 系统上——框架 Unity 层应用内置旋钮，业务应用自己的。
    /// </summary>
    public static class QualityKnobs
    {
        public const int LevelCount = 5;

        /// <summary>渲染分辨率缩放上限（自动画质在 [MinRenderScale, 该值] 内动态调）。</summary>
        public const int RenderScale = 1;

        /// <summary>阴影距离（米）。</summary>
        public const int ShadowDistance = 2;

        /// <summary>LOD 偏置（越大越晚切低模）。</summary>
        public const int LodBias = 3;

        /// <summary>全局贴图 mip 下限（0 = 全分辨率，1 = 半分辨率……）。</summary>
        public const int TextureMipLimit = 4;

        /// <summary>世界流送半径缩放（写入 IWorldStreamingManager.RadiusScale）。</summary>
        public const int StreamingRadiusScale = 5;

        /// <summary>种群 / NPC 密度缩放（业务或 PopulationManager 预算使用）。</summary>
        public const int PopulationScale = 6;

        /// <summary>粒子 / 特效预算缩放。</summary>
        public const int ParticleBudgetScale = 7;

        /// <summary>目标帧率（Application.targetFrameRate；也作为自动画质的帧预算）。</summary>
        public const int TargetFrameRate = 8;

        /// <summary>业务自定义旋钮起点。</summary>
        public const int Custom = 1000;

        internal static void DefineDefaults(IQualityManager manager)
        {
            manager.DefineKnob(RenderScale, "RenderScale", new[] { 0.75f, 0.85f, 0.9f, 1f, 1f });
            manager.DefineKnob(ShadowDistance, "ShadowDistance", new[] { 30f, 50f, 80f, 120f, 180f });
            manager.DefineKnob(LodBias, "LodBias", new[] { 0.6f, 0.8f, 1f, 1.5f, 2f });
            manager.DefineKnob(TextureMipLimit, "TextureMipLimit", new[] { 2f, 1f, 1f, 0f, 0f });
            manager.DefineKnob(StreamingRadiusScale, "StreamingRadiusScale", new[] { 0.6f, 0.75f, 0.9f, 1f, 1.2f });
            manager.DefineKnob(PopulationScale, "PopulationScale", new[] { 0.4f, 0.6f, 0.8f, 1f, 1f });
            manager.DefineKnob(ParticleBudgetScale, "ParticleBudgetScale", new[] { 0.3f, 0.5f, 0.75f, 1f, 1f });
            manager.DefineKnob(TargetFrameRate, "TargetFrameRate", new[] { 30f, 30f, 60f, 60f, 60f });
        }
    }
}
