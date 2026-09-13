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
    /// 针对 <see cref="GachaBanner"/> 软/硬保底与档位分配行为的单元测试。
    /// </summary>
    /// <remarks>
    /// 测试卡池：保底档 SSR（基础 0.06），非保底档 SR（基础 0.94）。
    /// 保底配置：软保底起点 74，硬保底 90，软保底每抽 +0.06。
    /// </remarks>
    [TestFixture]
    public class GachaBannerTests
    {
        private const float Delta = 1e-4f;

        private static GachaBanner BuildBanner()
        {
            LootTable ssrPool = new LootTable().Add("ssr_item", 1f);
            LootTable srPool = new LootTable().Add("sr_item", 1f);

            GachaTier ssr = new GachaTier("SSR", 0.06f, ssrPool, isPityTier: true);
            GachaTier sr = new GachaTier("SR", 0.94f, srPool);

            PityConfig pity = new PityConfig(softPityStart: 74, hardPity: 90, softPityRampPerPull: 0.06f);
            return new GachaBanner(new List<GachaTier> { ssr, sr }, pity);
        }

        [Test]
        public void Pull_RollBelowBaseRate_HitsPityTier_NotForced()
        {
            GachaBanner banner = BuildBanner();
            PityState state = new PityState { PullsSincePityTier = 0 };
            // roll=0.01 < 0.06 → 命中 SSR；非硬保底强制 → WasPity=false；状态归 0。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.01d, 0.0d });

            GachaResult result = banner.Pull(rng, state);

            Assert.AreEqual("SSR", result.TierId);
            Assert.AreEqual("ssr_item", result.ItemId);
            Assert.IsFalse(result.WasPity);
            Assert.AreEqual(0, state.PullsSincePityTier);
        }

        [Test]
        public void Pull_RollAboveBaseRate_HitsNonPityTier_IncrementsState()
        {
            GachaBanner banner = BuildBanner();
            PityState state = new PityState { PullsSincePityTier = 0 };
            // roll=0.50 >= 0.06 → 未命中 SSR → 落到 SR；状态 +1。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.50d, 0.0d });

            GachaResult result = banner.Pull(rng, state);

            Assert.AreEqual("SR", result.TierId);
            Assert.AreEqual("sr_item", result.ItemId);
            Assert.IsFalse(result.WasPity);
            Assert.AreEqual(1, state.PullsSincePityTier);
        }

        [Test]
        public void Pull_HardPity_ForcesPityTier_RegardlessOfRoll()
        {
            GachaBanner banner = BuildBanner();
            // PullsSincePityTier=89 → 本抽是第 90 抽 → 达到硬保底 → 强制 SSR。
            PityState state = new PityState { PullsSincePityTier = 89 };
            // roll=0.99 本应错过 SSR，但硬保底优先于掷点直接强制命中。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.99d, 0.0d });

            GachaResult result = banner.Pull(rng, state);

            Assert.AreEqual("SSR", result.TierId);
            Assert.IsTrue(result.WasPity);
            Assert.AreEqual(0, state.PullsSincePityTier);
        }

        [Test]
        public void Pull_AtSoftPityStart_RateStillEqualsBase()
        {
            GachaBanner banner = BuildBanner();
            // PullsSincePityTier=73 → 本抽是第 74 抽 = SoftPityStart → beyond=0 → 有效率仍为 0.06。
            PityState state = new PityState { PullsSincePityTier = 73 };
            // roll=0.09 >= 0.06 → 未命中 SSR（证明软保底在起点当抽尚未抬升概率）。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.09d, 0.0d });

            GachaResult result = banner.Pull(rng, state);

            Assert.AreEqual("SR", result.TierId);
            Assert.AreEqual(74, state.PullsSincePityTier);
        }

        [Test]
        public void Pull_SoftPityRamp_RaisesEffectiveRateAboveBase()
        {
            GachaBanner banner = BuildBanner();
            // PullsSincePityTier=74 → 本抽是第 75 抽 → beyond=1 → 有效率 = 0.06 + 0.06 = 0.12。
            PityState state = new PityState { PullsSincePityTier = 74 };
            // roll=0.09：在基础率 0.06 下会错过（0.09>=0.06），但在 ramp 后的 0.12 下命中（0.09<0.12）。
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.09d, 0.0d });

            GachaResult result = banner.Pull(rng, state);

            Assert.AreEqual("SSR", result.TierId);
            // 软保底自然命中并非硬保底强制 → WasPity=false（见类型文档约定）。
            Assert.IsFalse(result.WasPity);
            Assert.AreEqual(0, state.PullsSincePityTier);
        }

        [Test]
        public void Pull_NonPityIncrement_AccumulatesAcrossPulls()
        {
            GachaBanner banner = BuildBanner();
            PityState state = new PityState { PullsSincePityTier = 0 };
            // 三次均掷 0.50 → 三次都落 SR → 计数累加到 3。
            ScriptedRandomSource rng = new ScriptedRandomSource(
                new[] { 0.50d, 0.0d, 0.50d, 0.0d, 0.50d, 0.0d });

            banner.Pull(rng, state);
            banner.Pull(rng, state);
            banner.Pull(rng, state);

            Assert.AreEqual(3, state.PullsSincePityTier);
        }

        [Test]
        public void PullMany_ReturnsRequestedCount_AndThreadsState()
        {
            GachaBanner banner = BuildBanner();
            PityState state = new PityState { PullsSincePityTier = 0 };
            // 第 1、2 抽 SR（+1，+1=2），第 3 抽 roll=0.01 命中 SSR（归 0）。
            ScriptedRandomSource rng = new ScriptedRandomSource(
                new[] { 0.50d, 0.0d, 0.50d, 0.0d, 0.01d, 0.0d });

            IList<GachaResult> results = banner.PullMany(3, rng, state);

            Assert.AreEqual(3, results.Count);
            Assert.AreEqual("SR", results[0].TierId);
            Assert.AreEqual("SR", results[1].TierId);
            Assert.AreEqual("SSR", results[2].TierId);
            Assert.AreEqual(0, state.PullsSincePityTier);
        }

        [Test]
        public void PullMany_ZeroCount_ReturnsEmpty()
        {
            GachaBanner banner = BuildBanner();
            PityState state = new PityState();
            ScriptedRandomSource rng = new ScriptedRandomSource(new[] { 0.0d });

            IList<GachaResult> results = banner.PullMany(0, rng, state);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Pull_NoPityConfig_DistributesByBaseRate()
        {
            // 无保底档/无保底配置：纯按基础概率分布抽档（此处用普通档位，不标记保底）。
            LootTable aPool = new LootTable().Add("a_item", 1f);
            LootTable bPool = new LootTable().Add("b_item", 1f);
            GachaTier a = new GachaTier("A", 0.30f, aPool);
            GachaTier b = new GachaTier("B", 0.70f, bPool);
            GachaBanner banner = new GachaBanner(new List<GachaTier> { a, b });
            PityState state = new PityState();

            // total=1.0；point=0.20 → 落入 A 段 [0,0.30)。
            ScriptedRandomSource rngA = new ScriptedRandomSource(new[] { 0.20d, 0.0d });
            Assert.AreEqual("A", banner.Pull(rngA, state).TierId);

            // point=0.50 → 落入 B 段 [0.30,1.0)。
            ScriptedRandomSource rngB = new ScriptedRandomSource(new[] { 0.50d, 0.0d });
            Assert.AreEqual("B", banner.Pull(rngB, state).TierId);
        }

        [Test]
        public void Ctor_PityTierWithoutNonPityRate_Throws()
        {
            // 回归：仅有保底档（无任何正基础概率的非保底档）会导致未命中保底档时无处落点，
            // 旧实现会回退到保底档却仍把保底计数 +1，造成“命中保底档却累加计数”的失同步。
            // 现在应在构造期就显式拒绝该配置，让误配卡池尽早失败而非静默破坏保底统计。
            LootTable ssrPool = new LootTable().Add("ssr_item", 1f);
            GachaTier ssrOnly = new GachaTier("SSR", 0.06f, ssrPool, isPityTier: true);
            PityConfig pity = new PityConfig(softPityStart: 74, hardPity: 90, softPityRampPerPull: 0.06f);

            Assert.Throws<ArgumentException>(
                () => new GachaBanner(new List<GachaTier> { ssrOnly }, pity));
        }

        [Test]
        public void Ctor_NonPityRateAllZero_Throws()
        {
            // 回归：非保底档存在但基础概率为 0，其总和仍不为正，同样会破坏保底计数，应被拒绝。
            LootTable ssrPool = new LootTable().Add("ssr_item", 1f);
            LootTable srPool = new LootTable().Add("sr_item", 1f);
            GachaTier ssr = new GachaTier("SSR", 0.06f, ssrPool, isPityTier: true);
            GachaTier srZero = new GachaTier("SR", 0f, srPool);
            PityConfig pity = new PityConfig(softPityStart: 74, hardPity: 90, softPityRampPerPull: 0.06f);

            Assert.Throws<ArgumentException>(
                () => new GachaBanner(new List<GachaTier> { ssr, srZero }, pity));
        }

        [Test]
        public void Ctor_BaseRateSumExceedsOne_Throws()
        {
            // 回归：所有档位基础概率之和超过 1 会静默扭曲分布，应在构造期显式失败。
            LootTable ssrPool = new LootTable().Add("ssr_item", 1f);
            LootTable srPool = new LootTable().Add("sr_item", 1f);
            GachaTier ssr = new GachaTier("SSR", 0.60f, ssrPool, isPityTier: true);
            GachaTier sr = new GachaTier("SR", 0.60f, srPool);

            Assert.Throws<ArgumentException>(
                () => new GachaBanner(new List<GachaTier> { ssr, sr }));
        }

        [Test]
        public void Ctor_BaseRateSumExactlyOne_DoesNotThrow()
        {
            // 边界：基础概率之和恰为 1 是合法配置，不应被自洽性校验误伤。
            Assert.DoesNotThrow(() => BuildBanner());
        }
    }
}
