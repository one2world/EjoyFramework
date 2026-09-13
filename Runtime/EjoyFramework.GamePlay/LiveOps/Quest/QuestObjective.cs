//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Quest
{
    /// <summary>
    /// 任务目标。匹配给定 <see cref="EventType"/> 与 <see cref="TargetKey"/> 的进度事件。
    /// 当 <see cref="TargetKey"/> 为空（null 或空串）时，匹配该 EventType 下的任意 key。
    /// 不可变值对象，注册后不应再修改。
    /// </summary>
    public sealed class QuestObjective
    {
        private readonly string m_Id;
        private readonly string m_EventType;
        private readonly string m_TargetKey;
        private readonly int m_RequiredCount;

        /// <summary>
        /// 构造任务目标。
        /// </summary>
        /// <param name="id">目标唯一标识（任务内唯一），不可为空。</param>
        /// <param name="eventType">事件类型，如 "kill"、"collect"、"reach"，不可为空。</param>
        /// <param name="targetKey">目标键，如 "goblin"、"gold"、"town"；为空时匹配任意键。</param>
        /// <param name="requiredCount">达成所需数量；小于等于 0 时归一化为 1。</param>
        /// <exception cref="ArgumentException">当 id 或 eventType 为空时抛出。</exception>
        public QuestObjective(string id, string eventType, string targetKey, int requiredCount)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("任务目标的 id 不能为空。", nameof(id));
            }

            if (string.IsNullOrEmpty(eventType))
            {
                throw new ArgumentException("任务目标的 eventType 不能为空。", nameof(eventType));
            }

            m_Id = id;
            m_EventType = eventType;
            // 归一化：空串统一记为 null，便于内部用 IsNullOrEmpty 之外的简单判空。
            m_TargetKey = string.IsNullOrEmpty(targetKey) ? null : targetKey;
            // 归一化：非正数视为 1，避免出现“0 次即完成”的退化目标。
            m_RequiredCount = requiredCount <= 0 ? 1 : requiredCount;
        }

        /// <summary>
        /// 目标唯一标识（任务内唯一）。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 事件类型，如 "kill"、"collect"、"reach"。
        /// </summary>
        public string EventType
        {
            get { return m_EventType; }
        }

        /// <summary>
        /// 目标键；为 null 表示匹配该事件类型下的任意键。
        /// </summary>
        public string TargetKey
        {
            get { return m_TargetKey; }
        }

        /// <summary>
        /// 达成所需数量。
        /// </summary>
        public int RequiredCount
        {
            get { return m_RequiredCount; }
        }

        /// <summary>
        /// 判断本目标是否匹配给定的进度事件。
        /// EventType 必须相等；当本目标 TargetKey 为空时匹配任意键，否则要求精确相等。
        /// </summary>
        /// <param name="eventType">进度事件类型。</param>
        /// <param name="targetKey">进度事件键。</param>
        /// <returns>匹配返回 true。</returns>
        public bool Matches(string eventType, string targetKey)
        {
            if (!string.Equals(m_EventType, eventType, StringComparison.Ordinal))
            {
                return false;
            }

            // TargetKey 为空 => 通配，匹配任意键。
            if (m_TargetKey == null)
            {
                return true;
            }

            return string.Equals(m_TargetKey, targetKey, StringComparison.Ordinal);
        }
    }
}
