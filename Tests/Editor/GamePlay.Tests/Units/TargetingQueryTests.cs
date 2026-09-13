//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Targeting;
using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对 <see cref="TargetingQuery"/> 的过滤、快照构建与最佳目标采集（含阵营感知）的单元测试。
    /// </summary>
    [TestFixture]
    public class TargetingQueryTests
    {
        private const float Delta = 1e-4f;

        // 约定的阵营 Id（默认规则：同 Id 友方，不同 Id 敌对）。
        private const int FactionPlayer = 1;
        private const int FactionEnemy = 2;
        private const int FactionNeutral = 3;

        private static FactionRelations MakeRelations()
        {
            FactionRelations relations = new FactionRelations();
            // 玩家与中立阵营显式设为中立，避免落入“不同阵营默认敌对”。
            relations.SetRelation(FactionPlayer, FactionNeutral, FactionRelation.Neutral);
            return relations;
        }

        private static TargetingQuery MakeQuery()
        {
            return new TargetingQuery(MakeRelations());
        }

        [Test]
        public void Passes_EnemyPassesEnemiesAffinity()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit enemy = new FakeTargetableUnit(10, FactionEnemy);

            TargetFilter filter = TargetFilter.Default; // Enemies
            Assert.IsTrue(query.Passes(enemy, FactionPlayer, filter));
        }

        [Test]
        public void Passes_AllyFailsEnemiesAffinity_PassesAlliesAffinity()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit ally = new FakeTargetableUnit(11, FactionPlayer);

            TargetFilter enemies = TargetFilter.Default; // Enemies
            Assert.IsFalse(query.Passes(ally, FactionPlayer, enemies));

            TargetFilter allies = TargetFilter.Default;
            allies.Affinity = TargetAffinity.Allies;
            Assert.IsTrue(query.Passes(ally, FactionPlayer, allies));
        }

        [Test]
        public void Passes_NeutralAffinity_AndAnyAffinity()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit neutral = new FakeTargetableUnit(12, FactionNeutral);

            TargetFilter neutrals = TargetFilter.Default;
            neutrals.Affinity = TargetAffinity.Neutrals;
            Assert.IsTrue(query.Passes(neutral, FactionPlayer, neutrals));

            TargetFilter any = TargetFilter.Default;
            any.Affinity = TargetAffinity.Any;
            Assert.IsTrue(query.Passes(neutral, FactionPlayer, any));

            // 中立单位面向 Enemies 不应通过。
            Assert.IsFalse(query.Passes(neutral, FactionPlayer, TargetFilter.Default));
        }

        [Test]
        public void Passes_AliveOnly_ExcludesDead()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit dead = new FakeTargetableUnit(13, FactionEnemy, alive: false);

            TargetFilter aliveOnly = TargetFilter.Default; // AliveOnly = true
            Assert.IsFalse(query.Passes(dead, FactionPlayer, aliveOnly));

            TargetFilter allowDead = TargetFilter.Default;
            allowDead.AliveOnly = false;
            Assert.IsTrue(query.Passes(dead, FactionPlayer, allowDead));
        }

        [Test]
        public void Passes_TargetableOnly_ExcludesUntargetable()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit stealthed = new FakeTargetableUnit(14, FactionEnemy, targetable: false);

            TargetFilter targetableOnly = TargetFilter.Default; // TargetableOnly = true
            Assert.IsFalse(query.Passes(stealthed, FactionPlayer, targetableOnly));

            TargetFilter allowUntargetable = TargetFilter.Default;
            allowUntargetable.TargetableOnly = false;
            Assert.IsTrue(query.Passes(stealthed, FactionPlayer, allowUntargetable));
        }

        [Test]
        public void BuildTargetInfos_MapsAllEightFields()
        {
            TargetingQuery query = MakeQuery();
            FakeTargetableUnit enemy = new FakeTargetableUnit(
                id: 42,
                factionId: FactionEnemy,
                x: 3.5f,
                y: -1.25f,
                alive: true,
                targetable: true,
                health: 73f,
                maxHealth: 120f,
                threat: 9.5f,
                priority: 4,
                progress: 0.66f);

            List<ITargetableUnit> candidates = new List<ITargetableUnit> { enemy };
            List<TargetInfo> into = new List<TargetInfo>();

            query.BuildTargetInfos(candidates, FactionPlayer, TargetFilter.Default, -1, into);

            Assert.AreEqual(1, into.Count);
            TargetInfo info = into[0];
            Assert.AreEqual(42, info.Id);
            Assert.AreEqual(3.5f, info.X, Delta);
            Assert.AreEqual(-1.25f, info.Y, Delta);
            Assert.AreEqual(73f, info.Health, Delta);
            Assert.AreEqual(120f, info.MaxHealth, Delta);
            Assert.AreEqual(9.5f, info.Threat, Delta);
            Assert.AreEqual(4, info.Priority);
            Assert.AreEqual(0.66f, info.Progress, Delta);
        }

        [Test]
        public void BuildTargetInfos_ClearsInto_ExcludesExcludeId_AndOnlyPassing()
        {
            TargetingQuery query = MakeQuery();

            FakeTargetableUnit enemyA = new FakeTargetableUnit(1, FactionEnemy);
            FakeTargetableUnit self = new FakeTargetableUnit(2, FactionEnemy);   // 将被 excludeId 排除
            FakeTargetableUnit ally = new FakeTargetableUnit(3, FactionPlayer);  // 面向 Enemies 不通过
            FakeTargetableUnit dead = new FakeTargetableUnit(4, FactionEnemy, alive: false); // AliveOnly 排除
            FakeTargetableUnit enemyB = new FakeTargetableUnit(5, FactionEnemy);

            List<ITargetableUnit> candidates = new List<ITargetableUnit> { enemyA, self, ally, dead, enemyB };

            // into 预填脏数据，验证会被清空。
            List<TargetInfo> into = new List<TargetInfo> { new TargetInfo(999, 0, 0, 0, 0, 0, 0, 0) };

            query.BuildTargetInfos(candidates, FactionPlayer, TargetFilter.Default, 2, into);

            Assert.AreEqual(2, into.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 5 }, new[] { into[0].Id, into[1].Id });
        }

        [Test]
        public void BuildTargetInfos_NullCandidates_JustClears()
        {
            TargetingQuery query = MakeQuery();
            List<TargetInfo> into = new List<TargetInfo> { new TargetInfo(7, 0, 0, 0, 0, 0, 0, 0) };

            query.BuildTargetInfos(null, FactionPlayer, TargetFilter.Default, -1, into);

            Assert.AreEqual(0, into.Count);
        }

        [Test]
        public void TryAcquire_PicksNearestEnemy_IgnoringAlliesDeadUntargetable()
        {
            TargetingQuery query = MakeQuery();

            FakeTargetableUnit nearAlly = new FakeTargetableUnit(1, FactionPlayer, x: 1f, y: 0f);       // 友方 → 忽略
            FakeTargetableUnit nearDeadEnemy = new FakeTargetableUnit(2, FactionEnemy, x: 1f, y: 0f, alive: false);        // 死亡 → 忽略
            FakeTargetableUnit nearStealthEnemy = new FakeTargetableUnit(3, FactionEnemy, x: 1f, y: 0f, targetable: false); // 潜行 → 忽略
            FakeTargetableUnit midEnemy = new FakeTargetableUnit(4, FactionEnemy, x: 5f, y: 0f);        // 合格但较远
            FakeTargetableUnit farEnemy = new FakeTargetableUnit(5, FactionEnemy, x: 9f, y: 0f);        // 合格但最远
            FakeTargetableUnit nearEnemy = new FakeTargetableUnit(6, FactionEnemy, x: 2f, y: 0f);       // 合格且最近 → 应选中

            List<ITargetableUnit> candidates = new List<ITargetableUnit>
            {
                nearAlly, nearDeadEnemy, nearStealthEnemy, midEnemy, farEnemy, nearEnemy
            };

            bool ok = query.TryAcquire(
                candidates, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit best);

            Assert.IsTrue(ok);
            Assert.IsNotNull(best);
            Assert.AreEqual(6, best.Id);
            Assert.AreSame(nearEnemy, best);
        }

        [Test]
        public void TryAcquire_RespectsMaxRange()
        {
            TargetingQuery query = MakeQuery();

            FakeTargetableUnit inRange = new FakeTargetableUnit(1, FactionEnemy, x: 3f, y: 0f);
            FakeTargetableUnit outOfRange = new FakeTargetableUnit(2, FactionEnemy, x: 20f, y: 0f);
            List<ITargetableUnit> candidates = new List<ITargetableUnit> { outOfRange, inRange };

            TargetFilter filter = TargetFilter.EnemiesInRange(5f);

            bool ok = query.TryAcquire(
                candidates, FactionPlayer, 0f, 0f,
                filter, TargetingStrategy.Nearest, -1,
                out ITargetableUnit best);

            Assert.IsTrue(ok);
            Assert.AreEqual(1, best.Id);

            // 把唯一的合格目标移出射程 → 无目标。
            FakeTargetableUnit onlyFar = new FakeTargetableUnit(3, FactionEnemy, x: 100f, y: 0f);
            List<ITargetableUnit> farOnly = new List<ITargetableUnit> { onlyFar };

            bool none = query.TryAcquire(
                farOnly, FactionPlayer, 0f, 0f,
                filter, TargetingStrategy.Nearest, -1,
                out ITargetableUnit shouldBeNull);

            Assert.IsFalse(none);
            Assert.IsNull(shouldBeNull);
        }

        [Test]
        public void TryAcquire_ExcludeId_SkipsSelf()
        {
            TargetingQuery query = MakeQuery();

            // 注意：来源把自身（FFA 场景）也放进候选时，应被 excludeId 跳过。
            FactionRelations ffaRelations = new FactionRelations();
            ffaRelations.SetRelation(FactionEnemy, FactionEnemy, FactionRelation.Enemy); // 自伤阵营
            TargetingQuery ffaQuery = new TargetingQuery(ffaRelations);

            FakeTargetableUnit self = new FakeTargetableUnit(99, FactionEnemy, x: 0f, y: 0f);
            FakeTargetableUnit other = new FakeTargetableUnit(100, FactionEnemy, x: 4f, y: 0f);
            List<ITargetableUnit> candidates = new List<ITargetableUnit> { self, other };

            bool ok = ffaQuery.TryAcquire(
                candidates, FactionEnemy, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, 99,
                out ITargetableUnit best);

            Assert.IsTrue(ok);
            Assert.AreEqual(100, best.Id);
            Assert.AreSame(other, best);
        }

        [Test]
        public void TryAcquire_ReturnsFalse_WhenNoneQualify()
        {
            TargetingQuery query = MakeQuery();

            FakeTargetableUnit ally1 = new FakeTargetableUnit(1, FactionPlayer);
            FakeTargetableUnit ally2 = new FakeTargetableUnit(2, FactionPlayer);
            List<ITargetableUnit> candidates = new List<ITargetableUnit> { ally1, ally2 };

            bool ok = query.TryAcquire(
                candidates, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit best);

            Assert.IsFalse(ok);
            Assert.IsNull(best);
        }

        [Test]
        public void TryAcquire_EmptyAndNullCandidates_ReturnFalse()
        {
            TargetingQuery query = MakeQuery();

            bool emptyOk = query.TryAcquire(
                new List<ITargetableUnit>(), FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit emptyBest);
            Assert.IsFalse(emptyOk);
            Assert.IsNull(emptyBest);

            bool nullOk = query.TryAcquire(
                null, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit nullBest);
            Assert.IsFalse(nullOk);
            Assert.IsNull(nullBest);
        }

        [Test]
        public void TryAcquire_NonNearestStrategy_LowestHealth_ThroughFactionFilter()
        {
            TargetingQuery query = MakeQuery();

            // 一个血量更低但为友方的单位应被阵营过滤忽略；敌方中血量最低者应被选中。
            FakeTargetableUnit lowHpAlly = new FakeTargetableUnit(1, FactionPlayer, x: 1f, y: 0f, health: 5f);
            FakeTargetableUnit highHpEnemy = new FakeTargetableUnit(2, FactionEnemy, x: 1f, y: 0f, health: 90f);
            FakeTargetableUnit lowHpEnemy = new FakeTargetableUnit(3, FactionEnemy, x: 8f, y: 0f, health: 20f);

            List<ITargetableUnit> candidates = new List<ITargetableUnit> { lowHpAlly, highHpEnemy, lowHpEnemy };

            bool ok = query.TryAcquire(
                candidates, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.LowestHealth, -1,
                out ITargetableUnit best);

            Assert.IsTrue(ok);
            Assert.AreEqual(3, best.Id);
            Assert.AreSame(lowHpEnemy, best);
        }

        [Test]
        public void TryAcquire_ReusesBuffers_AcrossCalls_NoCrossContamination()
        {
            TargetingQuery query = MakeQuery();

            FakeTargetableUnit enemy = new FakeTargetableUnit(1, FactionEnemy, x: 2f, y: 0f);
            List<ITargetableUnit> firstCall = new List<ITargetableUnit> { enemy };

            bool firstOk = query.TryAcquire(
                firstCall, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit firstBest);
            Assert.IsTrue(firstOk);
            Assert.AreEqual(1, firstBest.Id);

            // 第二次：全部为友方，复用的缓冲必须被清空，不得残留上次的敌方目标。
            FakeTargetableUnit ally = new FakeTargetableUnit(2, FactionPlayer, x: 2f, y: 0f);
            List<ITargetableUnit> secondCall = new List<ITargetableUnit> { ally };

            bool secondOk = query.TryAcquire(
                secondCall, FactionPlayer, 0f, 0f,
                TargetFilter.Default, TargetingStrategy.Nearest, -1,
                out ITargetableUnit secondBest);

            Assert.IsFalse(secondOk);
            Assert.IsNull(secondBest);
        }

        [Test]
        public void Relations_Property_ExposesInjectedInstance()
        {
            FactionRelations relations = MakeRelations();
            TargetingQuery query = new TargetingQuery(relations);

            Assert.AreSame(relations, query.Relations);
        }
    }
}
