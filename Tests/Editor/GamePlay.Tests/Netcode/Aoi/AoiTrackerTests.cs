//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Aoi;
using EjoyFramework.GamePlay.Spatial;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Aoi
{
    /// <summary>
    /// 针对 <see cref="AoiTracker"/> 视野追踪与进入/离开差分的单元测试：首次报告全部进入、
    /// 移出报告离开、移入报告进入、观察者排除自身、视野查询、以及移除观察者。
    /// </summary>
    [TestFixture]
    public class AoiTrackerTests
    {
        private static List<int> Entered()
        {
            return new List<int>();
        }

        private static List<int> Left()
        {
            return new List<int>();
        }

        [Test]
        public void FirstUpdate_ReportsAllInViewAsEntered_NoneLeft()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);   // 观察者
            grid.AddOrUpdate(1, 5f, 0f);     // 视野内
            grid.AddOrUpdate(2, 10f, 0f);    // 视野内
            grid.AddOrUpdate(3, 100f, 0f);   // 视野外

            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.Contains(1, entered);
            Assert.Contains(2, entered);
            Assert.IsFalse(entered.Contains(3));
            Assert.IsFalse(entered.Contains(100), "观察者不应进入自身视野。");
            Assert.AreEqual(0, left.Count);
        }

        [Test]
        public void Observer_ExcludedFromOwnView()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 1f, 1f);

            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            IReadOnlyCollection<int> view = tracker.GetView(100);
            CollectionAssert.DoesNotContain(view, 100);
            CollectionAssert.Contains(view, 1);
        }

        [Test]
        public void EntityMovesOut_ReportedAsLeft()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 5f, 0f);

            // 第一次：实体 1 进入视野。
            tracker.UpdateObserver(100, Entered(), Left());

            // 实体 1 移出视野半径。
            grid.AddOrUpdate(1, 200f, 0f);

            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.Contains(1, left);
            Assert.IsFalse(entered.Contains(1));
            CollectionAssert.DoesNotContain(tracker.GetView(100), 1);
        }

        [Test]
        public void EntityMovesIn_ReportedAsEntered()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 200f, 0f);   // 初始在视野外

            // 第一次：视野为空。
            List<int> entered0 = Entered();
            List<int> left0 = Left();
            tracker.UpdateObserver(100, entered0, left0);
            Assert.AreEqual(0, entered0.Count);
            Assert.AreEqual(0, left0.Count);

            // 实体 1 移入视野。
            grid.AddOrUpdate(1, 3f, 0f);

            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.Contains(1, entered);
            Assert.AreEqual(0, left.Count);
        }

        [Test]
        public void ObserverMoves_DiffReflectsNewPosition()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 12f);

            grid.AddOrUpdate(100, 0f, 0f);   // 观察者
            grid.AddOrUpdate(1, 5f, 0f);     // 近原点
            grid.AddOrUpdate(2, 200f, 0f);   // 远处

            tracker.UpdateObserver(100, Entered(), Left());
            // 此刻视野含实体 1，不含实体 2。

            // 观察者移动到实体 2 附近。
            grid.AddOrUpdate(100, 200f, 0f);

            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.Contains(2, entered);  // 现在能看到实体 2
            Assert.Contains(1, left);     // 看不到实体 1 了
        }

        [Test]
        public void NoChange_ReportsEmptyDiffs()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 5f, 0f);

            tracker.UpdateObserver(100, Entered(), Left());

            // 没有任何移动，再次更新差分应为空。
            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.AreEqual(0, entered.Count);
            Assert.AreEqual(0, left.Count);
            CollectionAssert.Contains(tracker.GetView(100), 1);
        }

        [Test]
        public void GetView_ReflectsCurrentSet()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 5f, 0f);
            grid.AddOrUpdate(2, 8f, 0f);

            tracker.UpdateObserver(100, Entered(), Left());

            IReadOnlyCollection<int> view = tracker.GetView(100);
            Assert.AreEqual(2, view.Count);
            CollectionAssert.Contains(view, 1);
            CollectionAssert.Contains(view, 2);
        }

        [Test]
        public void GetView_UnknownObserver_ReturnsEmpty()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            IReadOnlyCollection<int> view = tracker.GetView(42);
            Assert.IsNotNull(view);
            Assert.AreEqual(0, view.Count);
        }

        [Test]
        public void RemoveObserver_ClearsStoredView()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 5f, 0f);
            tracker.UpdateObserver(100, Entered(), Left());
            Assert.AreEqual(1, tracker.GetView(100).Count);

            tracker.RemoveObserver(100);
            Assert.AreEqual(0, tracker.GetView(100).Count);

            // 移除后下一次更新视实体 1 为新进入（视野历史已清空）。
            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);
            Assert.Contains(1, entered);
            Assert.AreEqual(0, left.Count);
        }

        [Test]
        public void UpdateObserver_ClearsInputListsAtStart()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 5f, 0f);

            // 预置脏数据，方法应在开始时清空。
            List<int> entered = new List<int> { 555 };
            List<int> left = new List<int> { 666 };
            tracker.UpdateObserver(100, entered, left);

            Assert.IsFalse(entered.Contains(555));
            Assert.IsFalse(left.Contains(666));
            Assert.Contains(1, entered);
        }

        [Test]
        public void ViewRadius_Setter_AffectsNextUpdate()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 5f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(1, 8f, 0f);   // 距离 8：半径 5 时看不到，半径 12 时看得到。

            tracker.UpdateObserver(100, Entered(), Left());
            CollectionAssert.DoesNotContain(tracker.GetView(100), 1);

            tracker.ViewRadius = 12f;
            List<int> entered = Entered();
            List<int> left = Left();
            tracker.UpdateObserver(100, entered, left);

            Assert.Contains(1, entered);
            Assert.AreEqual(12f, tracker.ViewRadius, 1e-4f);
        }

        [Test]
        public void Clear_RemovesAllObservers()
        {
            AoiGrid grid = new AoiGrid(10f);
            AoiTracker tracker = new AoiTracker(grid, 15f);

            grid.AddOrUpdate(100, 0f, 0f);
            grid.AddOrUpdate(200, 5f, 0f);
            grid.AddOrUpdate(1, 2f, 0f);

            tracker.UpdateObserver(100, Entered(), Left());
            tracker.UpdateObserver(200, Entered(), Left());

            tracker.Clear();

            Assert.AreEqual(0, tracker.GetView(100).Count);
            Assert.AreEqual(0, tracker.GetView(200).Count);
        }
    }
}
