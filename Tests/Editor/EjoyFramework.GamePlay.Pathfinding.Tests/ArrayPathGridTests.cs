//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Pathfinding;

namespace EjoyFramework.GamePlay.Tests.Pathfinding
{
    public class ArrayPathGridTests
    {
        private const float Delta = 1e-5f;

        [Test]
        public void Defaults_AllWalkableCostOne()
        {
            var grid = new ArrayPathGrid(4, 3);
            Assert.AreEqual(4, grid.Width);
            Assert.AreEqual(3, grid.Height);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Assert.IsTrue(grid.IsWalkable(x, y));
                    Assert.AreEqual(1f, grid.GetCost(x, y), Delta);
                }
            }
        }

        [Test]
        public void SetWalkable_TogglesCell()
        {
            var grid = new ArrayPathGrid(3, 3);
            grid.SetWalkable(1, 1, false);
            Assert.IsFalse(grid.IsWalkable(1, 1));
            grid.SetWalkable(1, 1, true);
            Assert.IsTrue(grid.IsWalkable(1, 1));
        }

        [Test]
        public void IsWalkable_OutOfBounds_ReturnsFalse()
        {
            var grid = new ArrayPathGrid(2, 2);
            Assert.IsFalse(grid.IsWalkable(-1, 0));
            Assert.IsFalse(grid.IsWalkable(0, -1));
            Assert.IsFalse(grid.IsWalkable(2, 0));
            Assert.IsFalse(grid.IsWalkable(0, 2));
        }

        [Test]
        public void SetCost_ClampsBelowOneToOne()
        {
            var grid = new ArrayPathGrid(2, 2);
            grid.SetCost(0, 0, 5f);
            Assert.AreEqual(5f, grid.GetCost(0, 0), Delta);

            // IPathGrid contract requires GetCost >= 1 so the A* heuristic stays admissible.
            grid.SetCost(0, 0, -3f);
            Assert.AreEqual(1f, grid.GetCost(0, 0), Delta);

            grid.SetCost(0, 0, 0.25f);
            Assert.AreEqual(1f, grid.GetCost(0, 0), Delta);
        }

        [Test]
        public void SetWalkable_OutOfBounds_IsIgnored()
        {
            var grid = new ArrayPathGrid(2, 2);
            Assert.DoesNotThrow(() => grid.SetWalkable(99, 99, false));
            Assert.DoesNotThrow(() => grid.SetCost(-5, -5, 10f));
        }

        [Test]
        public void Constructor_RejectsNonPositiveSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayPathGrid(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayPathGrid(4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayPathGrid(-1, -1));
        }
    }
}
