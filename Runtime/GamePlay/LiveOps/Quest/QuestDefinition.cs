//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Quest
{
    /// <summary>
    /// 任务（含成就）定义（模板）。描述目标、前置任务、自动激活与奖励等静态数据。
    /// 不可变值对象，注册后不应再修改。
    /// </summary>
    public sealed class QuestDefinition
    {
        private readonly string m_Id;
        private readonly IReadOnlyList<QuestObjective> m_Objectives;
        private readonly IReadOnlyList<string> m_PrerequisiteQuestIds;
        private readonly bool m_AutoActivate;
        private readonly bool m_IsAchievement;
        private readonly object m_Reward;

        /// <summary>
        /// 构造任务定义。
        /// </summary>
        /// <param name="id">任务唯一标识，不可为空。</param>
        /// <param name="objectives">目标列表，至少包含一个目标。</param>
        /// <param name="prerequisiteQuestIds">前置任务 Id 列表；这些任务全部 Claimed 后本任务方可 Available。</param>
        /// <param name="autoActivate">Available 时是否立即自动激活为 Active。</param>
        /// <param name="isAchievement">是否为成就（风味标记；成就通常自动激活并全程追踪）。</param>
        /// <param name="reward">不透明的游戏侧奖励数据，领取时原样回传。</param>
        /// <exception cref="ArgumentException">当 id 为空，或目标列表为空时抛出。</exception>
        public QuestDefinition(
            string id,
            IReadOnlyList<QuestObjective> objectives,
            IReadOnlyList<string> prerequisiteQuestIds = null,
            bool autoActivate = false,
            bool isAchievement = false,
            object reward = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("任务定义的 id 不能为空。", nameof(id));
            }

            if (objectives == null || objectives.Count == 0)
            {
                throw new ArgumentException("任务定义至少需要一个目标。", nameof(objectives));
            }

            m_Id = id;
            m_Objectives = objectives;
            m_PrerequisiteQuestIds = prerequisiteQuestIds ?? Array.Empty<string>();
            m_AutoActivate = autoActivate;
            m_IsAchievement = isAchievement;
            m_Reward = reward;
        }

        /// <summary>
        /// 任务唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 目标列表。
        /// </summary>
        public IReadOnlyList<QuestObjective> Objectives
        {
            get { return m_Objectives; }
        }

        /// <summary>
        /// 前置任务 Id 列表；这些任务全部 Claimed 后本任务方可 Available。
        /// </summary>
        public IReadOnlyList<string> PrerequisiteQuestIds
        {
            get { return m_PrerequisiteQuestIds; }
        }

        /// <summary>
        /// Available 时是否立即自动激活为 Active（成就与主线常用）。
        /// </summary>
        public bool AutoActivate
        {
            get { return m_AutoActivate; }
        }

        /// <summary>
        /// 是否为成就（风味标记；成就通常自动激活并全程追踪）。
        /// </summary>
        public bool IsAchievement
        {
            get { return m_IsAchievement; }
        }

        /// <summary>
        /// 不透明的游戏侧奖励数据，领取时原样回传。
        /// </summary>
        public object Reward
        {
            get { return m_Reward; }
        }

        /// <summary>
        /// 创建一个任务定义构建器。
        /// </summary>
        /// <param name="id">任务唯一标识。</param>
        /// <returns>构建器实例。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// 任务定义的链式构建器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly List<QuestObjective> m_Objectives = new List<QuestObjective>();
            private readonly List<string> m_Prerequisites = new List<string>();
            private bool m_AutoActivate;
            private bool m_IsAchievement;
            private object m_Reward;

            /// <summary>
            /// 构造构建器。
            /// </summary>
            /// <param name="id">任务唯一标识。</param>
            public Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 添加一个目标。
            /// </summary>
            /// <param name="objective">目标实例。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddObjective(QuestObjective objective)
            {
                if (objective != null)
                {
                    m_Objectives.Add(objective);
                }

                return this;
            }

            /// <summary>
            /// 以参数形式添加一个目标。
            /// </summary>
            /// <param name="id">目标唯一标识。</param>
            /// <param name="eventType">事件类型。</param>
            /// <param name="targetKey">目标键；为空时匹配任意键。</param>
            /// <param name="requiredCount">达成所需数量。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddObjective(string id, string eventType, string targetKey, int requiredCount)
            {
                m_Objectives.Add(new QuestObjective(id, eventType, targetKey, requiredCount));
                return this;
            }

            /// <summary>
            /// 添加一个前置任务。
            /// </summary>
            /// <param name="questId">前置任务 Id。</param>
            /// <returns>构建器自身。</returns>
            public Builder RequirePrerequisite(string questId)
            {
                if (!string.IsNullOrEmpty(questId))
                {
                    m_Prerequisites.Add(questId);
                }

                return this;
            }

            /// <summary>
            /// 标记 Available 时自动激活为 Active。
            /// </summary>
            /// <returns>构建器自身。</returns>
            public Builder AutoActivate()
            {
                m_AutoActivate = true;
                return this;
            }

            /// <summary>
            /// 标记本任务为成就（同时启用自动激活）。
            /// </summary>
            /// <returns>构建器自身。</returns>
            public Builder AsAchievement()
            {
                m_IsAchievement = true;
                m_AutoActivate = true;
                return this;
            }

            /// <summary>
            /// 设置奖励数据。
            /// </summary>
            /// <param name="reward">不透明的游戏侧奖励数据。</param>
            /// <returns>构建器自身。</returns>
            public Builder WithReward(object reward)
            {
                m_Reward = reward;
                return this;
            }

            /// <summary>
            /// 构建任务定义。
            /// </summary>
            /// <returns>不可变的任务定义实例。</returns>
            public QuestDefinition Build()
            {
                return new QuestDefinition(
                    m_Id,
                    m_Objectives.ToArray(),
                    m_Prerequisites.ToArray(),
                    m_AutoActivate,
                    m_IsAchievement,
                    m_Reward);
            }
        }
    }
}
