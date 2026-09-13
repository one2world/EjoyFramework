//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Pathfinding;

namespace EjoyFramework.GamePlay.Tests.Pathfinding
{
    public class AStarPathfinderReuseTests
    {
        [Test]
        public void ResultIntoOverload_ClearsBeforeFilling()
        {
            var grid = new ArrayPathGrid(5, 1);
            var finder = new AStarPathfinder(grid);

            var path = new List<GridCoord>();
            // 预填充垃圾数据，验证 FindPath 会先清空。
            path.Add(new GridCoord(99, 99));
            path.Add(new GridCoord(88, 88));

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(4, 0), path);

            Assert.AreEqual(PathStatus.Found, status);
            Assert.AreEqual(5, path.Count);
            Assert.AreEqual(new GridCoord(0, 0), path[0]);
            Assert.AreEqual(new GridCoord(4, 0), path[4]);
            CollectionAssert.DoesNotContain(path, new GridCoord(99, 99));
        }

        [Test]
        public void ResultIntoOverload_ClearsOnFailure()
        {
            var grid = new ArrayPathGrid(3, 3);
            var finder = new AStarPathfinder(grid);

            var path = new List<GridCoord> { new GridCoord(7, 7) };

            // 越界终点 => InvalidEndpoint，且必须清空旧内容。
            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(10, 10), path);

            Assert.AreEqual(PathStatus.InvalidEndpoint, status);
            CollectionAssert.IsEmpty(path);
        }

        [Test]
        public void AllocatingOverload_ReturnsPath()
        {
            var grid = new ArrayPathGrid(4, 1);
            var finder = new AStarPathfinder(grid);

            List<GridCoord> path = finder.FindPath(new GridCoord(0, 0), new GridCoord(3, 0));

            Assert.AreEqual(4, path.Count);
            Assert.AreEqual(new GridCoord(0, 0), path[0]);
            Assert.AreEqual(new GridCoord(3, 0), path[3]);
        }

        [Test]
        public void AllocatingOverload_ReturnsEmptyOnNoPath()
        {
            var grid = new ArrayPathGrid(3, 1);
            grid.SetWalkable(1, 0, false); // 把通道一分为二

            var finder = new AStarPathfinder(grid);
            List<GridCoord> path = finder.FindPath(new GridCoord(0, 0), new GridCoord(2, 0));

            CollectionAssert.IsEmpty(path);
        }

        [Test]
        public void RepeatedCalls_ReuseBuffers_AndStayDeterministic()
        {
            var grid = new ArrayPathGrid(8, 8);
            grid.SetWalkable(3, 0, false);
            grid.SetWalkable(3, 1, false);
            grid.SetWalkable(3, 2, false);

            var finder = new AStarPathfinder(grid);
            var start = new GridCoord(0, 0);
            var goal = new GridCoord(7, 7);

            var first = new List<GridCoord>();
            Assert.AreEqual(PathStatus.Found, finder.FindPath(start, goal, first));

            // 同一寻路器多次调用应得到完全相同的确定性路径（缓冲被正确重置）。
            for (int i = 0; i < 5; i++)
            {
                var again = new List<GridCoord>();
                Assert.AreEqual(PathStatus.Found, finder.FindPath(start, goal, again));
                CollectionAssert.AreEqual(first, again,
                    "Repeated searches must yield identical deterministic paths.");
            }
        }

        [Test]
        public void TogglingAllowDiagonal_BetweenCalls_Works()
        {
            var grid = new ArrayPathGrid(5, 5);
            var finder = new AStarPathfinder(grid);
            var start = new GridCoord(0, 0);
            var goal = new GridCoord(4, 4);

            var four = new List<GridCoord>();
            finder.AllowDiagonal = false;
            finder.FindPath(start, goal, four);
            Assert.AreEqual(9, four.Count); // 8 步

            var eight = new List<GridCoord>();
            finder.AllowDiagonal = true;
            finder.FindPath(start, goal, eight);
            Assert.AreEqual(5, eight.Count); // 4 对角步

            Assert.Less(eight.Count, four.Count);
        }

        [Test]
        public void NullResult_Throws()
        {
            var grid = new ArrayPathGrid(2, 2);
            var finder = new AStarPathfinder(grid);
            Assert.Throws<ArgumentNullException>(
                () => finder.FindPath(new GridCoord(0, 0), new GridCoord(1, 1), null));
        }

        [Test]
        public void NullGrid_ConstructorThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new AStarPathfinder(null));
        }
    }
}
