//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Achievements
{
    /// <summary>
    /// 成就管理器接口。游戏侧通过 <see cref="Framework.GetModule{T}"/>（T = <see cref="IAchievementManager"/>）获取，
    /// 用 <see cref="Define"/> 注册成就、用 <see cref="AddProgress"/> / <see cref="SetProgress"/> / <see cref="Unlock"/>
    /// 推进，达成时自动转入 Unlocked 并触发 <see cref="OnUnlocked"/>。纯逻辑、与引擎无关，可独立单测。
    ///
    /// 与 <c>IQuestTracker</c> 的区别：任务按 (事件类型, 目标键) 广播驱动多目标；成就按<b>成就 Id</b> 直接推进单一计数器，
    /// 适合"累计/里程碑"类（击杀数、登录天数、收集进度）。
    /// </summary>
    public interface IAchievementManager
    {
        /// <summary>
        /// 注册一个成就定义，初始 Locked、进度 0。
        /// </summary>
        /// <exception cref="ArgumentNullException">definition 为 null。</exception>
        /// <exception cref="ArgumentException">成就 Id 已存在。</exception>
        AchievementState Define(AchievementDefinition definition);

        /// <summary>尝试获取成就状态。</summary>
        bool TryGet(string achievementId, out AchievementState state);

        /// <summary>获取成就状态，不存在返回 null。</summary>
        AchievementState Get(string achievementId);

        /// <summary>枚举所有已注册成就（按注册顺序）。</summary>
        IEnumerable<AchievementState> Achievements { get; }

        /// <summary>已达成（Unlocked + Claimed）的成就数量。</summary>
        int UnlockedCount { get; }

        /// <summary>已达成成就的点数总和。</summary>
        int TotalPoints { get; }

        /// <summary>
        /// 为指定成就累加进度。达到 TargetCount 时自动转入 Unlocked 并触发 <see cref="OnUnlocked"/>；
        /// 进度变化触发 <see cref="OnProgress"/>。
        /// </summary>
        /// <param name="amount">累加量，&lt;= 0 视为无效。</param>
        /// <returns>累加后的当前进度；成就不存在或已达成（无法再累计）返回 -1。</returns>
        int AddProgress(string achievementId, int amount = 1);

        /// <summary>
        /// 设置指定成就的绝对进度（钳制到 [0, TargetCount]）。达到 TargetCount 时自动 Unlock。
        /// </summary>
        /// <returns>成就存在返回 true。</returns>
        bool SetProgress(string achievementId, int count);

        /// <summary>
        /// 直接达成成就：Locked → Unlocked，进度置为 TargetCount，触发 <see cref="OnUnlocked"/>。
        /// </summary>
        /// <returns>发生状态转移返回 true；不存在或已达成返回 false。</returns>
        bool Unlock(string achievementId);

        /// <summary>领取成就奖励：Unlocked → Claimed，触发 <see cref="OnClaimed"/>。</summary>
        /// <returns>发生状态转移返回 true；不存在或非 Unlocked 返回 false。</returns>
        bool Claim(string achievementId);

        /// <summary>
        /// 从存档恢复某成就的进度与状态（不触发事件）。成就需已 <see cref="Define"/>，否则忽略。
        /// </summary>
        void Restore(string achievementId, int currentCount, AchievementStatus status);

        /// <summary>进度被推进时触发。参数：管理器、成就状态。</summary>
        event Action<AchievementManager, AchievementState> OnProgress;

        /// <summary>成就达成（转入 Unlocked）时触发。</summary>
        event Action<AchievementManager, AchievementState> OnUnlocked;

        /// <summary>成就奖励被领取（转入 Claimed）时触发。</summary>
        event Action<AchievementManager, AchievementState> OnClaimed;
    }
}
