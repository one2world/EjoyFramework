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
    /// 任务与成就追踪器。纯逻辑、与引擎无关，可独立单元测试。
    /// 作为 FrameworkModule 接入框架，可通过 <see cref="Framework.GetModule{T}"/>（T = <see cref="IQuestTracker"/>）获取。
    /// 游戏侧通过 <see cref="ReportProgress"/> 推送进度，追踪器据此推进处于 Active 的任务。
    /// 状态流转：Locked → Available → Active → Completed → Claimed。
    /// </summary>
    public sealed class QuestTracker : FrameworkModule, IQuestTracker
    {
        // 已注册任务，键为任务 Id，保持插入顺序由 Quests 枚举返回。
        private readonly Dictionary<string, QuestState> m_Quests;

        // 注册顺序，用于稳定的 Quests 枚举。
        private readonly List<QuestState> m_Order;

        /// <summary>
        /// 构造追踪器。
        /// </summary>
        public QuestTracker()
        {
            m_Quests = new Dictionary<string, QuestState>();
            m_Order = new List<QuestState>();
        }

        /// <summary>
        /// 模块轮询优先级，保持默认 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架轮询。任务追踪为纯事件驱动（依赖 <see cref="ReportProgress"/> 推送），无每帧工作，留空。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭模块：清空所有已注册任务与注册顺序。事件订阅由订阅方负责释放，这里仅清运行时状态。
        /// </summary>
        public override void Shutdown()
        {
            m_Quests.Clear();
            m_Order.Clear();
        }

        /// <summary>
        /// 注册一个任务定义并解析初始状态。
        /// 无前置 => Available；有前置 => Locked。AutoActivate 且 Available => 立即 Active。
        /// 注册过程中若解析为 Available 会触发 <see cref="OnQuestAvailable"/>。
        /// </summary>
        /// <param name="definition">任务定义。</param>
        /// <returns>新建的任务状态。</returns>
        /// <exception cref="ArgumentNullException">definition 为 null。</exception>
        /// <exception cref="ArgumentException">任务 Id 已存在。</exception>
        public QuestState Define(QuestDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (m_Quests.ContainsKey(definition.Id))
            {
                throw new ArgumentException($"任务 Id 已存在：{definition.Id}", nameof(definition));
            }

            // 先以 Locked 建态，再统一走解锁评估，使事件触发逻辑收敛到一处。
            QuestState state = new QuestState(definition, QuestStatus.Locked);
            m_Quests.Add(definition.Id, state);
            m_Order.Add(state);

            if (ArePrerequisitesSatisfied(definition))
            {
                Unlock(state);
            }

            return state;
        }

        /// <summary>
        /// 尝试获取任务状态。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <param name="state">输出的任务状态。</param>
        /// <returns>存在返回 true。</returns>
        public bool TryGet(string questId, out QuestState state)
        {
            if (questId == null)
            {
                state = null;
                return false;
            }

            return m_Quests.TryGetValue(questId, out state);
        }

        /// <summary>
        /// 获取任务状态，不存在返回 null。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>任务状态或 null。</returns>
        public QuestState Get(string questId)
        {
            if (questId != null && m_Quests.TryGetValue(questId, out QuestState state))
            {
                return state;
            }

            return null;
        }

        /// <summary>
        /// 枚举所有已注册任务（按注册顺序）。
        /// </summary>
        public IEnumerable<QuestState> Quests
        {
            get { return m_Order; }
        }

        /// <summary>
        /// 手动接取任务：Available → Active。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>成功接取返回 true；任务不存在或非 Available 返回 false。</returns>
        public bool Activate(string questId)
        {
            QuestState state = Get(questId);
            if (state == null || state.Status != QuestStatus.Available)
            {
                return false;
            }

            state.Status = QuestStatus.Active;
            return true;
        }

        /// <summary>
        /// 推送一次进度事件，推进所有匹配的 Active 任务。
        /// 对每个 Active 任务的每个未完成、且 (eventType, targetKey) 匹配的目标累加 amount（在 RequiredCount 处截断），
        /// 并触发 <see cref="OnObjectiveProgress"/>。当某任务全部目标完成 → Completed，触发 <see cref="OnQuestCompleted"/>。
        /// </summary>
        /// <param name="eventType">事件类型，如 "kill"、"collect"。</param>
        /// <param name="targetKey">目标键，如 "goblin"、"gold"。</param>
        /// <param name="amount">累加量，默认 1；小于等于 0 时视为无效，直接返回 0。</param>
        /// <returns>状态发生变化（进度推进或完成）的任务数量。</returns>
        public int ReportProgress(string eventType, string targetKey, int amount = 1)
        {
            if (string.IsNullOrEmpty(eventType) || amount <= 0)
            {
                return 0;
            }

            int changedCount = 0;

            // 直接遍历注册顺序列表，避免分配迭代器/中间集合。
            for (int i = 0; i < m_Order.Count; i++)
            {
                QuestState state = m_Order[i];
                if (state.Status != QuestStatus.Active)
                {
                    continue;
                }

                bool questChanged = false;
                IReadOnlyList<QuestObjective> objectives = state.Objectives;
                for (int j = 0; j < objectives.Count; j++)
                {
                    QuestObjective objective = objectives[j];

                    // 已完成的目标不再推进。
                    if (state.GetProgress(objective.Id) >= objective.RequiredCount)
                    {
                        continue;
                    }

                    if (!objective.Matches(eventType, targetKey))
                    {
                        continue;
                    }

                    state.AdvanceObjective(objective, amount);
                    questChanged = true;
                    OnObjectiveProgress?.Invoke(this, state, objective);
                }

                if (questChanged)
                {
                    changedCount++;

                    if (state.AreAllObjectivesComplete())
                    {
                        state.Status = QuestStatus.Completed;
                        OnQuestCompleted?.Invoke(this, state);
                    }
                }
            }

            return changedCount;
        }

        /// <summary>
        /// 领取奖励：Completed → Claimed，触发 <see cref="OnQuestClaimed"/>。
        /// 领取后重新评估所有 Locked 任务，前置已全部 Claimed 者解锁（可级联）。
        /// </summary>
        /// <param name="questId">任务 Id。</param>
        /// <returns>成功领取返回 true；任务不存在或非 Completed 返回 false。</returns>
        public bool Claim(string questId)
        {
            QuestState state = Get(questId);
            if (state == null || state.Status != QuestStatus.Completed)
            {
                return false;
            }

            state.Status = QuestStatus.Claimed;
            OnQuestClaimed?.Invoke(this, state);

            // 领取可能满足其他任务的前置条件，级联解锁。
            ReevaluateLockedQuests();

            return true;
        }

        /// <summary>
        /// 判断给定定义的前置任务是否全部已 Claimed。无前置视为满足。
        /// </summary>
        private bool ArePrerequisitesSatisfied(QuestDefinition definition)
        {
            IReadOnlyList<string> prerequisites = definition.PrerequisiteQuestIds;
            for (int i = 0; i < prerequisites.Count; i++)
            {
                QuestState prereq = Get(prerequisites[i]);
                // 前置不存在或尚未 Claimed 都视为未满足。
                if (prereq == null || prereq.Status != QuestStatus.Claimed)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 将一个 Locked/初始任务解锁为 Available（触发 OnQuestAvailable），
        /// 若定义为 AutoActivate 则立即转为 Active。
        /// </summary>
        private void Unlock(QuestState state)
        {
            state.Status = QuestStatus.Available;
            OnQuestAvailable?.Invoke(this, state);

            if (state.Definition.AutoActivate)
            {
                state.Status = QuestStatus.Active;
            }
        }

        /// <summary>
        /// 扫描所有 Locked 任务，对前置已全部 Claimed 者解锁。级联进行，直至无新解锁。
        /// </summary>
        private void ReevaluateLockedQuests()
        {
            bool unlockedAny;
            do
            {
                unlockedAny = false;
                for (int i = 0; i < m_Order.Count; i++)
                {
                    QuestState state = m_Order[i];
                    if (state.Status != QuestStatus.Locked)
                    {
                        continue;
                    }

                    if (ArePrerequisitesSatisfied(state.Definition))
                    {
                        Unlock(state);
                        unlockedAny = true;
                    }
                }
            }
            while (unlockedAny);
        }

        /// <summary>
        /// 某目标进度被推进时触发。参数：追踪器、任务状态、被推进的目标。
        /// </summary>
        public event Action<QuestTracker, QuestState, QuestObjective> OnObjectiveProgress;

        /// <summary>
        /// 任务全部目标完成（转入 Completed）时触发。
        /// </summary>
        public event Action<QuestTracker, QuestState> OnQuestCompleted;

        /// <summary>
        /// 任务奖励被领取（转入 Claimed）时触发。
        /// </summary>
        public event Action<QuestTracker, QuestState> OnQuestClaimed;

        /// <summary>
        /// 任务前置满足、解锁为 Available 时触发。
        /// </summary>
        public event Action<QuestTracker, QuestState> OnQuestAvailable;
    }
}
