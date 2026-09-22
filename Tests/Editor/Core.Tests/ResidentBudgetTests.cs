//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests
{
    /// <summary>WS2-M2：常驻内存预算策略——温缓存、LRU 淘汰、预算未启用 no-op、主动收缩、压力告警、零分配。</summary>
    public sealed class ResidentBudgetTests
    {
        private ResidentBudget m_Budget;
        private readonly List<string> m_Out = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Budget = new ResidentBudget();
        }

        [Test]
        public void Disabled_TracksBytes_ButNeverEvicts()
        {
            m_Budget.OnLoaded("a", 100, true);
            m_Budget.OnUnreferenced("a");
            Assert.AreEqual(100, m_Budget.ResidentBytes);
            Assert.AreEqual(100, m_Budget.WarmBytes);
            Assert.AreEqual(0, m_Budget.SelectEvictions(m_Out));
            Assert.IsFalse(m_Budget.IsEnabled);
        }

        [Test]
        public void Evicts_LeastRecentlyUsed_WarmEntriesOnly()
        {
            m_Budget.BudgetBytes = 250;
            m_Budget.OnLoaded("old", 100, true);
            m_Budget.OnLoaded("mid", 100, true);
            m_Budget.OnLoaded("hot", 100, true);     // 300 > 250，但全部在引用中
            Assert.AreEqual(0, m_Budget.SelectEvictions(m_Out), "引用中的单元不可淘汰。");

            m_Budget.OnUnreferenced("old");
            m_Budget.OnUnreferenced("mid");         // old 更久未用
            Assert.AreEqual(1, m_Budget.SelectEvictions(m_Out), "只需淘汰一个即可回到预算内。");
            CollectionAssert.AreEqual(new[] { "old" }, m_Out);

            m_Budget.OnUnloaded("old");
            Assert.AreEqual(200, m_Budget.ResidentBytes);
            Assert.AreEqual(100, m_Budget.WarmBytes);
            Assert.AreEqual(1, m_Budget.EvictionCount);
        }

        [Test]
        public void WarmHit_ReReference_RemovesFromEvictionCandidates()
        {
            m_Budget.BudgetBytes = 150;
            m_Budget.OnLoaded("a", 100, true);
            m_Budget.OnLoaded("b", 100, true);
            m_Budget.OnUnreferenced("a");
            m_Budget.OnReferenced("a");             // 温缓存命中
            m_Budget.OnUnreferenced("b");

            Assert.AreEqual(1, m_Budget.WarmHitCount);
            m_Budget.SelectEvictions(m_Out);
            CollectionAssert.AreEqual(new[] { "b" }, m_Out, "重新引用的 a 不可淘汰，只能淘汰 b。");
        }

        [Test]
        public void TargetBytes_TrimsBelowBudget()
        {
            m_Budget.BudgetBytes = 1000;
            for (int i = 0; i < 5; i++) { m_Budget.OnLoaded("b" + i, 100, true); m_Budget.OnUnreferenced("b" + i); }
            Assert.AreEqual(0, m_Budget.SelectEvictions(m_Out), "500 < 1000 预算内，不淘汰。");
            Assert.AreEqual(3, m_Budget.SelectEvictions(m_Out, 200), "主动收缩到 200：淘汰 3 个最久未用。");
            CollectionAssert.AreEqual(new[] { "b0", "b1", "b2" }, m_Out);
        }

        [Test]
        public void ReloadSameKey_ReplacesSize()
        {
            m_Budget.BudgetBytes = 1000;
            m_Budget.OnLoaded("a", 100, true);
            m_Budget.OnLoaded("a", 300, true);
            Assert.AreEqual(300, m_Budget.ResidentBytes);
            Assert.AreEqual(1, m_Budget.Count);
        }

        [Test]
        public void Pressure_WarnsOnce_WhenNothingEvictable()
        {
            m_Budget.BudgetBytes = 100;
            m_Budget.OnLoaded("a", 200, true);
            Assert.AreEqual(0, m_Budget.SelectEvictions(m_Out));
            Assert.IsTrue(m_Budget.IsUnderPressure);
            Assert.AreEqual(1, m_Budget.PressureWarningCount);
            Assert.AreEqual(0, m_Budget.SelectEvictions(m_Out));
            Assert.AreEqual(1, m_Budget.PressureWarningCount, "压力状态持续期间只告警一次。");

            m_Budget.OnUnreferenced("a");
            Assert.AreEqual(1, m_Budget.SelectEvictions(m_Out), "有了可淘汰单元即恢复。");
            m_Budget.OnUnloaded("a");
            m_Budget.OnLoaded("b", 200, true);
            m_Budget.SelectEvictions(m_Out);
            Assert.AreEqual(2, m_Budget.PressureWarningCount, "再次进入压力状态重新告警。");
        }

        [Test]
        public void Clear_ResetsEverything()
        {
            m_Budget.BudgetBytes = 100;
            m_Budget.OnLoaded("a", 50, true);
            m_Budget.Clear();
            Assert.AreEqual(0, m_Budget.Count);
            Assert.AreEqual(0, m_Budget.ResidentBytes);
            Assert.AreEqual(0, m_Budget.WarmBytes);
        }

        [Test]
        public void ReportAndSelect_SteadyState_DoesNotAllocate()
        {
            m_Budget.BudgetBytes = 300;
            string[] keys = { "k0", "k1", "k2", "k3", "k4", "k5" };
            TestDelegate body = () =>
            {
                for (int i = 0; i < keys.Length; i++) m_Budget.OnLoaded(keys[i], 100, true);
                for (int i = 0; i < keys.Length; i++) m_Budget.OnUnreferenced(keys[i]);
                m_Budget.SelectEvictions(m_Out);
                for (int i = 0; i < m_Out.Count; i++) m_Budget.OnUnloaded(m_Out[i]);
                for (int i = 0; i < keys.Length; i++) m_Budget.OnUnloaded(keys[i]);
            };
            body();
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
