//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Quest
{
    /// <summary>
    /// 任务运行时状态。封装某个 <see cref="QuestDefinition"/> 的当前状态与各目标的进度。
    /// 其状态由 <see cref="QuestTracker"/> 驱动变更；外部仅作只读查询。
    /// </summary>
    public sealed class QuestState
    {
        private readonly QuestDefinition m_Definition;

        // 各目标的当前计数，键为目标 Id。容量在构造时确定，运行期不再扩容。
        private readonly Dictionary<string, int> m_Progress;

        private QuestStatus m_Status;

        /// <summary>
        /// 构造任务状态。初始状态由 <see cref="QuestTracker"/> 在注册时解析设置。
        /// </summary>
        /// <param name="definition">关联的任务定义。</param>
        /// <param name="initialStatus">初始状态。</param>
        internal QuestState(QuestDefinition definition, QuestStatus initialStatus)
        {
            m_Definition = definition;
            m_Status = initialStatus;

            IReadOnlyList<QuestObjective> objectives = definition.Objectives;
            m_Progress = new Dictionary<string, int>(objectives.Count);
            for (int i = 0; i < objectives.Count; i++)
            {
                m_Progress[objectives[i].Id] = 0;
            }
        }

        /// <summary>
        /// 关联的任务定义。
        /// </summary>
        public QuestDefinition Definition
        {
            get { return m_Definition; }
        }

        /// <summary>
        /// 当前任务状态。
        /// </summary>
        public QuestStatus Status
        {
            get { return m_Status; }
            internal set { m_Status = value; }
        }

        /// <summary>
        /// 目标列表（等同于定义中的目标）。
        /// </summary>
        public IReadOnlyList<QuestObjective> Objectives
        {
            get { return m_Definition.Objectives; }
        }

        /// <summary>
        /// 获取指定目标的当前计数。目标不存在时返回 0。
        /// </summary>
        /// <param name="objectiveId">目标 Id。</param>
        /// <returns>当前计数。</returns>
        public int GetProgress(string objectiveId)
        {
            if (objectiveId != null && m_Progress.TryGetValue(objectiveId, out int value))
            {
                return value;
            }

            return 0;
        }

        /// <summary>
        /// 获取指定目标的所需计数。目标不存在时返回 0。
        /// </summary>
        /// <param name="objectiveId">目标 Id。</param>
        /// <returns>所需计数。</returns>
        public int GetRequired(string objectiveId)
        {
            QuestObjective objective = FindObjective(objectiveId);
            return objective != null ? objective.RequiredCount : 0;
        }

        /// <summary>
        /// 判断指定目标是否已完成（当前计数 ≥ 所需计数）。目标不存在时返回 false。
        /// </summary>
        /// <param name="objectiveId">目标 Id。</param>
        /// <returns>已完成返回 true。</returns>
        public bool IsObjectiveComplete(string objectiveId)
        {
            QuestObjective objective = FindObjective(objectiveId);
            if (objective == null)
            {
                return false;
            }

            return GetProgress(objectiveId) >= objective.RequiredCount;
        }

        /// <summary>
        /// 整体完成度（0..1），按各目标的完成比例（封顶 1）求平均。无目标时返回 0。
        /// </summary>
        public float Completion
        {
            get
            {
                IReadOnlyList<QuestObjective> objectives = m_Definition.Objectives;
                if (objectives.Count == 0)
                {
                    return 0f;
                }

                float sum = 0f;
                for (int i = 0; i < objectives.Count; i++)
                {
                    QuestObjective objective = objectives[i];
                    int progress = GetProgress(objective.Id);
                    float ratio = (float)progress / objective.RequiredCount;
                    sum += ratio > 1f ? 1f : ratio;
                }

                return sum / objectives.Count;
            }
        }

        /// <summary>
        /// 判断所有目标是否均已完成。
        /// </summary>
        /// <returns>全部完成返回 true。</returns>
        internal bool AreAllObjectivesComplete()
        {
            IReadOnlyList<QuestObjective> objectives = m_Definition.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                QuestObjective objective = objectives[i];
                if (GetProgress(objective.Id) < objective.RequiredCount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 为指定目标累加进度并在 [0, RequiredCount] 内截断。返回截断后的新计数。
        /// </summary>
        /// <param name="objective">目标。</param>
        /// <param name="amount">累加量。</param>
        /// <returns>截断后的新计数。</returns>
        internal int AdvanceObjective(QuestObjective objective, int amount)
        {
            int current = m_Progress[objective.Id];
            int next = current + amount;
            if (next > objective.RequiredCount)
            {
                next = objective.RequiredCount;
            }

            m_Progress[objective.Id] = next;
            return next;
        }

        private QuestObjective FindObjective(string objectiveId)
        {
            if (objectiveId == null)
            {
                return null;
            }

            IReadOnlyList<QuestObjective> objectives = m_Definition.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (string.Equals(objectives[i].Id, objectiveId, System.StringComparison.Ordinal))
                {
                    return objectives[i];
                }
            }

            return null;
        }
    }
}
