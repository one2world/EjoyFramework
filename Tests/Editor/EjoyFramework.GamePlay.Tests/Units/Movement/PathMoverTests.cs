//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Units.Movement;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units.Movement
{
    /// <summary>
    /// 针对 <see cref="PathMover"/> 的单元测试：覆盖按序沿路径点移动、逐点消费、
    /// CurrentWaypointIndex 前移、抵达最终点、单步跨越多个短路径点、空路径无目的地、Stop 清除等。
    /// </summary>
    [TestFixture]
    public class PathMoverTests
    {
        private const float Delta = 1e-4f;

        private static List<Waypoint> Path(params (float x, float y)[] points)
        {
            List<Waypoint> list = new List<Waypoint>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                list.Add(new Waypoint(points[i].x, points[i].y));
            }
            return list;
        }

        [Test]
        public void EmptyPath_NoDestination()
        {
            PathMover mover = new PathMover(5f);

            mover.SetPath(new List<Waypoint>());

            Assert.IsFalse(mover.HasDestination);
            Assert.AreEqual(0, mover.WaypointCount);

            MoveStep step = mover.Step(1f, 2f, 1f);
            Assert.AreEqual(1f, step.X, Delta);
            Assert.AreEqual(2f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(0f, step.DistanceMoved, Delta);
        }

        [Test]
        public void NullPath_NoDestination()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(null);

            Assert.IsFalse(mover.HasDestination);
            Assert.AreEqual(0, mover.WaypointCount);
        }

        [Test]
        public void SetPath_ResetsToFirstWaypoint()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(Path((10f, 0f), (10f, 10f)));

            Assert.IsTrue(mover.HasDestination);
            Assert.AreEqual(2, mover.WaypointCount);
            Assert.AreEqual(0, mover.CurrentWaypointIndex);
        }

        [Test]
        public void FollowsWaypointsInOrder_AndConsumesEach()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(Path((10f, 0f), (10f, 10f)));

            // 第 1 步：朝 (10,0) 前进 5 → (5,0)，仍指向第 0 点。
            MoveStep s1 = mover.Step(0f, 0f, 1f);
            Assert.AreEqual(5f, s1.X, Delta);
            Assert.AreEqual(0f, s1.Y, Delta);
            Assert.IsFalse(s1.Arrived);
            Assert.AreEqual(0, mover.CurrentWaypointIndex);

            // 第 2 步：到达 (10,0) 并消费它，剩余预算继续朝 (10,10)。
            // 距离到 (10,0) 是 5（耗尽预算）→ 吸附并消费第 0 点，进入索引 1。
            MoveStep s2 = mover.Step(5f, 0f, 1f);
            Assert.AreEqual(10f, s2.X, Delta);
            Assert.AreEqual(0f, s2.Y, Delta);
            Assert.IsFalse(s2.Arrived);
            Assert.AreEqual(1, mover.CurrentWaypointIndex);

            // 第 3 步：朝 (10,10) 前进 5 → (10,5)。
            MoveStep s3 = mover.Step(10f, 0f, 1f);
            Assert.AreEqual(10f, s3.X, Delta);
            Assert.AreEqual(5f, s3.Y, Delta);
            Assert.IsFalse(s3.Arrived);

            // 第 4 步：到达最终点 (10,10) → Arrived，路径清空。
            MoveStep s4 = mover.Step(10f, 5f, 1f);
            Assert.AreEqual(10f, s4.X, Delta);
            Assert.AreEqual(10f, s4.Y, Delta);
            Assert.IsTrue(s4.Arrived);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void SingleStep_CrossesMultipleShortWaypoints()
        {
            // 三个共线、间距很短的路径点，一步（预算 10）应连续穿过全部并抵达终点。
            PathMover mover = new PathMover(10f);
            mover.SetPath(Path((1f, 0f), (2f, 0f), (3f, 0f)));

            MoveStep step = mover.Step(0f, 0f, 1f);

            // 终点 (3,0)，累计移动 3，且抵达。
            Assert.AreEqual(3f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsTrue(step.Arrived);
            Assert.AreEqual(3f, step.DistanceMoved, Delta);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void SingleStep_CrossesSomeWaypoints_StopsWhenBudgetExhausted()
        {
            // 预算 2.5：应越过 (1,0) 与 (2,0)，再朝 (3,0) 前进剩余 0.5 → 落在 (2.5,0)。
            PathMover mover = new PathMover(2.5f);
            mover.SetPath(Path((1f, 0f), (2f, 0f), (3f, 0f)));

            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(2.5f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(2.5f, step.DistanceMoved, Delta);
            // 已消费索引 0、1，正朝索引 2 前进。
            Assert.AreEqual(2, mover.CurrentWaypointIndex);
            Assert.IsTrue(mover.HasDestination);
        }

        [Test]
        public void WaypointWithinThreshold_IsConsumedWithoutBudget()
        {
            // 起点恰好落在第 0 点的阈值内：该点应被立即消费，本步继续朝第 1 点推进。
            PathMover mover = new PathMover(5f, arriveThreshold: 0.1f);
            mover.SetPath(Path((0.05f, 0f), (10f, 0f)));

            MoveStep step = mover.Step(0f, 0f, 1f);

            // 第 0 点（0.05,0）在阈值内被消费（不耗预算），随后朝 (10,0) 前进 5 → (5,0)。
            Assert.AreEqual(5f, step.X, Delta);
            Assert.AreEqual(0f, step.Y, Delta);
            Assert.IsFalse(step.Arrived);
            Assert.AreEqual(1, mover.CurrentWaypointIndex);
            Assert.AreEqual(5f, step.DistanceMoved, Delta);
        }

        [Test]
        public void Diagonal_Path_UsesHypotDistance()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(Path((3f, 4f))); // 单点，斜边距离 5

            MoveStep step = mover.Step(0f, 0f, 1f);

            Assert.AreEqual(3f, step.X, Delta);
            Assert.AreEqual(4f, step.Y, Delta);
            Assert.IsTrue(step.Arrived);
            Assert.AreEqual(5f, step.DistanceMoved, Delta);
        }

        [Test]
        public void SinglePointPath_PartialThenArrive()
        {
            PathMover mover = new PathMover(2f);
            mover.SetPath(Path((6f, 0f)));

            MoveStep s1 = mover.Step(0f, 0f, 1f);
            Assert.AreEqual(2f, s1.X, Delta);
            Assert.IsFalse(s1.Arrived);
            Assert.IsTrue(mover.HasDestination);

            MoveStep s2 = mover.Step(2f, 0f, 1f);
            Assert.AreEqual(4f, s2.X, Delta);
            Assert.IsFalse(s2.Arrived);

            MoveStep s3 = mover.Step(4f, 0f, 1f);
            Assert.AreEqual(6f, s3.X, Delta);
            Assert.IsTrue(s3.Arrived);
            Assert.IsFalse(mover.HasDestination);
        }

        [Test]
        public void Stop_ClearsPath()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(Path((10f, 0f), (10f, 10f)));
            Assert.IsTrue(mover.HasDestination);

            mover.Stop();

            Assert.IsFalse(mover.HasDestination);
            Assert.AreEqual(0, mover.WaypointCount);

            MoveStep step = mover.Step(0f, 0f, 1f);
            Assert.AreEqual(0f, step.X, Delta);
            Assert.AreEqual(0f, step.DistanceMoved, Delta);
            Assert.IsFalse(step.Arrived);
        }

        [Test]
        public void SetPath_CopiesWaypoints_MutatingSourceDoesNotAffectMover()
        {
            List<Waypoint> source = Path((10f, 0f));
            PathMover mover = new PathMover(5f);
            mover.SetPath(source);

            // 修改源集合不应影响已复制的内部路径。
            source.Clear();
            source.Add(new Waypoint(999f, 999f));

            MoveStep s1 = mover.Step(0f, 0f, 1f); // 朝原 (10,0) 前进 5 → (5,0)
            Assert.AreEqual(5f, s1.X, Delta);
            Assert.AreEqual(0f, s1.Y, Delta);
        }

        [Test]
        public void SetPath_Reuse_AfterShorterPath_ReportsCorrectCount()
        {
            PathMover mover = new PathMover(5f);
            mover.SetPath(Path((1f, 0f), (2f, 0f), (3f, 0f)));
            Assert.AreEqual(3, mover.WaypointCount);

            // 复用为更短路径：计数应反映新路径，而非旧缓冲容量。
            mover.SetPath(Path((5f, 0f)));
            Assert.AreEqual(1, mover.WaypointCount);
            Assert.AreEqual(0, mover.CurrentWaypointIndex);
            Assert.IsTrue(mover.HasDestination);
        }

        [Test]
        public void ImplementsIMover()
        {
            IMover mover = new PathMover(5f);
            Assert.IsFalse(mover.HasDestination);
            Assert.AreEqual(5f, mover.Speed, Delta);
            Assert.DoesNotThrow(() => mover.Stop());
        }
    }
}
