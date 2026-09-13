//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Loot;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Loot
{
    /// <summary>
    /// 针对 <see cref="LootTable"/> 加权抽取、数量区间与无放回去重行为的单元测试。
    /// </summary>
    [TestFixture]
    public class LootTableTests
    {
        [Test]
        public void TotalWeight_SumsAllEntryWeights()
        {
            LootTable table = new LootTable()
                .Add("a", 30f)
                .Add("b", 70f);

            Assert.AreEqual(100f, table.TotalWeight, 1e-4f);
            Assert.AreEqual(2, table.Entries.Count);
        }

        [Test]
        public void RollOnce_JustBelowBoundary_PicksFirstEntry()
        {
            // TotalWeight=100，A 占 [0,30)。NextDouble=0.29 → point=29 < 30 → 命中 A。
            LootTable table = new LootTable()
                .Add("a", 30f)
                .Add("b", 70f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.29d });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual("a", drop.ItemId);
        }

        [Test]
        public void RollOnce_AtBoundary_PicksSecondEntry()
        {
            // NextDouble=0.30 → point=30，不满足 point<30 → 落入 B 段。
            LootTable table = new LootTable()
                .Add("a", 30f)
                .Add("b", 70f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.30d });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual("b", drop.ItemId);
        }

        [Test]
        public void RollOnce_PointAtTopOfRange_FallsBackToLastEntry()
        {
            // NextDouble 取最大可能值附近 → point 接近 TotalWeight → 浮点兜底归到最后一项。
            LootTable table = new LootTable()
                .Add("a", 30f)
                .Add("b", 70f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.9999999d });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual("b", drop.ItemId);
        }

        [Test]
        public void RollOnce_MinEqualsMax_GivesFixedCount()
        {
            LootTable table = new LootTable()
                .Add("gold", 1f, 5, 5);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual("gold", drop.ItemId);
            Assert.AreEqual(5, drop.Count);
        }

        [Test]
        public void RollOnce_MinLessThanMax_UsesNextIntOffset()
        {
            // Min=2,Max=5 → span=4，NextInt 返回 3 → 数量 = 2 + 3 = 5。
            LootTable table = new LootTable()
                .Add("gem", 1f, 2, 5);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d }, new[] { 3 });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual(5, drop.Count);
        }

        [Test]
        public void RollOnce_MinLessThanMax_LowerBoundWhenNextIntZero()
        {
            LootTable table = new LootTable()
                .Add("gem", 1f, 2, 5);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d }, new[] { 0 });

            LootDrop drop = table.RollOnce(rng);

            Assert.AreEqual(2, drop.Count);
        }

        [Test]
        public void Roll_WithReplacement_CanRepeatSameEntry()
        {
            // 两次都把 point 打到 A 段 → 允许重复。
            LootTable table = new LootTable()
                .Add("a", 50f)
                .Add("b", 50f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.10d, 0.20d });

            IList<LootDrop> drops = table.Roll(2, rng, withReplacement: true);

            Assert.AreEqual(2, drops.Count);
            Assert.AreEqual("a", drops[0].ItemId);
            Assert.AreEqual("a", drops[1].ItemId);
        }

        [Test]
        public void Roll_WithoutReplacement_NoDuplicateItemIds()
        {
            LootTable table = new LootTable()
                .Add("a", 50f)
                .Add("b", 30f)
                .Add("c", 20f);
            // 第一次命中 A（移除后剩 B+C，权重 50）；第二次 point 打到 B 段。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.10d, 0.10d, 0.10d });

            IList<LootDrop> drops = table.Roll(3, rng, withReplacement: false);

            Assert.AreEqual(3, drops.Count);
            HashSet<string> seen = new HashSet<string>();
            foreach (LootDrop d in drops)
            {
                Assert.IsTrue(seen.Add(d.ItemId), "无放回抽取出现了重复的 ItemId: " + d.ItemId);
            }
        }

        [Test]
        public void Roll_WithoutReplacement_ClampsToEntryCount()
        {
            LootTable table = new LootTable()
                .Add("a", 50f)
                .Add("b", 50f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.10d, 0.10d, 0.10d, 0.10d, 0.10d });

            // 请求 5 次但只有 2 个条目 → 裁剪为 2。
            IList<LootDrop> drops = table.Roll(5, rng, withReplacement: false);

            Assert.AreEqual(2, drops.Count);
        }

        [Test]
        public void Roll_ZeroOrNegativeCount_ReturnsEmpty()
        {
            LootTable table = new LootTable().Add("a", 1f);
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d });

            Assert.AreEqual(0, table.Roll(0, rng).Count);
            Assert.AreEqual(0, table.Roll(-3, rng).Count);
        }

        [Test]
        public void RollOnce_EmptyTable_Throws()
        {
            LootTable table = new LootTable();
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d });

            Assert.Throws<InvalidOperationException>(() => table.RollOnce(rng));
        }

        [Test]
        public void RollOnce_NullRandom_Throws()
        {
            LootTable table = new LootTable().Add("a", 1f);

            Assert.Throws<ArgumentNullException>(() => table.RollOnce(null));
        }

        [Test]
        public void LootEntry_InvalidWeight_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootEntry("a", 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootEntry("a", -1f));
        }

        [Test]
        public void LootEntry_MaxLessThanMin_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LootEntry("a", 1f, 5, 2));
        }
    }
}
