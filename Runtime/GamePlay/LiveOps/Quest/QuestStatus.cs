//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Quest
{
    /// <summary>
    /// 任务（含成就）的生命周期状态。
    /// 流转：Locked → Available → Active → Completed → Claimed。
    /// </summary>
    public enum QuestStatus
    {
        /// <summary>
        /// 已锁定：前置任务尚未全部满足，不可接取。
        /// </summary>
        Locked,

        /// <summary>
        /// 可接取：前置条件已满足，等待玩家接取（或被自动激活）。
        /// </summary>
        Available,

        /// <summary>
        /// 进行中：已接取，正在追踪进度。
        /// </summary>
        Active,

        /// <summary>
        /// 已完成：全部目标达成，等待领取奖励。
        /// </summary>
        Completed,

        /// <summary>
        /// 已领取：奖励已发放，任务终结。
        /// </summary>
        Claimed,
    }
}
