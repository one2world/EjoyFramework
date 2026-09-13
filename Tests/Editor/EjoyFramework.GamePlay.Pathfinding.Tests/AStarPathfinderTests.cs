//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Pathfinding;

namespace EjoyFramework.GamePlay.Tests.Pathfinding
{
    public class AStarPathfinderTests
    {
        private const float Delta = 1e-4f;

        // 计算一条路径的总进入代价（不含起点）：直行 += GetCost，对角 += 1.414×GetCost。
        private static float PathCost(IPathGrid grid, IReadOnlyList<GridCoord> path)
        {
            const float sqrt2 = 1.41421356237f;
            float cost = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                GridCoord prev = path[i - 1];
                GridCoord cur = path[i];
                bool diagonal = prev.X != cur.X && prev.Y != cur.Y;
                float enter = grid.GetCost(cur.X, cur.Y);
                cost += diagonal ? enter * sqrt2 : enter;
            }

            return cost;
        }

        // 校验路径首尾正确且每一步都是合法的（相邻、可行走、不切角）邻接移动。
        private static void AssertValidPath(IPathGrid grid, IReadOnlyList<GridCoord> path,
            GridCoord start, GridCoord goal, bool allowDiagonal)
        {
            Assert.Greater(path.Count, 0, "Path must be non-empty.");
            Assert.AreEqual(start, path[0], "Path must start at start.");
            Assert.AreEqual(goal, path[path.Count - 1], "Path must end at goal.");

            for (int i = 1; i < path.Count; i++)
            {
                GridCoord a = path[i - 1];
                GridCoord b = path[i];
                int dx = b.X - a.X;
                int dy = b.Y - a.Y;
                int adx = dx < 0 ? -dx : dx;
                int ady = dy < 0 ? -dy : dy;

                Assert.IsTrue(grid.IsWalkable(b.X, b.Y), "Every step must enter a walkable cell.");

                if (allowDiagonal)
                {
                    Assert.IsTrue(adx <= 1 && ady <= 1 && (adx + ady) >= 1,
                        "8-dir steps must move to an adjacent cell.");
                    if (adx == 1 && ady == 1)
                    {
                        Assert.IsTrue(grid.IsWalkable(a.X + dx, a.Y) && grid.IsWalkable(a.X, a.Y + dy),
                            "Diagonal step must not cut a corner between two walls.");
                    }
                }
                else
                {
                    Assert.AreEqual(1, adx + ady, "4-dir steps must be orthogonal and length 1.");
                }
            }
        }

        [Test]
        public void StraightLine_OpenGrid_ExactLength()
        {
            var grid = new ArrayPathGrid(10, 1);
            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(9, 0), path);

