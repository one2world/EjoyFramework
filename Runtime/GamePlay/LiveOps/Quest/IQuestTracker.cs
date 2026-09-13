//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Quest
{
    /// <summary>
    /// 任务与成就追踪器的抽象接口。游戏侧通过 <see cref="Framework.GetModule{T}"/> 获取本模块，
    /// 调用 <see cref="ReportProgress"/> 推送进度，追踪器据此推进处于 Active 的任务。
    /// 状态流转：Locked → Available → Active → Completed → Claimed。
    /// </summary>
    public interface IQuestTracker
    {
        /// <summary>
        /// 注册一个任务定义并解析初始状态。
        /// 无前置 => Available；有前置 => Locked。AutoActivate 且 Available => 立即 Active。
        /// 注册过程中若解析为 Available 会触发 <see cref="OnQuestAvailable"/>。
        /// </summary>
        /// <param name="definition">任务定义。</param>
        /// <returns>新建的任务状态。</returns>
        /// <exception cref="ArgumentNullException">definition 为 null。</exception>
        /// <exception cref="ArgumentException">任务 Id 已存在。</exception>
        QuestState Define(QuestDefinition definition);

        /// <summary>
        /// 尝试获取任务状态。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <param name="state">输出的任务状态。</param>
        /// <returns>存在返回 true。</returns>
        bool TryGet(string questId, out QuestState state);

        /// <summary>
        /// 获取任务状态，不存在返回 null。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>任务状态或 null。</returns>
        QuestState Get(string questId);

        /// <summary>
        /// 枚举所有已注册任务（按注册顺序）。
        /// </summary>
        IEnumerable<QuestState> Quests { get; }

        /// <summary>
        /// 手动接取任务：Available → Active。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>成功接取返回 true；任务不存在或非 Available 返回 false。</returns>
        bool Activate(string questId);

        /// <summary>
        /// 推送一次进度事件，推进所有匹配的 Active 任务。
        /// 对每个 Active 任务的每个未完成、且 (eventType, targetKey) 匹配的目标累加 amount（在 RequiredCount 处截断），
        /// 并触发 <see cref="OnObjectiveProgress"/>。当某任务全部目标完成 → Completed，触发 <see cref="OnQuestCompleted"/>。
        /// </summary>
        /// <param name="eventType">事件类型，如 "kill"、"collect"。</param>
        /// <param name="targetKey">目标键，如 "goblin"、"gold"。</param>
        /// <param name="amount">累加量，默认 1；小于等于 0 时视为无效，直接返回 0。</param>
        /// <returns>状态发生变化（进度推进或完成）的任务数量。</returns>
        int ReportProgress(string eventType, string targetKey, int amount = 1);

        /// <summary>
        /// 领取奖励：Completed → Claimed，触发 <see cref="OnQuestClaimed"/>。
        /// 领取后重新评估所有 Locked 任务，前置已全部 Claimed 者解锁（可级联）。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>成功领取返回 true；任务不存在或非 Completed 返回 false。</returns>
        bool Claim(string questId);

        /// <summary>
        /// 某目标进度被推进时触发。参数：追踪器、任务状态、被推进的目标。
        /// </summary>
        event Action<QuestTracker, QuestState, QuestObjective> OnObjectiveProgress;

        /// <summary>
        /// 任务全部目标完成（转入 Completed）时触发。
        /// </summary>
        event Action<QuestTracker, QuestState> OnQuestCompleted;

        /// <summary>
        /// 任务奖励被领取（转入 Claimed）时触发。
        /// </summary>
        event Action<QuestTracker, QuestState> OnQuestClaimed;

        /// <summary>
        /// 任务前置满足、解锁为 Available 时触发。
        /// </summary>
        event Action<QuestTracker, QuestState> OnQuestAvailable;
    }
}
