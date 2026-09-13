//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Units.Movement;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units.Movement
{
    /// <summary>
    /// 针对 <see cref="StraightMover"/> 的单元测试：覆盖按 speed*dt 推进、到达吸附、
    /// 移动距离正确、无目的地不移动、对角线距离（hypot）、Stop 清除目的地等。
    /// </summary>
    [TestFixture]
    public class StraightMoverTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void NoDestination_DoesNotMove()
        {
            StraightMover mover = new StraightMover(5f);

            Assert.IsFalse(mover.HasDestination);

            MoveStep step = mover.Step(3f, 4f, 1f);

            Assert.AreEqual(3f, step.X, Delta);
            Assert.AreEqual(4f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(0f, step.DistanceMoved, Delta);
        }

        [Test]
        public void Step_MovesTowardDestination_AtSpeedTimesDelta()
        {
            StraightMover mover = new StraightMover(2f);
            mover.SetDestination(10f, 0f);

            // 距离 10，预算 2*1 = 2 → 前进到 x = 2，未抵达。
            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(2f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(2f, step.DistanceMoved, Delta);
            Assert.IsTrue(mover.HasDestination);
        }

        [Test]
        public void Step_HalfSecond_MovesHalfTheBudget()
        {
            StraightMover mover = new StraightMover(4f);
            mover.SetDestination(0f, 100f);

            // 预算 4 * 0.5 = 2 → 沿 +Y 前进 2。
            MoveStep step = mover.Step(0f, 0f, 0.5f);

            Assert.AreEqual(0f, step.X, Delta);
            Assert.AreEqual(2f, step.Y, Delta);
            Assert.AreEqual(2f, step.DistanceMoved, Delta);
            Assert.IsFalse(step.Arrived);
        }

        [Test]
        public void Step_ArrivesWhenBudgetReachesDestination_SnapsAndClears()
        {
            StraightMover mover = new StraightMover(10f);
            mover.SetDestination(3f, 0f);

            // 距离 3，预算 10 → 一步即可抵达，吸附到终点。
            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(3f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsTrue(step.Arrived);
            Assert.AreEqual(3f, step.DistanceMoved, Delta);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void Step_ArrivesWhenWithinThreshold_EvenIfBudgetIsTiny()
        {
            StraightMover mover = new StraightMover(0.0001f, arriveThreshold: 0.1f);
            mover.SetDestination(0.05f, 0f);

            // 剩余距离 0.05 ≤ 阈值 0.1 → 直接抵达（即便预算极小）。
            MoveStep step = mover.Step(0f, 0f, 0.016f);

            Assert.AreEqual(0.05f, step.X, Delta);
            Assert.IsTrue(step.Arrived);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void Step_Diagonal_UsesHypotForDistance()
        {
            StraightMover mover = new StraightMover(5f);
            mover.SetDestination(3f, 4f); // 斜边距离 = 5

            // 预算 5 * 1 = 5 == 距离 → 一步抵达 (3,4)。
            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(3f, step.X, Delta);
            Assert.AreEqual(4f, step.Y, Delta);
            Assert.IsTrue(step.Arrived);
            Assert.AreEqual(5f, step.DistanceMoved, Delta);
        }

        [Test]
        public void Step_Diagonal_PartialMove_PreservesDirection()
        {
            StraightMover mover = new StraightMover(2.5f);
            mover.SetDestination(3f, 4f); // 距离 5，方向 (0.6, 0.8)

            // 预算 2.5 → 应前进到方向上的一半，即 (1.5, 2.0)。
            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(1.5f, step.X, Delta);
            Assert.AreEqual(2.0f, step.Y, Delta);
            Assert.AreEqual(2.5f, step.DistanceMoved, Delta);
            Assert.IsFalse(step.Arrived);
        }

        [Test]
        public void MultipleSteps_ConvergeAndArrive()
        {
            StraightMover mover = new StraightMover(3f);
            mover.SetDestination(10f, 0f);

            float x = 0f;
            bool arrived = false;
            // 距离 10，每步 3 → 4 步内抵达（3,6,9,10）。
            for (int i = 0; i < 10 && !arrived; i++)
            {
                MoveStep step = mover.Step(x, 0f, 1f);
                x = step.X;
                arrived = step.Arrived;
            }

            Assert.IsTrue(arrived);
            Assert.AreEqual(10f, x, Delta);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void Stop_ClearsDestination()
        {
            StraightMover mover = new StraightMover(5f);
            mover.SetDestination(10f, 10f);
            Assert.IsTrue(mover.HasDestination);

            mover.Stop();

            Assert.IsFalse(mover.HasDestination);
            MoveStep step = mover.Step(0f, 0f, 1f);
            Assert.AreEqual(0f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(0f, step.DistanceMoved, Delta);
        }

        [Test]
        public void Speed_Setter_ClampsNegativeToZero()
        {
            StraightMover mover = new StraightMover(-3f);
            Assert.AreEqual(0f, mover.Speed, Delta);

            mover.Speed = 7f;
            Assert.AreEqual(7f, mover.Speed, Delta);

            mover.Speed = -1f;
            Assert.AreEqual(0f, mover.Speed, Delta);
        }

        [Test]
        public void ArriveThreshold_Setter_ClampsNegativeToZero()
        {
            StraightMover mover = new StraightMover(5f, arriveThreshold: -1f);
            Assert.AreEqual(0f, mover.ArriveThreshold, Delta);

            mover.ArriveThreshold = 0.25f;
            Assert.AreEqual(0.25f, mover.ArriveThreshold, Delta);
        }

        [Test]
        public void Step_ZeroSpeed_NoThresholdReach_DoesNotMoveButKeepsDestination()
        {
            StraightMover mover = new StraightMover(0f, arriveThreshold: 0.05f);
            mover.SetDestination(100f, 0f);

            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(0f, step.X, Delta);
            Assert.AreEqual(0f, step.DistanceMoved, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.IsTrue(mover.HasDestination);
        }

        [Test]
        public void ImplementsIMover()
        {
            IMover mover = new StraightMover(5f);
            Assert.IsFalse(mover.HasDestination);
            Assert.AreEqual(5f, mover.Speed, Delta);
            Assert.DoesNotThrow(() => mover.Stop());
        }
    }
}
