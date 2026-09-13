//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Battle;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Battle
{
    /// <summary>
    /// 针对 <see cref="BattleState"/> 单位集合管理、队伍筛选与行动顺序计算的单元测试。
    /// </summary>
    [TestFixture]
    public class BattleStateTests
    {
        private static BattleUnit MakeUnit(string id, string team, int maxHp, int atk, int def, int spd)
        {
            return new BattleUnit(id, team, new BattleStats(maxHp, atk, def, spd));
        }

        [Test]
        public void AddUnit_Then_GetUnit_ReturnsSame()
        {
            BattleState state = new BattleState();
            BattleUnit hero = MakeUnit("hero", "A", 100, 10, 5, 8);

            state.AddUnit(hero);

            Assert.AreSame(hero, state.GetUnit("hero"));
        }

        [Test]
        public void GetUnit_Missing_ReturnsNull()
        {
            BattleState state = new BattleState();
            Assert.IsNull(state.GetUnit("nope"));
            Assert.IsNull(state.GetUnit(null));
            Assert.IsNull(state.GetUnit(string.Empty));
        }

        [Test]
        public void AddUnit_Duplicate_Throws()
        {
            BattleState state = new BattleState();
            state.AddUnit(MakeUnit("dup", "A", 100, 1, 0, 1));

            Assert.Throws<ArgumentException>(
                () => state.AddUnit(MakeUnit("dup", "B", 50, 1, 0, 1)));
        }

        [Test]
        public void AddUnit_Null_Throws()
        {
            BattleState state = new BattleState();
            Assert.Throws<ArgumentNullException>(() => state.AddUnit(null));
        }

        [Test]
        public void Units_EnumeratesInInsertionOrder()
        {
            BattleState state = new BattleState();
            state.AddUnit(MakeUnit("c", "A", 10, 1, 0, 1));
            state.AddUnit(MakeUnit("a", "A", 10, 1, 0, 1));
            state.AddUnit(MakeUnit("b", "A", 10, 1, 0, 1));

            List<string> ids = new List<string>();
            foreach (BattleUnit u in state.Units)
            {
                ids.Add(u.Id);
            }

            CollectionAssert.AreEqual(new[] { "c", "a", "b" }, ids);
        }

        [Test]
        public void GetTurnOrder_SortsBySpeedDesc_TieByIdOrdinal()
        {
            BattleState state = new BattleState();
            // 速度：fast=20, midB=10, midA=10, slow=5。midA/midB 同速，期望 Id 升序 midA 先于 midB。
            state.AddUnit(MakeUnit("slow", "A", 10, 1, 0, 5));
            state.AddUnit(MakeUnit("midB", "A", 10, 1, 0, 10));
            state.AddUnit(MakeUnit("fast", "A", 10, 1, 0, 20));
            state.AddUnit(MakeUnit("midA", "A", 10, 1, 0, 10));

            IReadOnlyList<BattleUnit> order = state.GetTurnOrder();

            List<string> ids = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                ids.Add(order[i].Id);
            }

            CollectionAssert.AreEqual(new[] { "fast", "midA", "midB", "slow" }, ids);
        }

        [Test]
        public void GetTurnOrder_ExcludesDeadUnits()
        {
            BattleState state = new BattleState();
            BattleUnit a = MakeUnit("a", "A", 10, 1, 0, 30);
            BattleUnit b = MakeUnit("b", "A", 10, 1, 0, 20);
            BattleUnit c = MakeUnit("c", "A", 10, 1, 0, 10);
            state.AddUnit(a);
            state.AddUnit(b);
            state.AddUnit(c);

            // 击杀 b。
            b.Stats.Hp = 0;

            IReadOnlyList<BattleUnit> order = state.GetTurnOrder();
            List<string> ids = new List<string>();
            for (int i = 0; i < order.Count; i++)
            {
                ids.Add(order[i].Id);
            }

            CollectionAssert.AreEqual(new[] { "a", "c" }, ids);
        }

        [Test]
        public void AliveUnits_OnlyAlive()
        {
            BattleState state = new BattleState();
            BattleUnit a = MakeUnit("a", "A", 10, 1, 0, 1);
            BattleUnit b = MakeUnit("b", "A", 10, 1, 0, 1);
            state.AddUnit(a);
            state.AddUnit(b);
            a.Stats.Hp = 0;

            List<string> alive = new List<string>();
            foreach (BattleUnit u in state.AliveUnits)
            {
                alive.Add(u.Id);
            }

            CollectionAssert.AreEqual(new[] { "b" }, alive);
        }

        [Test]
        public void UnitsOfTeam_FiltersByTeam()
        {
            BattleState state = new BattleState();
            state.AddUnit(MakeUnit("a1", "A", 10, 1, 0, 1));
            state.AddUnit(MakeUnit("b1", "B", 10, 1, 0, 1));
            state.AddUnit(MakeUnit("a2", "A", 10, 1, 0, 1));

            List<string> teamA = new List<string>();
            foreach (BattleUnit u in state.UnitsOfTeam("A"))
            {
                teamA.Add(u.Id);
            }

            CollectionAssert.AreEqual(new[] { "a1", "a2" }, teamA);
            CollectionAssert.IsEmpty(new List<BattleUnit>(state.UnitsOfTeam("Z")));
            CollectionAssert.IsEmpty(new List<BattleUnit>(state.UnitsOfTeam(null)));
        }

        [Test]
        public void IsTeamDefeated_TrueOnlyWhenAllDead()
        {
            BattleState state = new BattleState();
            BattleUnit a1 = MakeUnit("a1", "A", 10, 1, 0, 1);
            BattleUnit a2 = MakeUnit("a2", "A", 10, 1, 0, 1);
            state.AddUnit(a1);
            state.AddUnit(a2);

            Assert.IsFalse(state.IsTeamDefeated("A"));

            a1.Stats.Hp = 0;
            Assert.IsFalse(state.IsTeamDefeated("A"));

            a2.Stats.Hp = 0;
            Assert.IsTrue(state.IsTeamDefeated("A"));

            // 不存在的队伍视为未被击败。
            Assert.IsFalse(state.IsTeamDefeated("ghost"));
            Assert.IsFalse(state.IsTeamDefeated(null));
        }
    }
}
