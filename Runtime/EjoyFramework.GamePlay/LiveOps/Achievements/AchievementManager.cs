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
    /// 成就管理器。纯逻辑、与引擎无关，可独立单元测试。作为 FrameworkModule 接入框架，
    /// 通过 <see cref="Framework.GetModule{T}"/>（T = <see cref="IAchievementManager"/>）获取。
    /// 状态流转：Locked → Unlocked → Claimed。
    /// </summary>
    public sealed class AchievementManager : FrameworkModule, IAchievementManager
    {
        private readonly Dictionary<string, AchievementState> m_Achievements;
        private readonly List<AchievementState> m_Order;

        /// <summary>构造管理器。</summary>
        public AchievementManager()
        {
            m_Achievements = new Dictionary<string, AchievementState>();
            m_Order = new List<AchievementState>();
        }

        /// <summary>模块轮询优先级，保持默认 0。</summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>成就为事件驱动（依赖进度推送），无每帧工作，留空。</summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>关闭模块：清空运行时状态。事件订阅由订阅方负责释放。</summary>
        public override void Shutdown()
        {
            m_Achievements.Clear();
            m_Order.Clear();
        }

        /// <inheritdoc />
        public AchievementState Define(AchievementDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            if (m_Achievements.ContainsKey(definition.Id))
            {
                throw new ArgumentException($"成就 Id 已存在：{definition.Id}", nameof(definition));
            }

            AchievementState state = new AchievementState(definition, 0, AchievementStatus.Locked);
            m_Achievements.Add(definition.Id, state);
            m_Order.Add(state);
            return state;
        }

        /// <inheritdoc />
        public bool TryGet(string achievementId, out AchievementState state)
        {
            if (achievementId == null)
            {
                state = null;
                return false;
            }
            return m_Achievements.TryGetValue(achievementId, out state);
        }

        /// <inheritdoc />
        public AchievementState Get(string achievementId)
        {
            if (achievementId != null && m_Achievements.TryGetValue(achievementId, out AchievementState state))
            {
                return state;
            }
            return null;
        }

        /// <inheritdoc />
        public IEnumerable<AchievementState> Achievements
        {
            get { return m_Order; }
        }

        /// <inheritdoc />
        public int UnlockedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < m_Order.Count; i++)
                {
                    if (m_Order[i].IsUnlocked) n++;
                }
                return n;
            }
        }

        /// <inheritdoc />
        public int TotalPoints
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < m_Order.Count; i++)
                {
                    if (m_Order[i].IsUnlocked) sum += m_Order[i].Definition.Points;
                }
                return sum;
            }
        }

        /// <inheritdoc />
        public int AddProgress(string achievementId, int amount = 1)
        {
            AchievementState state = Get(achievementId);
            // 不存在，或已达成（无法继续累计）。
            if (state == null || state.Status != AchievementStatus.Locked)
            {
                return -1;
            }
            if (amount <= 0)
            {
                return state.CurrentCount;
            }

            int target = state.Definition.TargetCount;
            int next = state.CurrentCount + amount;
            state.CurrentCount = next > target ? target : next;
            OnProgress?.Invoke(this, state);

            if (state.CurrentCount >= target)
            {
                UnlockInternal(state);
            }
            return state.CurrentCount;
        }

        /// <inheritdoc />
        public bool SetProgress(string achievementId, int count)
        {
            AchievementState state = Get(achievementId);
            if (state == null)
            {
                return false;
            }
            // 已达成的成就不再回退/改动进度。
            if (state.Status != AchievementStatus.Locked)
            {
                return true;
            }

            int target = state.Definition.TargetCount;
            int clamped = count < 0 ? 0 : (count > target ? target : count);
            if (clamped != state.CurrentCount)
            {
                state.CurrentCount = clamped;
                OnProgress?.Invoke(this, state);
            }

            if (state.CurrentCount >= target)
            {
                UnlockInternal(state);
            }
            return true;
        }

        /// <inheritdoc />
        public bool Unlock(string achievementId)
        {
            AchievementState state = Get(achievementId);
            if (state == null || state.Status != AchievementStatus.Locked)
            {
                return false;
            }
            state.CurrentCount = state.Definition.TargetCount;
            UnlockInternal(state);
            return true;
        }

        /// <inheritdoc />
        public bool Claim(string achievementId)
        {
            AchievementState state = Get(achievementId);
            if (state == null || state.Status != AchievementStatus.Unlocked)
            {
                return false;
            }
            state.Status = AchievementStatus.Claimed;
            OnClaimed?.Invoke(this, state);
            return true;
        }

        /// <inheritdoc />
        public void Restore(string achievementId, int currentCount, AchievementStatus status)
        {
            AchievementState state = Get(achievementId);
            if (state == null)
            {
                return;
            }
            int target = state.Definition.TargetCount;
            state.CurrentCount = currentCount < 0 ? 0 : (currentCount > target ? target : currentCount);
            state.Status = status;
        }

        // Locked → Unlocked 的统一转移点，触发 OnUnlocked。
        private void UnlockInternal(AchievementState state)
        {
            if (state.Status != AchievementStatus.Locked)
            {
                return;
            }
            state.Status = AchievementStatus.Unlocked;
            OnUnlocked?.Invoke(this, state);
        }

        /// <inheritdoc />
        public event Action<AchievementManager, AchievementState> OnProgress;

        /// <inheritdoc />
        public event Action<AchievementManager, AchievementState> OnUnlocked;

        /// <inheritdoc />
        public event Action<AchievementManager, AchievementState> OnClaimed;
    }
}
