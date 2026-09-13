//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Targeting;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Targeting
{
    /// <summary>
    /// 针对 <see cref="TargetSelector"/> 各选择策略、射程过滤与平局裁决的单元测试。
    /// </summary>
    [TestFixture]
    public class TargetSelectorTests
    {
        private const float Delta = 1e-4f;

        /// <summary>
        /// 构造候选目标的便捷方法。未显式给出的字段使用合理默认值。
        /// </summary>
        private static TargetInfo Make(
            int id,
            float x = 0f,
            float y = 0f,
            float health = 100f,
            float maxHealth = 100f,
            float threat = 0f,
            int priority = 0,
            float progress = 0f)
        {
            return new TargetInfo(id, x, y, health, maxHealth, threat, priority, progress);
        }

        [Test]
        public void Distance_And_SqrDistance_AreConsistent()
        {
            Assert.AreEqual(25f, TargetSelector.SqrDistance(0f, 0f, 3f, 4f), Delta);
            Assert.AreEqual(5f, TargetSelector.Distance(0f, 0f, 3f, 4f), Delta);
        }

        [Test]
        public void TrySelect_EmptyList_ReturnsFalse()
        {
            List<TargetInfo> candidates = new List<TargetInfo>();

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 0f, out TargetInfo best);

            Assert.IsFalse(ok);
            Assert.AreEqual(default(TargetInfo).Id, best.Id);
        }

        [Test]
        public void TrySelect_NullList_ReturnsFalse()
        {
            bool ok = TargetSelector.TrySelect(null, 0f, 0f, TargetingStrategy.Nearest, 0f, out TargetInfo best);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, best.Id);
        }

        [Test]
        public void TrySelect_Nearest_PicksClosest()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 10f, y: 0f),
                Make(2, x: 3f, y: 0f),
                Make(3, x: 7f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, best.Id);
        }

        [Test]
        public void TrySelect_Farthest_PicksFurthest()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 10f, y: 0f),
                Make(2, x: 3f, y: 0f),
                Make(3, x: 7f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Farthest, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_LowestHealth_PicksMinHealth()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, health: 80f),
                Make(2, health: 20f),
                Make(3, health: 50f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.LowestHealth, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, best.Id);
        }

        [Test]
        public void TrySelect_HighestHealth_PicksMaxHealth()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, health: 80f),
                Make(2, health: 20f),
                Make(3, health: 50f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.HighestHealth, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_LowestHealthPercent_UsesRatioNotAbsolute()
        {
            // 实体 1：50/200 = 0.25；实体 2：30/100 = 0.30。
            // 绝对血量实体 2 更低，但百分比实体 1 更低 → 应选实体 1。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, health: 50f, maxHealth: 200f),
                Make(2, health: 30f, maxHealth: 100f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.LowestHealthPercent, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_HighestThreat_PicksMaxThreat()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, threat: 5f),
                Make(2, threat: 42f),
                Make(3, threat: 17f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.HighestThreat, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, best.Id);
        }

        [Test]
        public void TrySelect_HighestPriority_PicksMaxPriority()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, priority: 1),
                Make(2, priority: 9),
                Make(3, priority: 3),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.HighestPriority, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, best.Id);
        }

        [Test]
        public void TrySelect_FirstInProgress_PicksMaxProgress()
        {
            // 塔防约定：进度最大 = 最接近终点 = 队首。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, progress: 0.2f),
                Make(2, progress: 0.9f),
                Make(3, progress: 0.5f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.FirstInProgress, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, best.Id);
        }

        [Test]
        public void TrySelect_LastInProgress_PicksMinProgress()
        {
            // 塔防约定：进度最小 = 刚出生 = 队尾。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, progress: 0.2f),
                Make(2, progress: 0.9f),
                Make(3, progress: 0.5f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.LastInProgress, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_MaxRange_FiltersOutOfRange()
        {
            // 来源在原点，射程 5。实体 1 在距离 3（命中），实体 2 在距离 10（超出）。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 3f, y: 0f),
                Make(2, x: 10f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 5f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_AllOutOfRange_ReturnsFalse()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 20f, y: 0f),
                Make(2, x: 30f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 5f, out TargetInfo best);

            Assert.IsFalse(ok);
        }

        [Test]
        public void TrySelect_RangeAtExactBoundary_IncludesTarget()
        {
            // 距离恰为 5（= maxRange）：使用 SqrDistance 比较 25 <= 25，应被包含。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 5f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 5f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_NonPositiveRange_MeansUnlimited()
        {
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(1, x: 1000f, y: 1000f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }

        [Test]
        public void TrySelect_Tie_BreaksByLowestId()
        {
            // 三个候选血量相同，应确定性地选取 Id 最小者。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(7, health: 50f),
                Make(3, health: 50f),
                Make(5, health: 50f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.LowestHealth, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(3, best.Id);
        }

        [Test]
        public void TrySelect_Tie_LowestIdWins_RegardlessOfOrder()
        {
            // 即便较小 Id 出现在列表后部，平局裁决依然取较小 Id。
            List<TargetInfo> candidates = new List<TargetInfo>
            {
                Make(2, x: 4f, y: 0f),
                Make(1, x: 4f, y: 0f),
            };

            bool ok = TargetSelector.TrySelect(candidates, 0f, 0f, TargetingStrategy.Nearest, 0f, out TargetInfo best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);
        }
    }
}
