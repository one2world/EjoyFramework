//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 缓动曲线类型。归一化时间 t ∈ [0,1] 经曲线映射为缓动后的进度。
    /// 多数曲线在端点处映射为 0 与 1；OutBack/OutElastic 在过程中会越过 [0,1]（过冲），
    /// 但仍在 t=1 处回到 1。
    /// </summary>
    public enum Ease
    {
        /// <summary>线性，无缓动。</summary>
        Linear,

        /// <summary>二次方，缓入。</summary>
        InQuad,
        /// <summary>二次方，缓出。</summary>
        OutQuad,
        /// <summary>二次方，缓入缓出。</summary>
        InOutQuad,

        /// <summary>三次方，缓入。</summary>
        InCubic,
        /// <summary>三次方，缓出。</summary>
        OutCubic,
        /// <summary>三次方，缓入缓出。</summary>
        InOutCubic,

        /// <summary>正弦，缓入。</summary>
        InSine,
        /// <summary>正弦，缓出。</summary>
        OutSine,
        /// <summary>正弦，缓入缓出。</summary>
        InOutSine,

        /// <summary>指数，缓入。</summary>
        InExpo,
        /// <summary>指数，缓出。</summary>
        OutExpo,
        /// <summary>指数，缓入缓出。</summary>
        InOutExpo,

        /// <summary>回弹（过冲后回正），缓出。</summary>
        OutBack,
        /// <summary>弹性（振荡），缓出。</summary>
        OutElastic,
        /// <summary>弹跳，缓出。</summary>
        OutBounce
    }
}
