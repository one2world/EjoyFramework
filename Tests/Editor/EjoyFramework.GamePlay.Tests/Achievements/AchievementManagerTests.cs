//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Achievements;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Achievements
{
    /// <summary>
    /// <see cref="AchievementManager"/> 的注册、进度累计、自动达成、领取、汇总与存档恢复单元测试。
    /// </summary>
    [TestFixture]
    public class AchievementManagerTests
    {
        [Test]
        public void Define_StartsLockedWithZeroProgress()
        {
            var mgr = new AchievementManager();
            AchievementState s = mgr.Define(new AchievementDefinition("a1", targetCount: 3, points: 10));

            Assert.AreEqual(AchievementStatus.Locked, s.Status);
            Assert.AreEqual(0, s.CurrentCount);
            Assert.IsFalse(s.IsUnlocked);
        }

        [Test]
        public void Define_NullOrDuplicate_Throws()
        {
            var mgr = new AchievementManager();
            Assert.Throws<ArgumentNullException>(() => mgr.Define(null));
            mgr.Define(new AchievementDefinition("dup"));
            Assert.Throws<ArgumentException>(() => mgr.Define(new AchievementDefinition("dup")));
        }

        [Test]
        public void AddProgress_AccumulatesAndAutoUnlocksAtTarget()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("kill100", targetCount: 100, points: 50));

            int progressEvents = 0;
            AchievementState unlocked = null;
            mgr.OnProgress += (m, s) => progressEvents++;
            mgr.OnUnlocked += (m, s) => unlocked = s;

            Assert.AreEqual(40, mgr.AddProgress("kill100", 40));
            Assert.AreEqual(100, mgr.AddProgress("kill100", 80)); // clamps to target
            Assert.AreEqual(AchievementStatus.Unlocked, mgr.Get("kill100").Status);
            Assert.IsNotNull(unlocked);
            Assert.AreEqual("kill100", unlocked.Definition.Id);
            Assert.AreEqual(2, progressEvents);
        }

        [Test]
        public void AddProgress_OnUnlocked_ReturnsMinusOne()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a", targetCount: 1));
            mgr.AddProgress("a"); // unlocks

            Assert.AreEqual(-1, mgr.AddProgress("a", 5));
            Assert.AreEqual(-1, mgr.AddProgress("missing", 1));
        }

        [Test]
        public void SetProgress_ClampsAndAutoUnlocks()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a", targetCount: 10));

            Assert.IsTrue(mgr.SetProgress("a", 5));
            Assert.AreEqual(5, mgr.Get("a").CurrentCount);
            Assert.IsTrue(mgr.SetProgress("a", 999)); // clamps + unlocks
            Assert.AreEqual(10, mgr.Get("a").CurrentCount);
            Assert.AreEqual(AchievementStatus.Unlocked, mgr.Get("a").Status);
            Assert.IsFalse(mgr.SetProgress("missing", 1));
        }

        [Test]
        public void Unlock_ForcesUnlockFromLockedOnly()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a", targetCount: 5));

            Assert.IsTrue(mgr.Unlock("a"));
            Assert.AreEqual(AchievementStatus.Unlocked, mgr.Get("a").Status);
            Assert.AreEqual(5, mgr.Get("a").CurrentCount);
            Assert.IsFalse(mgr.Unlock("a")); // already unlocked
        }

        [Test]
        public void Claim_OnlyFromUnlocked()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a"));
            Assert.IsFalse(mgr.Claim("a")); // still Locked

            mgr.Unlock("a");
            AchievementState claimed = null;
            mgr.OnClaimed += (m, s) => claimed = s;
            Assert.IsTrue(mgr.Claim("a"));
            Assert.AreEqual(AchievementStatus.Claimed, mgr.Get("a").Status);
            Assert.AreSame(mgr.Get("a"), claimed);
        }

        [Test]
        public void UnlockedCount_And_TotalPoints_Aggregate()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a", points: 10));
            mgr.Define(new AchievementDefinition("b", points: 25));
            mgr.Define(new AchievementDefinition("c", points: 100));

            mgr.Unlock("a");
            mgr.Unlock("b");
            mgr.Claim("b"); // Claimed still counts as unlocked

            Assert.AreEqual(2, mgr.UnlockedCount);
            Assert.AreEqual(35, mgr.TotalPoints);
        }

        [Test]
        public void Restore_SetsStateWithoutFiringEvents()
        {
            var mgr = new AchievementManager();
            mgr.Define(new AchievementDefinition("a", targetCount: 10));

            bool anyEvent = false;
            mgr.OnProgress += (m, s) => anyEvent = true;
            mgr.OnUnlocked += (m, s) => anyEvent = true;

            mgr.Restore("a", 10, AchievementStatus.Claimed);

            Assert.AreEqual(10, mgr.Get("a").CurrentCount);
            Assert.AreEqual(AchievementStatus.Claimed, mgr.Get("a").Status);
            Assert.IsFalse(anyEvent, "Restore 不应触发事件");
        }
    }
}
