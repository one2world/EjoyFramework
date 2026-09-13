//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对 <see cref="ProjectileModel"/> 的单元测试：覆盖直线 / 追踪运动、寿命与过期事件、
    /// 强制销毁、重叠检测、穿透与命中去重、平方距离。
    /// </summary>
    [TestFixture]
    public class ProjectileModelTests
    {
        private const float Delta = 1e-4f;

        // ---------------------------------------------------------------
        // 直线运动
        // ---------------------------------------------------------------

        [Test]
        public void Straight_Advances_AlongNormalizedDirection_BySpeedTimesDt()
        {
            var p = new ProjectileModel(0f, 0f, 10f, ProjectileMotion.Straight);
            // 非单位向量，应被内部归一化为 (1, 0)。
            p.SetDirection(5f, 0f);

            p.Tick(0.5f); // 位移 = 10 * 0.5 = 5

            Assert.AreEqual(5f, p.X, Delta);
            Assert.AreEqual(0f, p.Y, Delta);
        }

        [Test]
        public void Straight_Diagonal_NormalizesBeforeMoving()
        {
            var p = new ProjectileModel(0f, 0f, 10f, ProjectileMotion.Straight);
            p.SetDirection(3f, 4f); // 归一化为 (0.6, 0.8)

            p.Tick(1f); // 位移 10

            Assert.AreEqual(6f, p.X, Delta);
            Assert.AreEqual(8f, p.Y, Delta);
        }

        [Test]
        public void Straight_WithNoDirection_StaysStationary()
        {
            var p = new ProjectileModel(2f, 3f, 10f, ProjectileMotion.Straight);
            // 未设置方向。
            p.Tick(1f);

            Assert.AreEqual(2f, p.X, Delta);
            Assert.AreEqual(3f, p.Y, Delta);
        }

        // ---------------------------------------------------------------
        // 追踪运动
        // ---------------------------------------------------------------

        [Test]
        public void Homing_MovesTowardUpdatedTarget()
        {
            var p = new ProjectileModel(0f, 0f, 10f, ProjectileMotion.Homing);
            p.SetTargetPosition(100f, 0f); // 远目标，步长不会越过

            p.Tick(0.5f); // 朝目标移动 5

            Assert.AreEqual(5f, p.X, Delta);
            Assert.AreEqual(0f, p.Y, Delta);
        }

        [Test]
        public void Homing_RetargetsEachTick()
        {
            var p = new ProjectileModel(0f, 0f, 10f, ProjectileMotion.Homing);

            p.SetTargetPosition(100f, 0f);
            p.Tick(0.5f); // → x = 5
            Assert.AreEqual(5f, p.X, Delta);
            Assert.AreEqual(0f, p.Y, Delta);

            // 改为朝 +Y 远目标。
            p.SetTargetPosition(5f, 100f);
            p.Tick(0.5f); // 从 (5,0) 朝 (5,100) 移动 5 → (5,5)
            Assert.AreEqual(5f, p.X, Delta);
            Assert.AreEqual(5f, p.Y, Delta);
        }

        [Test]
        public void Homing_DoesNotOvershootTarget()
        {
            var p = new ProjectileModel(0f, 0f, 100f, ProjectileMotion.Homing);
            p.SetTargetPosition(3f, 0f); // 距离 3，步长 100*1=100 远大于之

            p.Tick(1f);

            // 抵达目标而非越过。
            Assert.AreEqual(3f, p.X, Delta);
            Assert.AreEqual(0f, p.Y, Delta);
        }

        [Test]
        public void Homing_WithNoTarget_StaysStationary()
        {
            var p = new ProjectileModel(7f, 8f, 10f, ProjectileMotion.Homing);
            // 未设置目标。
            p.Tick(1f);

            Assert.AreEqual(7f, p.X, Delta);
            Assert.AreEqual(8f, p.Y, Delta);
        }

        // ---------------------------------------------------------------
        // 寿命与过期事件
        // ---------------------------------------------------------------

        [Test]
        public void Lifetime_Decrements_PerTick()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight, 5f);

            p.Tick(2f);
            Assert.AreEqual(3f, p.Lifetime, Delta);
            Assert.IsFalse(p.IsExpired);

            p.Tick(1f);
            Assert.AreEqual(2f, p.Lifetime, Delta);
            Assert.IsFalse(p.IsExpired);
        }

        [Test]
        public void Lifetime_Expires_AndFiresOnExpiredOnce()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight, 1f);
            int expiredCount = 0;
            ProjectileModel reported = null;
            p.OnExpired += proj =>
            {
                expiredCount++;
                reported = proj;
            };

            Assert.IsFalse(p.IsExpired);

            p.Tick(1f); // 寿命跨越到 0
            Assert.IsTrue(p.IsExpired);
            Assert.AreEqual(0f, p.Lifetime, Delta);
            Assert.AreEqual(1, expiredCount);
            Assert.AreSame(p, reported);

            // 继续 Tick 不再重复触发。
            p.Tick(1f);
            p.Tick(1f);
            Assert.AreEqual(1, expiredCount);
        }

        [Test]
        public void Kill_ExpiresImmediately_AndFiresOnExpiredOnce()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight, 99f);
            int expiredCount = 0;
            p.OnExpired += _ => expiredCount++;

            Assert.IsFalse(p.IsExpired);

            p.Kill();
            Assert.IsTrue(p.IsExpired);
            Assert.AreEqual(1, expiredCount);

            // 重复 Kill 与后续 Tick 都不再触发。
            p.Kill();
            p.Tick(1f);
            Assert.AreEqual(1, expiredCount);
        }

        [Test]
        public void Kill_StopsMotion()
        {
            var p = new ProjectileModel(0f, 0f, 10f, ProjectileMotion.Straight);
            p.SetDirection(1f, 0f);
            p.Kill();

            p.Tick(1f); // 已过期，不应移动

            Assert.AreEqual(0f, p.X, Delta);
            Assert.AreEqual(0f, p.Y, Delta);
        }

        // ---------------------------------------------------------------
        // 重叠检测
        // ---------------------------------------------------------------

        [Test]
        public void Overlaps_TrueWithinHitRadius_FalseOutside()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight)
            {
                HitRadius = 2f
            };

            Assert.IsTrue(p.Overlaps(1.5f, 0f));   // 距离 1.5 < 2
            Assert.IsTrue(p.Overlaps(2f, 0f));     // 距离 2 == 2（边界包含）
            Assert.IsTrue(p.Overlaps(0f, 0f));     // 同点
            Assert.IsFalse(p.Overlaps(2.001f, 0f)); // 略超出
            Assert.IsFalse(p.Overlaps(3f, 0f));    // 距离 3 > 2
        }

        [Test]
        public void Overlaps_ZeroHitRadius_OnlySamePoint()
        {
            var p = new ProjectileModel(1f, 1f, 0f, ProjectileMotion.Straight)
            {
                HitRadius = 0f
            };

            Assert.IsTrue(p.Overlaps(1f, 1f));
            Assert.IsFalse(p.Overlaps(1.0001f, 1f));
        }

        // ---------------------------------------------------------------
        // 平方距离
        // ---------------------------------------------------------------

        [Test]
        public void SqrDistanceTo_IsCorrect()
        {
            var p = new ProjectileModel(1f, 2f, 0f, ProjectileMotion.Straight);

            // (4,6) 相对 (1,2) → (3,4) → 9 + 16 = 25
            Assert.AreEqual(25f, p.SqrDistanceTo(4f, 6f), Delta);
            Assert.AreEqual(0f, p.SqrDistanceTo(1f, 2f), Delta);
        }

        // ---------------------------------------------------------------
        // 穿透与命中去重
        // ---------------------------------------------------------------

        [Test]
        public void RegisterHit_PierceZero_DespawnsOnFirstHit()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight)
            {
                Pierce = 0
            };

            bool shouldDespawn = p.RegisterHit(42);

            Assert.IsTrue(shouldDespawn);
            Assert.IsTrue(p.HasHit(42));
            Assert.AreEqual(0, p.Pierce);
        }

        [Test]
        public void RegisterHit_PierceTwo_KeepsFlyingTwiceThenExhausts()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight)
            {
                Pierce = 2
            };

            // 第 1 次命中：消耗一层穿透，继续飞行。
            Assert.IsFalse(p.RegisterHit(1));
            Assert.AreEqual(1, p.Pierce);

            // 第 2 次命中（不同目标）：再消耗一层，继续飞行。
            Assert.IsFalse(p.RegisterHit(2));
            Assert.AreEqual(0, p.Pierce);

            // 第 3 次命中（不同目标）：穿透耗尽，应销毁。
            Assert.IsTrue(p.RegisterHit(3));
            Assert.AreEqual(0, p.Pierce);

            Assert.IsTrue(p.HasHit(1));
            Assert.IsTrue(p.HasHit(2));
            Assert.IsTrue(p.HasHit(3));
        }

        [Test]
        public void RegisterHit_SameTargetTwice_PreventsDoubleHit_NoPierceConsumed()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight)
            {
                Pierce = 2
            };

            Assert.IsFalse(p.RegisterHit(7)); // 首次命中，消耗一层 → Pierce 1
            Assert.AreEqual(1, p.Pierce);

            // 再次命中同一目标：返回 false，不消耗穿透。
            Assert.IsFalse(p.RegisterHit(7));
            Assert.AreEqual(1, p.Pierce);
            Assert.IsTrue(p.HasHit(7));
        }

        [Test]
        public void HasHit_FalseForUnhitTarget()
        {
            var p = new ProjectileModel(0f, 0f, 0f, ProjectileMotion.Straight);
            Assert.IsFalse(p.HasHit(123));
        }
    }
}
