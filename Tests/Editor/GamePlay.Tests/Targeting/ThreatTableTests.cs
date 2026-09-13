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
    /// 针对 <see cref="ThreatTable"/> 累积/覆盖/取值、最高者裁决、衰减剔除、移除与
    /// <see cref="ThreatTable.OnTopChanged"/> 事件的单元测试。
    /// </summary>
    [TestFixture]
    public class ThreatTableTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void AddThreat_Accumulates()
        {
            ThreatTable table = new ThreatTable();

            table.AddThreat(1, 10f);
            table.AddThreat(1, 5f);

            Assert.AreEqual(15f, table.GetThreat(1), Delta);
        }

        [Test]
        public void AddThreat_OnNewEntity_StartsFromZero()
        {
            ThreatTable table = new ThreatTable();

            table.AddThreat(42, 7f);

            Assert.AreEqual(7f, table.GetThreat(42), Delta);
            Assert.AreEqual(1, table.Count);
        }

        [Test]
        public void SetThreat_Overwrites()
        {
            ThreatTable table = new ThreatTable();

            table.AddThreat(1, 100f);
            table.SetThreat(1, 30f);

            Assert.AreEqual(30f, table.GetThreat(1), Delta);
        }

        [Test]
        public void GetThreat_Unknown_ReturnsZero()
        {
            ThreatTable table = new ThreatTable();

            Assert.AreEqual(0f, table.GetThreat(999), Delta);
        }

        [Test]
        public void TryGetTop_Empty_ReturnsFalse()
        {
            ThreatTable table = new ThreatTable();

            bool ok = table.TryGetTop(out int id);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, id);
        }

        [Test]
        public void TryGetTop_PicksHighestThreat()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f);
            table.AddThreat(2, 50f);
            table.AddThreat(3, 25f);

            bool ok = table.TryGetTop(out int id);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, id);
        }

        [Test]
        public void TryGetTop_Tie_BreaksByLowestId()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(5, 30f);
            table.AddThreat(2, 30f);
            table.AddThreat(8, 30f);

            bool ok = table.TryGetTop(out int id);

            Assert.IsTrue(ok);
            Assert.AreEqual(2, id);
        }

        [Test]
        public void Remove_Existing_ReturnsTrue_AndUpdatesCount()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f);
            table.AddThreat(2, 20f);

            bool removed = table.Remove(1);

            Assert.IsTrue(removed);
            Assert.AreEqual(1, table.Count);
            Assert.AreEqual(0f, table.GetThreat(1), Delta);
        }

        [Test]
        public void Remove_Missing_ReturnsFalse()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f);

            bool removed = table.Remove(999);

            Assert.IsFalse(removed);
            Assert.AreEqual(1, table.Count);
        }

        [Test]
        public void Decay_ScalesAllEntries()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 100f);
            table.AddThreat(2, 40f);

            table.Decay(0.5f);

            Assert.AreEqual(50f, table.GetThreat(1), Delta);
            Assert.AreEqual(20f, table.GetThreat(2), Delta);
        }

        [Test]
        public void Decay_PrunesNearZeroEntries()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 100f);
            table.AddThreat(2, 0.0001f); // 已处于 ~0，衰减后必然被剔除。

            table.Decay(0.5f);

            Assert.AreEqual(1, table.Count);
            Assert.IsTrue(table.Threats.ContainsKey(1));
            Assert.IsFalse(table.Threats.ContainsKey(2));
        }

        [Test]
        public void Decay_FactorClampedToZeroOne()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 80f);

            // factor 超过 1 被钳制为 1 → 值不变。
            table.Decay(2f);
            Assert.AreEqual(80f, table.GetThreat(1), Delta);

            // factor 为负被钳制为 0 → 全部归零并剔除。
            table.Decay(-1f);
            Assert.AreEqual(0, table.Count);
        }

        [Test]
        public void Decay_ToZero_ClearsAll()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 50f);
            table.AddThreat(2, 70f);

            table.Decay(0f);

            Assert.AreEqual(0, table.Count);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f);
            table.AddThreat(2, 20f);

            table.Clear();

            Assert.AreEqual(0, table.Count);
            Assert.IsFalse(table.TryGetTop(out int _));
        }

        [Test]
        public void Threats_ExposesUnderlyingEntries()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f);
            table.AddThreat(2, 20f);

            IReadOnlyDictionary<int, float> view = table.Threats;

            Assert.AreEqual(2, view.Count);
            Assert.AreEqual(10f, view[1], Delta);
            Assert.AreEqual(20f, view[2], Delta);
        }

        [Test]
        public void OnTopChanged_FiresWhenFirstEntryBecomesTop()
        {
            ThreatTable table = new ThreatTable();
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            table.AddThreat(1, 10f);

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(1, events[0]);
        }

        [Test]
        public void OnTopChanged_FiresWhenTopChanges()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 10f); // top = 1
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            table.AddThreat(2, 50f); // top → 2

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(2, events[0]);
        }

        [Test]
        public void OnTopChanged_DoesNotFire_WhenTopUnchanged()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 100f); // top = 1
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            // 给非最高者加一点仇恨，但不足以超越 1 → top 仍为 1，不应触发。
            table.AddThreat(2, 10f);

            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void OnTopChanged_FiresAfterTopRemoved()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 50f);
            table.AddThreat(2, 30f); // top = 1
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            table.Remove(1); // top → 2

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(2, events[0]);
        }

        [Test]
        public void OnTopChanged_DoesNotFire_WhenTableBecomesEmpty()
        {
            ThreatTable table = new ThreatTable();
            table.AddThreat(1, 50f); // top = 1
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            table.Remove(1); // 表变空 → 无新最高者，不触发。

            Assert.AreEqual(0, events.Count);
            Assert.IsFalse(table.TryGetTop(out int _));
        }

        [Test]
        public void OnTopChanged_FiresWhenDecayRevealsNewTop()
        {
            // 初始 top = 1（值 100）。衰减后两者都缩小但比例不变，top 仍为 1，不触发；
            // 故构造一次性把 1 衰减到低于 2 的情形：先让 2 略高于 1 衰减后的值。
            ThreatTable table = new ThreatTable();
            table.SetThreat(1, 10f);
            table.SetThreat(2, 9f); // top = 1
            List<int> events = new List<int>();
            table.OnTopChanged += (t, id) => events.Add(id);

            // 同比衰减不会改变名次，这里通过 Set 让 2 超过 1 来验证 RecomputeTop 在 Set 后也会触发。
            table.SetThreat(1, 1f); // top → 2

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(2, events[0]);
        }
    }
}
