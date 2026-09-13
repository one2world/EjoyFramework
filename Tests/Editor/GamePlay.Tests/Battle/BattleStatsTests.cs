//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Battle;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Battle
{
    /// <summary>
    /// 针对 <see cref="BattleStats"/> 与 <see cref="BattleUnit"/> 构造、生命值钳制、存活语义的单元测试。
    /// </summary>
    [TestFixture]
    public class BattleStatsTests
    {
        [Test]
        public void Constructor_InitializesHpToMax_AndAlive()
        {
            BattleStats stats = new BattleStats(120, 15, 6, 9);

            Assert.AreEqual(120, stats.MaxHp);
            Assert.AreEqual(120, stats.Hp);
            Assert.AreEqual(15, stats.Attack);
            Assert.AreEqual(6, stats.Defense);
            Assert.AreEqual(9, stats.Speed);
            Assert.IsTrue(stats.IsAlive);
        }

        [Test]
        public void Hp_Setter_ClampsBelowZeroToZero_AndDead()
        {
            BattleStats stats = new BattleStats(100, 10, 0, 5);
            stats.Hp = -50;

            Assert.AreEqual(0, stats.Hp);
            Assert.IsFalse(stats.IsAlive);
        }

        [Test]
        public void Hp_Setter_ClampsAboveMaxToMax()
        {
            BattleStats stats = new BattleStats(100, 10, 0, 5);
            stats.Hp = 999;

            Assert.AreEqual(100, stats.Hp);
        }

        [Test]
        public void Constructor_InvalidArgs_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleStats(0, 1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleStats(10, -1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleStats(10, 1, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleStats(10, 1, 1, -1));
        }

        [Test]
        public void BattleUnit_Constructor_Validates()
        {
            BattleStats stats = new BattleStats(10, 1, 0, 1);
            Assert.Throws<ArgumentException>(() => new BattleUnit(null, "A", stats));
            Assert.Throws<ArgumentException>(() => new BattleUnit("id", string.Empty, stats));
            Assert.Throws<ArgumentNullException>(() => new BattleUnit("id", "A", null));

            BattleUnit u = new BattleUnit("u", "A", stats);
            Assert.AreEqual("u", u.Id);
            Assert.AreEqual("A", u.TeamId);
            Assert.AreSame(stats, u.Stats);
        }
    }
}
