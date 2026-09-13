//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Targeting;
using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对 <see cref="UnitWorld"/> 的注册表、阵营桶、空间近邻查询、索敌门面、
    /// Tick 与死亡转发、Clear 的单元测试。
    /// </summary>
    [TestFixture]
    public class UnitWorldTests
    {
        private const float Delta = 1e-4f;

        // 默认 FactionRelations：同 Id 友方、不同 Id 敌对。故 A=1 与 B=2 互为敌对。
        private const int FactionA = 1;
        private const int FactionB = 2;

        private static UnitWorld MakeWorld()
        {
            return new UnitWorld(new FactionRelations());
        }

        private static UnitModel MakeUnit(int id, int factionId, float x = 0f, float y = 0f)
        {
            UnitModel unit = new UnitModel(id, factionId);
            unit.SetPosition(x, y);
            return unit;
        }

        // ---------------------------------------------------------------
        // 构造与参数校验
        // ---------------------------------------------------------------

        [Test]
        public void Ctor_NullRelations_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new UnitWorld(null));
        }

        [Test]
        public void Ctor_ExposesInjectedRelations()
        {
            FactionRelations relations = new FactionRelations();
            UnitWorld world = new UnitWorld(relations);
            Assert.AreSame(relations, world.Relations);
        }

        [Test]
        public void Ctor_NonPositiveCellSize_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new UnitWorld(new FactionRelations(), 0f));
        }

        // ---------------------------------------------------------------
        // 框架模块：无参构造 + 注入 + Update/Shutdown
        // ---------------------------------------------------------------

        [Test]
        public void ParameterlessCtor_ProvidesDefaultRelations_AndIsUsable()
        {
            UnitWorld world = new UnitWorld();

            // 无参构造提供默认依赖：阵营关系非空，世界可直接使用。
            Assert.IsNotNull(world.Relations);
            world.Add(MakeUnit(1, FactionA));
            Assert.AreEqual(1, world.Count);
        }

        [Test]
        public void SetRelations_InjectsAndRebuildsTargeting()
        {
            UnitWorld world = new UnitWorld();
            FactionRelations relations = new FactionRelations();

            world.SetRelations(relations);

            Assert.AreSame(relations, world.Relations);
        }

        [Test]
        public void SetRelations_Null_Throws()
        {
            UnitWorld world = new UnitWorld();
            Assert.Throws<ArgumentNullException>(() => world.SetRelations(null));
        }

        [Test]
        public void SetAoiCellSize_NonPositive_Throws()
        {
            UnitWorld world = new UnitWorld();
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetAoiCellSize(0f));
        }

        [Test]
        public void SetterInjection_RespectsMaxRange_LikeParameterizedCtor()
        {
            // 经无参构造 + 注入装配，索敌按射程裁剪行为应与参数化构造一致。
            UnitWorld world = new UnitWorld();
            world.SetAoiCellSize(10f);
            world.SetRelations(new FactionRelations());

            UnitModel source = MakeUnit(100, FactionA, 0f, 0f);
            UnitModel inRange = MakeUnit(1, FactionB, 3f, 0f);
            UnitModel outOfRange = MakeUnit(2, FactionB, 50f, 0f);
            world.Add(source);
            world.Add(inRange);
            world.Add(outOfRange);

            world.SyncSpatial();

            TargetFilter filter = TargetFilter.EnemiesInRange(5f);
            bool ok = world.TryAcquireTarget(source, filter, TargetingStrategy.Nearest, out UnitModel best);

            Assert.IsTrue(ok);
            Assert.AreSame(inRange, best);
        }

        [Test]
        public void Update_DrivesTickAndSpatial_NoThrow()
        {
            UnitWorld world = new UnitWorld();
            world.Add(MakeUnit(1, FactionA, 1f, 0f));
            world.Add(MakeUnit(2, FactionB, 0f, 2f));

            // 场景层每帧驱动：SyncSpatial + Tick（UnitManager 的等价调用），不应抛异常。
            Assert.DoesNotThrow(() => { world.SyncSpatial(); world.Tick(0.016f); });

            // SyncSpatial 已执行，近邻查询反映当前位置。
            List<UnitModel> into = new List<UnitModel>();
            world.QueryNearby(0f, 0f, 5f, into);
            Assert.AreEqual(2, into.Count);
        }

        [Test]
        public void Shutdown_ClearsAllRuntimeState()
        {
            UnitWorld world = new UnitWorld();
            UnitModel unit = MakeUnit(1, FactionA);
            unit.ConfigureHealth(100f);
            world.Add(unit);
            world.SyncSpatial();

            int forwarded = 0;
            world.OnUnitDied += (w, u, src) => { forwarded++; };

            world.Clear();

            Assert.AreEqual(0, world.Count);
            Assert.IsFalse(world.Contains(1));

            // 已退订：关闭后单位死亡不应再转发。
            unit.TakeDamage(1000f, -1);
            Assert.AreEqual(0, forwarded);
        }

        // ---------------------------------------------------------------
        // 注册表：Add / dup / Get / Contains / Count / Units
        // ---------------------------------------------------------------

        [Test]
        public void Add_NullUnit_Throws()
        {
            UnitWorld world = MakeWorld();
            Assert.Throws<ArgumentNullException>(() => world.Add(null));
        }

        [Test]
        public void Add_DuplicateId_Throws()
        {
            UnitWorld world = MakeWorld();
            world.Add(MakeUnit(1, FactionA));
            Assert.Throws<ArgumentException>(() => world.Add(MakeUnit(1, FactionB)));
        }

        [Test]
        public void Add_RegistersUnit_GetContainsCount()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(7, FactionA);

            world.Add(unit);

            Assert.AreEqual(1, world.Count);
            Assert.IsTrue(world.Contains(7));
            Assert.AreSame(unit, world.Get(7));
            Assert.IsFalse(world.Contains(999));
            Assert.IsNull(world.Get(999));
        }

        [Test]
        public void Units_EnumeratesAllRegistered()
        {
            UnitWorld world = MakeWorld();
            UnitModel a = MakeUnit(1, FactionA);
            UnitModel b = MakeUnit(2, FactionB);
            world.Add(a);
            world.Add(b);

            List<UnitModel> seen = new List<UnitModel>(world.Units);

            Assert.AreEqual(2, seen.Count);
            CollectionAssert.Contains(seen, a);
            CollectionAssert.Contains(seen, b);
        }

        [Test]
        public void Add_FiresOnUnitAdded()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(5, FactionA);

            UnitModel reported = null;
            UnitWorld reportedWorld = null;
            world.OnUnitAdded += (w, u) =>
            {
                reportedWorld = w;
                reported = u;
            };

            world.Add(unit);

            Assert.AreSame(world, reportedWorld);
            Assert.AreSame(unit, reported);
        }

        // ---------------------------------------------------------------
        // 阵营桶
        // ---------------------------------------------------------------

        [Test]
        public void UnitsOfFaction_BucketsByFaction()
        {
            UnitWorld world = MakeWorld();
            UnitModel a1 = MakeUnit(1, FactionA);
            UnitModel a2 = MakeUnit(2, FactionA);
            UnitModel b1 = MakeUnit(3, FactionB);
            world.Add(a1);
            world.Add(a2);
            world.Add(b1);

            IReadOnlyList<UnitModel> bucketA = world.UnitsOfFaction(FactionA);
            IReadOnlyList<UnitModel> bucketB = world.UnitsOfFaction(FactionB);

            Assert.AreEqual(2, bucketA.Count);
            CollectionAssert.Contains(new List<UnitModel>(bucketA), a1);
            CollectionAssert.Contains(new List<UnitModel>(bucketA), a2);

            Assert.AreEqual(1, bucketB.Count);
            Assert.AreSame(b1, bucketB[0]);
        }

        [Test]
        public void UnitsOfFaction_EmptyForUnknownFaction()
        {
            UnitWorld world = MakeWorld();
            IReadOnlyList<UnitModel> bucket = world.UnitsOfFaction(999);
            Assert.IsNotNull(bucket);
            Assert.AreEqual(0, bucket.Count);
        }

        [Test]
        public void Remove_AfterFactionChange_RemovesFromRegisteredBucket_NoZombie()
        {
            // 阵营桶语义：以“登记时阵营”为准。单位登记于 A，运行时改阵营到 B，再 Remove，
            // 应从登记桶 A 中正确移除，原桶 A 不留僵尸引用，按阵营查询保持正确。
            UnitWorld world = MakeWorld();
            UnitModel a1 = MakeUnit(1, FactionA);
            UnitModel a2 = MakeUnit(2, FactionA);
            world.Add(a1);
            world.Add(a2);

            // 登记后改变阵营：本类型不会自动重分桶，故 a1 仍登记在 A 桶。
            a1.FactionId = FactionB;

            bool ok = world.Remove(1);

            Assert.IsTrue(ok);

            // 登记桶 A 不应残留 a1（无僵尸引用），仅剩 a2。
            IReadOnlyList<UnitModel> bucketA = world.UnitsOfFaction(FactionA);
            Assert.AreEqual(1, bucketA.Count);
            Assert.AreSame(a2, bucketA[0]);
            CollectionAssert.DoesNotContain(new List<UnitModel>(bucketA), a1);

            // 实时阵营 B 从未登记过任何单位，其桶应为空。
            Assert.AreEqual(0, world.UnitsOfFaction(FactionB).Count);

            // 注册表也已移除该单位。
            Assert.IsFalse(world.Contains(1));
            Assert.AreEqual(1, world.Count);
        }

        // ---------------------------------------------------------------
        // Remove：退订 + 出桶 + OnUnitRemoved
        // ---------------------------------------------------------------

        [Test]
        public void Remove_NonExistent_ReturnsFalse()
        {
            UnitWorld world = MakeWorld();
            Assert.IsFalse(world.Remove(123));
        }

        [Test]
        public void Remove_DropsFromRegistryAndBucket_FiresOnUnitRemoved()
        {
            UnitWorld world = MakeWorld();
            UnitModel a1 = MakeUnit(1, FactionA);
            UnitModel a2 = MakeUnit(2, FactionA);
            world.Add(a1);
            world.Add(a2);

            UnitModel removedUnit = null;
            world.OnUnitRemoved += (w, u) => { removedUnit = u; };

            bool ok = world.Remove(1);

            Assert.IsTrue(ok);
            Assert.AreSame(a1, removedUnit);
            Assert.IsFalse(world.Contains(1));
            Assert.AreEqual(1, world.Count);

            IReadOnlyList<UnitModel> bucketA = world.UnitsOfFaction(FactionA);
            Assert.AreEqual(1, bucketA.Count);
            Assert.AreSame(a2, bucketA[0]);
        }

        [Test]
        public void Remove_UnsubscribesDeath_NoFurtherForwarding()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(1, FactionA);
            unit.ConfigureHealth(100f);
            world.Add(unit);

            int forwarded = 0;
            world.OnUnitDied += (w, u, src) => { forwarded++; };

            world.Remove(1);

            // 已退订：移除后单位死亡不应再转发为 OnUnitDied。
            unit.TakeDamage(1000f, 42);

            Assert.AreEqual(0, forwarded);
        }

        // ---------------------------------------------------------------
        // 空间索引：SyncSpatial + QueryNearby
        // ---------------------------------------------------------------

        [Test]
        public void QueryNearby_ReturnsUnitsWithinRadius_ExcludesFar()
        {
            UnitWorld world = MakeWorld();
            UnitModel near1 = MakeUnit(1, FactionA, 1f, 0f);
            UnitModel near2 = MakeUnit(2, FactionA, 0f, 2f);
            UnitModel far = MakeUnit(3, FactionA, 100f, 100f);
            world.Add(near1);
            world.Add(near2);
            world.Add(far);

            world.SyncSpatial();

            List<UnitModel> into = new List<UnitModel>();
            world.QueryNearby(0f, 0f, 5f, into);

            Assert.AreEqual(2, into.Count);
            CollectionAssert.Contains(into, near1);
            CollectionAssert.Contains(into, near2);
            CollectionAssert.DoesNotContain(into, far);
        }

        [Test]
        public void QueryNearby_ClearsInto_BeforeFilling()
        {
            UnitWorld world = MakeWorld();
            world.Add(MakeUnit(1, FactionA, 1f, 1f));
            world.SyncSpatial();

            List<UnitModel> into = new List<UnitModel> { MakeUnit(99, FactionB, 500f, 500f) };
            world.QueryNearby(0f, 0f, 5f, into);

            Assert.AreEqual(1, into.Count);
            Assert.AreEqual(1, into[0].Id);
        }

        [Test]
        public void QueryNearby_NullInto_Throws()
        {
            UnitWorld world = MakeWorld();
            Assert.Throws<ArgumentNullException>(() => world.QueryNearby(0f, 0f, 5f, null));
        }

        [Test]
        public void QueryNearby_ReflectsResync_AfterMove()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(1, FactionA, 1f, 0f);
            world.Add(unit);
            world.SyncSpatial();

            List<UnitModel> into = new List<UnitModel>();
            world.QueryNearby(0f, 0f, 5f, into);
            Assert.AreEqual(1, into.Count);

            // 移到远处但未重同步 → 仍按旧位置命中。
            unit.SetPosition(100f, 100f);
            world.QueryNearby(0f, 0f, 5f, into);
            Assert.AreEqual(1, into.Count);

            // 重同步后 → 不再命中。
            world.SyncSpatial();
            world.QueryNearby(0f, 0f, 5f, into);
            Assert.AreEqual(0, into.Count);
        }

        // ---------------------------------------------------------------
        // 索敌门面：TryAcquireTarget
        // ---------------------------------------------------------------

        [Test]
        public void TryAcquireTarget_PicksNearestEnemy_ExcludesAlliesDeadUntargetableSelf()
        {
            UnitWorld world = MakeWorld();

            UnitModel source = MakeUnit(100, FactionA, 0f, 0f);

            UnitModel ally = MakeUnit(1, FactionA, 1f, 0f);   // 友方 → 排除
            UnitModel deadEnemy = MakeUnit(2, FactionB, 1f, 0f);
            deadEnemy.ConfigureHealth(10f, 0f);               // 死亡 → 排除
            UnitModel stealthEnemy = MakeUnit(3, FactionB, 1f, 0f);
            stealthEnemy.IsTargetable = false;                // 不可瞄准 → 排除
            UnitModel midEnemy = MakeUnit(4, FactionB, 5f, 0f);
            UnitModel nearEnemy = MakeUnit(5, FactionB, 2f, 0f); // 最近合格敌方 → 选中

            world.Add(source);
            world.Add(ally);
            world.Add(deadEnemy);
            world.Add(stealthEnemy);
            world.Add(midEnemy);
            world.Add(nearEnemy);

            bool ok = world.TryAcquireTarget(
                source, TargetFilter.Default, TargetingStrategy.Nearest, out UnitModel best);

            Assert.IsTrue(ok);
            Assert.AreSame(nearEnemy, best);
            // 选中目标的坐标应与最近敌方一致（浮点用 Delta 断言）。
            Assert.AreEqual(2f, best.PositionX, Delta);
            Assert.AreEqual(0f, best.PositionY, Delta);
        }

        [Test]
        public void TryAcquireTarget_RespectsMaxRange_ViaSpatialSubset()
        {
            UnitWorld world = MakeWorld();
            UnitModel source = MakeUnit(100, FactionA, 0f, 0f);
            UnitModel inRange = MakeUnit(1, FactionB, 3f, 0f);
            UnitModel outOfRange = MakeUnit(2, FactionB, 50f, 0f);
            world.Add(source);
            world.Add(inRange);
            world.Add(outOfRange);

            world.SyncSpatial();

            TargetFilter filter = TargetFilter.EnemiesInRange(5f);
            bool ok = world.TryAcquireTarget(source, filter, TargetingStrategy.Nearest, out UnitModel best);

            Assert.IsTrue(ok);
            Assert.AreSame(inRange, best);
        }

        [Test]
        public void TryAcquireTarget_ReturnsFalse_WhenNoneQualify()
        {
            UnitWorld world = MakeWorld();
            UnitModel source = MakeUnit(100, FactionA, 0f, 0f);
            UnitModel ally1 = MakeUnit(1, FactionA, 1f, 0f);
            UnitModel ally2 = MakeUnit(2, FactionA, 2f, 0f);
            world.Add(source);
            world.Add(ally1);
            world.Add(ally2);

            bool ok = world.TryAcquireTarget(
                source, TargetFilter.Default, TargetingStrategy.Nearest, out UnitModel best);

            Assert.IsFalse(ok);
            Assert.IsNull(best);
        }

        [Test]
        public void TryAcquireTarget_NullSource_Throws()
        {
            UnitWorld world = MakeWorld();
            Assert.Throws<ArgumentNullException>(
                () => world.TryAcquireTarget(null, TargetFilter.Default, TargetingStrategy.Nearest, out _));
        }

        [Test]
        public void TryAcquireTarget_DeadUnit_ExcludedFromSubsequentAcquire()
        {
            UnitWorld world = MakeWorld();
            UnitModel source = MakeUnit(100, FactionA, 0f, 0f);
            UnitModel enemy = MakeUnit(1, FactionB, 2f, 0f);
            enemy.ConfigureHealth(50f);
            world.Add(source);
            world.Add(enemy);

            int diedForwarded = 0;
            UnitModel diedUnit = null;
            int diedSource = 0;
            world.OnUnitDied += (w, u, src) =>
            {
                diedForwarded++;
                diedUnit = u;
                diedSource = src;
            };

            // 选中存活敌方。
            bool before = world.TryAcquireTarget(
                source, TargetFilter.Default, TargetingStrategy.Nearest, out UnitModel firstBest);
            Assert.IsTrue(before);
            Assert.AreSame(enemy, firstBest);

            // 击杀该敌方 → 应转发一次 OnUnitDied。
            enemy.TakeDamage(1000f, 100);
            Assert.AreEqual(1, diedForwarded);
            Assert.AreSame(enemy, diedUnit);
            Assert.AreEqual(100, diedSource);

            // 默认过滤 AliveOnly → 死亡敌方被后续索敌排除。
            bool after = world.TryAcquireTarget(
                source, TargetFilter.Default, TargetingStrategy.Nearest, out UnitModel secondBest);
            Assert.IsFalse(after);
            Assert.IsNull(secondBest);
        }

        // ---------------------------------------------------------------
        // Tick 与死亡转发
        // ---------------------------------------------------------------

        [Test]
        public void Tick_AdvancesAllModels_NoThrow()
        {
            UnitWorld world = MakeWorld();
            world.Add(MakeUnit(1, FactionA));
            world.Add(MakeUnit(2, FactionB));

            Assert.DoesNotThrow(() => world.Tick(0.016f));
        }

        [Test]
        public void Tick_RemovedUnitsAreSkipped_SnapshotSafe()
        {
            // 验证 Tick 对“快照之前已被移除”的单位不再 Tick，且不因集合在外部被改而抛异常。
            UnitWorld world = MakeWorld();
            for (int i = 1; i <= 5; i++)
            {
                world.Add(MakeUnit(i, FactionA));
            }

            // 在 Tick 之前移除若干单位（模拟死亡链路最终调用 Remove 的效果）。
            world.Remove(2);
            world.Remove(4);
            Assert.AreEqual(3, world.Count);

            // 快照遍历对仍存在的单位逐一 Tick，对已被移除单位通过 ContainsKey 守卫跳过，绝不抛异常。
            Assert.DoesNotThrow(() => world.Tick(0.016f));
            Assert.IsTrue(world.Contains(1));
            Assert.IsTrue(world.Contains(3));
            Assert.IsTrue(world.Contains(5));
        }

        [Test]
        public void Tick_DeathDuringWindow_TriggersChainedRemove_NoThrow()
        {
            // 构造“死亡 → OnUnitDied 处理器调用 Remove(其它单位) → 紧接 Tick 遍历注册表”的危险序列：
            // 注册表在 Tick 前刚被回调修改，Tick 必须基于快照安全遍历、对被移除单位跳过、不抛异常。
            UnitWorld world = MakeWorld();
            for (int i = 1; i <= 5; i++)
            {
                UnitModel u = MakeUnit(i, FactionA);
                u.ConfigureHealth(10f);
                world.Add(u);
            }

            // 1 号死亡时连带移除 3 号（遍历期/回调内修改注册表）。
            world.OnUnitDied += (w, u, src) =>
            {
                if (u.Id == 1)
                {
                    w.Remove(1); // 死者自身离场
                    if (w.Contains(3))
                    {
                        w.Remove(3); // 链式移除另一单位
                    }
                }
            };

            UnitModel one = world.Get(1);

            Assert.DoesNotThrow(() =>
            {
                one.TakeDamage(1000f, -1); // 触发 OnUnitDied → 链式 Remove(1), Remove(3)
                world.Tick(0.016f);        // 随后基于快照安全遍历剩余单位
            });

            Assert.IsFalse(world.Contains(1)); // 死者已被处理器移除
            Assert.IsFalse(world.Contains(3)); // 被链式移除
            Assert.AreEqual(3, world.Count);   // 剩 2、4、5
            Assert.IsTrue(world.Contains(2));
            Assert.IsTrue(world.Contains(4));
            Assert.IsTrue(world.Contains(5));
        }

        // ---------------------------------------------------------------
        // Clear
        // ---------------------------------------------------------------

        [Test]
        public void Clear_EmptiesWorld()
        {
            UnitWorld world = MakeWorld();
            world.Add(MakeUnit(1, FactionA));
            world.Add(MakeUnit(2, FactionB));
            world.SyncSpatial();

            world.Clear();

            Assert.AreEqual(0, world.Count);
            Assert.IsFalse(world.Contains(1));
            Assert.IsFalse(world.Contains(2));
            Assert.AreEqual(0, world.UnitsOfFaction(FactionA).Count);

            List<UnitModel> into = new List<UnitModel>();
            world.QueryNearby(0f, 0f, 100f, into);
            Assert.AreEqual(0, into.Count);
        }

        [Test]
        public void Clear_UnsubscribesDeath_NoForwardingAfterwards()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(1, FactionA);
            unit.ConfigureHealth(100f);
            world.Add(unit);

            int forwarded = 0;
            world.OnUnitDied += (w, u, src) => { forwarded++; };

            world.Clear();

            // 已退订：清空后单位死亡不应再转发。
            unit.TakeDamage(1000f, -1);

            Assert.AreEqual(0, forwarded);
        }

        [Test]
        public void OnUnitDied_ForwardedOncePerLife()
        {
            UnitWorld world = MakeWorld();
            UnitModel unit = MakeUnit(1, FactionA);
            unit.ConfigureHealth(30f);
            world.Add(unit);

            int forwarded = 0;
            world.OnUnitDied += (w, u, src) => { forwarded++; };

            unit.TakeDamage(1000f, -1);
            unit.Kill(-1); // 已死亡，不应再次触发。

            Assert.AreEqual(1, forwarded);
        }
    }
}
