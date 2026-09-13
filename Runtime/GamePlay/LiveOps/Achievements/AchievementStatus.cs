//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Achievements
{
    /// <summary>
    /// 成就状态流转：Locked(进行中/未达成) → Unlocked(已达成未领取) → Claimed(已领取奖励)。
    /// 无奖励的成就可只用到 Unlocked。
    /// </summary>
    public enum AchievementStatus
    {
        /// <summary>未达成（进度累计中）。</summary>
        Locked = 0,

        /// <summary>已达成，奖励未领取。</summary>
        Unlocked = 1,

        /// <summary>已达成且奖励已领取。</summary>
        Claimed = 2,
    }
}
