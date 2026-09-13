//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Spatial;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Aoi
{
    /// <summary>
    /// 针对 <see cref="AoiGrid"/> 空间哈希网格的单元测试：插入/更新/移除、跨单元移动重哈希、
    /// 负坐标取整、粗筛邻域、精筛欧氏距离、以及 non-alloc 重载的清空+填充语义。
    /// </summary>
    [TestFixture]
    public class AoiGridTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void AddOrUpdate_ThenTryGetPosition_ReturnsCoordsAndCounts()
        {
            AoiGrid grid = new AoiGrid(10f);
            Assert.AreEqual(0, grid.EntityCount);

            grid.AddOrUpdate(1, 3.5f, -7.25f);
            grid.AddOrUpdate(2, 100f, 100f);

            Assert.AreEqual(2, grid.EntityCount);

            Assert.IsTrue(grid.TryGetPosition(1, out float x, out float y));
            Assert.AreEqual(3.5f, x, Delta);
            Assert.AreEqual(-7.25f, y, Delta);

            Assert.IsFalse(grid.TryGetPosition(999, out float mx, out float my));
            Assert.AreEqual(0f, mx, Delta);
            Assert.AreEqual(0f, my, Delta);
        }

        [Test]
        public void AddOrUpdate_SameEntity_UpdatesPositionNotCount()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 1f, 1f);
            grid.AddOrUpdate(1, 5f, 6f);

            Assert.AreEqual(1, grid.EntityCount);
            Assert.IsTrue(grid.TryGetPosition(1, out float x, out float y));
            Assert.AreEqual(5f, x, Delta);
            Assert.AreEqual(6f, y, Delta);
        }

        [Test]
        public void Move_AcrossCells_UpdatesBuckets()
        {
            AoiGrid grid = new AoiGrid(10f);
            // 实体 1 初始在单元 (0,0)。
            grid.AddOrUpdate(1, 5f, 5f);

            List<int> oldCell = grid.QueryCells(5f, 5f, 0);
            Assert.Contains(1, oldCell);

            // 移动到单元 (5,5)（坐标 55,55），原单元应不再返回它，新单元应返回它。
            grid.AddOrUpdate(1, 55f, 55f);

            List<int> oldCellAfter = grid.QueryCells(5f, 5f, 0);
            Assert.IsFalse(oldCellAfter.Contains(1), "旧单元在跨单元移动后不应再包含该实体。");

            List<int> newCell = grid.QueryCells(55f, 55f, 0);
            Assert.Contains(1, newCell);
        }

        [Test]
        public void Move_WithinSameCell_StaysQueryable()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 1f, 1f);
            // 同单元 (0,0) 内移动。
            grid.AddOrUpdate(1, 9f, 9f);

            List<int> cell = grid.QueryCells(5f, 5f, 0);
            Assert.Contains(1, cell);
            Assert.AreEqual(1, grid.EntityCount);
        }

        [Test]
        public void Remove_RemovesEntity()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 5f, 5f);

            Assert.IsTrue(grid.Remove(1));
            Assert.AreEqual(0, grid.EntityCount);
            Assert.IsFalse(grid.TryGetPosition(1, out _, out _));

            List<int> cell = grid.QueryCells(5f, 5f, 0);
            Assert.IsFalse(cell.Contains(1));

            // 重复移除返回 false。
            Assert.IsFalse(grid.Remove(1));
        }

        [Test]
        public void NegativeCoordinates_HashedWithFloor()
        {
            AoiGrid grid = new AoiGrid(10f);
            // (-5, -5) => floor(-0.5) = -1 => 单元 (-1,-1)。
            grid.AddOrUpdate(1, -5f, -5f);
            // (-15, -15) => floor(-1.5) = -2 => 单元 (-2,-2)，与上不同单元。
            grid.AddOrUpdate(2, -15f, -15f);

            // 查询单元 (-1,-1)（点 -5,-5，cellRadius 0）只应命中实体 1。
            List<int> cellMinus1 = grid.QueryCells(-5f, -5f, 0);
            Assert.Contains(1, cellMinus1);
            Assert.IsFalse(cellMinus1.Contains(2));

            // 查询单元 (-2,-2)（点 -15,-15）只应命中实体 2。
            List<int> cellMinus2 = grid.QueryCells(-15f, -15f, 0);
            Assert.Contains(2, cellMinus2);
            Assert.IsFalse(cellMinus2.Contains(1));
        }

        [Test]
        public void NegativeCoordinate_BoundaryMapsToFloorCell()
        {
            AoiGrid grid = new AoiGrid(10f);
            // 正好落在 -10 边界：floor(-10/10) = -1 => 单元 (-1,*)。
            grid.AddOrUpdate(1, -10f, 0f);
            // -10.0001 => floor(-1.00001) = -2。
            grid.AddOrUpdate(2, -10.0001f, 0f);

            List<int> cellAtMinus1 = grid.QueryCells(-10f, 0f, 0);
            Assert.Contains(1, cellAtMinus1);
            Assert.IsFalse(cellAtMinus1.Contains(2));
        }

        [Test]
        public void QueryCells_BroadPhase_IncludesNeighbors()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 5f, 5f);    // 单元 (0,0)
            grid.AddOrUpdate(2, 15f, 5f);   // 单元 (1,0) —— 邻居
            grid.AddOrUpdate(3, 5f, 15f);   // 单元 (0,1) —— 邻居
            grid.AddOrUpdate(4, 35f, 5f);   // 单元 (3,0) —— 超出 1 层邻域

            // cellRadius=0 仅命中中心单元。
            List<int> r0 = grid.QueryCells(5f, 5f, 0);
            Assert.Contains(1, r0);
            Assert.IsFalse(r0.Contains(2));
            Assert.IsFalse(r0.Contains(3));

            // cellRadius=1 命中中心及其 8 邻域。
            List<int> r1 = grid.QueryCells(5f, 5f, 1);
            Assert.Contains(1, r1);
            Assert.Contains(2, r1);
            Assert.Contains(3, r1);
            Assert.IsFalse(r1.Contains(4), "距离 3 个单元的实体不应进入 1 层邻域。");
        }

        [Test]
        public void QueryRadius_ExactEuclideanFilter()
        {
            AoiGrid grid = new AoiGrid(10f);
            // 观察点 (0,0)，半径 5。
            grid.AddOrUpdate(1, 3f, 4f);     // 距离恰好 5.0 —— 在边界上（<=），应包含。
            grid.AddOrUpdate(2, 3f, 4.001f);  // 距离略大于 5 —— 应排除。
            grid.AddOrUpdate(3, 0f, 0f);      // 距离 0 —— 包含。

            List<int> hits = grid.QueryRadius(0f, 0f, 5f);
            Assert.Contains(1, hits);
            Assert.Contains(3, hits);
            Assert.IsFalse(hits.Contains(2), "略微超出半径的实体应被精筛排除。");
        }

        [Test]
        public void QueryRadius_SpansMultipleCells()
        {
            AoiGrid grid = new AoiGrid(10f);
            // 半径 25 跨多个单元；确认粗筛层数足够、不漏掉远端单元中的命中实体。
            grid.AddOrUpdate(1, 0f, 0f);
            grid.AddOrUpdate(2, 20f, 0f);    // 距离 20 —— 包含。
            grid.AddOrUpdate(3, 0f, -22f);   // 距离 22 —— 包含。
            grid.AddOrUpdate(4, 30f, 0f);    // 距离 30 —— 排除。

            List<int> hits = grid.QueryRadius(0f, 0f, 25f);
            Assert.Contains(1, hits);
            Assert.Contains(2, hits);
            Assert.Contains(3, hits);
            Assert.IsFalse(hits.Contains(4));
        }

        [Test]
        public void QueryCells_NonAlloc_ClearsThenFills()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 5f, 5f);

            // 预置脏数据，验证方法会先清空。
            List<int> buffer = new List<int> { 777, 888 };
            grid.QueryCells(5f, 5f, 0, buffer);

            Assert.IsFalse(buffer.Contains(777));
            Assert.IsFalse(buffer.Contains(888));
            Assert.Contains(1, buffer);
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void QueryRadius_NonAlloc_ClearsThenFills()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 1f, 1f);
            grid.AddOrUpdate(2, 100f, 100f);

            List<int> buffer = new List<int> { -1 };
            grid.QueryRadius(0f, 0f, 5f, buffer);

            Assert.IsFalse(buffer.Contains(-1));
            Assert.Contains(1, buffer);
            Assert.IsFalse(buffer.Contains(2));
            Assert.AreEqual(1, buffer.Count);
        }

        [Test]
        public void Clear_RemovesEverything()
        {
            AoiGrid grid = new AoiGrid(10f);
            grid.AddOrUpdate(1, 5f, 5f);
            grid.AddOrUpdate(2, 15f, 15f);

            grid.Clear();

            Assert.AreEqual(0, grid.EntityCount);
            Assert.IsFalse(grid.TryGetPosition(1, out _, out _));
            List<int> hits = grid.QueryCells(5f, 5f, 5);
            Assert.AreEqual(0, hits.Count);
        }

        [Test]
        public void CellSize_ReflectsConstructorValue()
        {
            AoiGrid grid = new AoiGrid(12.5f);
            Assert.AreEqual(12.5f, grid.CellSize, Delta);
        }
    }
}
