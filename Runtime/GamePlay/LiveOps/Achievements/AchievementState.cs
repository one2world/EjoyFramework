//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Achievements
{
    /// <summary>
    /// 成就运行时状态：定义 + 当前进度 + 状态。进度/状态字段仅由 <see cref="AchievementManager"/> 修改
    /// （internal set），外部只读。
    /// </summary>
    public sealed class AchievementState
    {
        /// <summary>成就定义。</summary>
        public AchievementDefinition Definition { get; }

        /// <summary>当前累计进度，范围 [0, Definition.TargetCount]。</summary>
        public int CurrentCount { get; internal set; }

        /// <summary>当前状态。</summary>
        public AchievementStatus Status { get; internal set; }

        /// <summary>
        /// 构造成就状态。
        /// </summary>
        public AchievementState(AchievementDefinition definition, int currentCount, AchievementStatus status)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            CurrentCount = currentCount < 0 ? 0 : currentCount;
            Status = status;
        }

        /// <summary>进度比 [0, 1]。</summary>
        public float Progress
        {
            get
            {
                int target = Definition.TargetCount;
                if (target <= 0)
                {
                    return 1f;
                }
                float p = (float)CurrentCount / target;
                return p < 0f ? 0f : (p > 1f ? 1f : p);
            }
        }

        /// <summary>是否已达成（Unlocked 或 Claimed）。</summary>
        public bool IsUnlocked
        {
            get { return Status == AchievementStatus.Unlocked || Status == AchievementStatus.Claimed; }
        }
    }
}