            Assert.AreEqual(PathStatus.Found, status);
            // 10 个格子（0..9 含两端）。
            Assert.AreEqual(10, path.Count);
            AssertValidPath(grid, path, new GridCoord(0, 0), new GridCoord(9, 0), false);
        }

        [Test]
        public void StartEqualsGoal_ReturnsSingleCell()
        {
            var grid = new ArrayPathGrid(5, 5);
            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(2, 2), new GridCoord(2, 2), path);

            Assert.AreEqual(PathStatus.Found, status);
            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(new GridCoord(2, 2), path[0]);
        }

        [Test]
        public void AroundWall_RoutesAround_CorrectLength()
        {
            // 5x5 网格，在 x=2 列竖一道墙（y=0..3），仅 (2,4) 留口。
            // 从 (0,0) 到 (4,0) 必须绕到 y=4 穿过缺口再回来。
            var grid = new ArrayPathGrid(5, 5);
            for (int y = 0; y <= 3; y++)
            {
                grid.SetWalkable(2, y, false);
            }

            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(4, 0), path);

            Assert.AreEqual(PathStatus.Found, status);
            AssertValidPath(grid, path, new GridCoord(0, 0), new GridCoord(4, 0), false);

            // 不能直接穿过 x=2 的墙：路径上 x=2 的格子必须是缺口 (2,4)。
            foreach (GridCoord c in path)
            {
                if (c.X == 2)
                {
                    Assert.AreEqual(4, c.Y, "Crossing the wall column is only allowed through the gap at (2,4).");
                }
            }

            // 曼哈顿最短：下行 4 + 右行 4 + 上行 4 = 12 步 => 13 个格子。
            Assert.AreEqual(13, path.Count);
        }

        [Test]
        public void NoPath_WhenGoalWalledOff()
        {
            // 把 (4,4) 周围全部封死（4 邻接），使终点无法到达。
            var grid = new ArrayPathGrid(5, 5);
            grid.SetWalkable(3, 4, false);
            grid.SetWalkable(4, 3, false);

            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(4, 4), path);

            Assert.AreEqual(PathStatus.NoPath, status);
            CollectionAssert.IsEmpty(path);
        }

        [Test]
        public void InvalidEndpoint_OutOfBounds()
        {
            var grid = new ArrayPathGrid(5, 5);
            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            Assert.AreEqual(PathStatus.InvalidEndpoint,
                finder.FindPath(new GridCoord(-1, 0), new GridCoord(4, 4), path));
            CollectionAssert.IsEmpty(path);

            Assert.AreEqual(PathStatus.InvalidEndpoint,
                finder.FindPath(new GridCoord(0, 0), new GridCoord(5, 5), path));
            CollectionAssert.IsEmpty(path);
        }

        [Test]
        public void InvalidEndpoint_BlockedStartOrGoal()
        {
            var grid = new ArrayPathGrid(5, 5);
            grid.SetWalkable(0, 0, false);
            grid.SetWalkable(4, 4, false);

            var finder = new AStarPathfinder(grid);
            var path = new List<GridCoord>();

            Assert.AreEqual(PathStatus.InvalidEndpoint,
                finder.FindPath(new GridCoord(0, 0), new GridCoord(3, 3), path));

            // 恢复起点，封住终点。
            grid.SetWalkable(0, 0, true);
            Assert.AreEqual(PathStatus.InvalidEndpoint,
                finder.FindPath(new GridCoord(0, 0), new GridCoord(4, 4), path));
        }

        [Test]
        public void Diagonal_ShortensPath_VersusFourDir()
        {
            var grid = new ArrayPathGrid(6, 6);
            var start = new GridCoord(0, 0);
            var goal = new GridCoord(5, 5);

            var fourDir = new AStarPathfinder(grid, allowDiagonal: false);
            var fourPath = new List<GridCoord>();
            Assert.AreEqual(PathStatus.Found, fourDir.FindPath(start, goal, fourPath));
            // 4 邻接：10 步 => 11 个格子。
            Assert.AreEqual(11, fourPath.Count);

            var eightDir = new AStarPathfinder(grid, allowDiagonal: true);
            var eightPath = new List<GridCoord>();
            Assert.AreEqual(PathStatus.Found, eightDir.FindPath(start, goal, eightPath));
            // 8 邻接：纯对角 5 步 => 6 个格子。
            Assert.AreEqual(6, eightPath.Count);

            Assert.Less(eightPath.Count, fourPath.Count, "Diagonal movement must shorten the path.");
            AssertValidPath(grid, eightPath, start, goal, true);

            // 对角总代价 ~= 5 * √2 ≈ 7.071，小于 4 邻接的 10。
            Assert.AreEqual(5f * 1.41421356f, PathCost(grid, eightPath), Delta);
        }

        [Test]
        public void WeightedCost_AvoidsExpensiveCorridor()
        {
            // 3 行通道：中间行 (y=1) 是直达但昂贵的"沼泽"；上下行便宜。
            // 直达需穿过若干高代价格子，绕行虽更长但更便宜，A* 应选绕行。
            var grid = new ArrayPathGrid(5, 3);
            for (int x = 1; x <= 3; x++)
            {
                grid.SetCost(x, 1, 50f); // 中间行昂贵
            }

            var finder = new AStarPathfinder(grid);
            var start = new GridCoord(0, 1);
            var goal = new GridCoord(4, 1);
            var path = new List<GridCoord>();

            Assert.AreEqual(PathStatus.Found, finder.FindPath(start, goal, path));
            AssertValidPath(grid, path, start, goal, false);

            // 应避开昂贵的中间格子（x=1..3, y=1）。
            foreach (GridCoord c in path)
            {
                bool expensive = c.Y == 1 && c.X >= 1 && c.X <= 3;
                Assert.IsFalse(expensive, "Path should avoid the expensive corridor cells.");
            }

            // 绕行总代价（全为 1 的便宜格）应远小于直穿沼泽。
            float detourCost = PathCost(grid, path);
            Assert.Less(detourCost, 50f, "Detour must be cheaper than crossing the swamp.");
        }

        [Test]
        public void CornerCutting_PreventedBetweenTwoWalls()
        {
            // 在 (1,0) 与 (0,1) 放两面墙，使从 (0,0) 到 (1,1) 的对角被夹在两墙之间。
            // 8 邻接下不得切角斜穿；只能绕行（实际上此例会变成无路）。
            var grid = new ArrayPathGrid(2, 2);
            grid.SetWalkable(1, 0, false);
            grid.SetWalkable(0, 1, false);

            var finder = new AStarPathfinder(grid, allowDiagonal: true);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(1, 1), path);

            // (0,0) 的唯一对角邻居 (1,1) 被两墙夹住，禁止切角 => 无路可达。
            Assert.AreEqual(PathStatus.NoPath, status);
            CollectionAssert.IsEmpty(path);
        }

        [Test]
        public void CornerCutting_AllowedWhenOnlyOneWall()
        {
            // 仅一面墙时仍可斜行：(1,0) 是墙，(0,1) 可走。
            // 此时从 (0,0) 斜到 (1,1) 合法（切角规则要求两正交格均可走，这里 (0,1) 可走，(1,0) 是墙 => 仍禁止该对角）。
            // 因此正确预期是：直接对角被禁，需经 (0,1) 折线到达 (1,1)。
            var grid = new ArrayPathGrid(2, 2);
            grid.SetWalkable(1, 0, false);

            var finder = new AStarPathfinder(grid, allowDiagonal: true);
            var path = new List<GridCoord>();

            PathStatus status = finder.FindPath(new GridCoord(0, 0), new GridCoord(1, 1), path);

            Assert.AreEqual(PathStatus.Found, status);
            AssertValidPath(grid, path, new GridCoord(0, 0), new GridCoord(1, 1), true);
            // 不能斜穿墙角：必须经由 (0,1)。
            Assert.AreEqual(3, path.Count);
            Assert.AreEqual(new GridCoord(0, 1), path[1]);
        }

        [Test]
        public void Diagonal_FreeCorner_AllowsDirectDiagonal()
        {
            // 完全开阔时，(0,0)->(1,1) 的对角合法（两正交格均可走），路径为 2 个格子。
            var grid = new ArrayPathGrid(2, 2);
            var finder = new AStarPathfinder(grid, allowDiagonal: true);
            var path = new List<GridCoord>();

            Assert.AreEqual(PathStatus.Found, finder.FindPath(new GridCoord(0, 0), new GridCoord(1, 1), path));
            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(new GridCoord(0, 0), path[0]);
            Assert.AreEqual(new GridCoord(1, 1), path[1]);
        }
    }
}
