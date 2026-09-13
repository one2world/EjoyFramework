//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 保底计数状态：由调用方按卡池（banner）持久化的可变计数器。
    /// </summary>
    /// <remarks>
    /// <see cref="PullsSincePityTier"/> 记录“距上次命中保底档以来的抽数”。
    /// 每次非保底档命中后 +1，命中保底档后归 0。该对象需由调用方在卡池维度上保存与读取。
    /// </remarks>
    public sealed class PityState
    {
        /// <summary>
        /// 距上次命中保底档以来累计的抽数。
        /// </summary>
        public int PullsSincePityTier { get; set; }
    }
}
