//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 循环方式。
    /// </summary>
    public enum LoopType
    {
        /// <summary>每次循环从起点重新开始（from→to，from→to……）。</summary>
        Restart,

        /// <summary>往返：每次循环反转方向（from→to，to→from，from→to……）。</summary>
        Yoyo
    }
}
