//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Activities
{
    /// <summary>
    /// 限时活动在某一时刻的状态。
    /// </summary>
    public enum ActivityStatus
    {
        /// <summary>
        /// 尚未开始：当前不在开放窗口内，但未来仍有开放窗口。
        /// 对 Daily/Weekly 而言，窗外即为此状态（会报告下一次开始）。
        /// </summary>
        NotStarted,

        /// <summary>
        /// 进行中：当前正处于开放窗口内。
        /// </summary>
        Active,

        /// <summary>
        /// 已结束：未来不再有开放窗口。仅 Once 活动在其结束时间之后进入此状态。
        /// Daily/Weekly 永不进入此状态。
        /// </summary>
        Ended,
    }
}
