//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Spatial;

namespace EjoyFramework.GamePlay.Spatial
{
    /// <summary>
    /// 覆盖 2D 空间的均匀空间哈希网格（uniform spatial-hash grid），用于快速空间近邻查询
    /// （兴趣管理 AOI、单位索敌等均可复用）。
    ///
    /// 自 WS3-M1 起为 <see cref="SpatialGrid"/>（Core）的薄包装：保留原有 API 与语义，
    /// 存储与查询由 Core 的侵入式链表网格提供——插入/移动/删除 O(1)、查询零分配、不再为每个单元分配 HashSet。
    /// 新代码请直接使用 <see cref="SpatialGrid"/>。
    ///
    /// 语义：
    /// - 单元索引 = (floor(x/cellSize), floor(y/cellSize))，对负坐标正确向下取整。
    /// - <see cref="AddOrUpdate"/> 既可插入也可移动实体；跨单元移动时重新归桶。
    /// - 两阶段查询：粗筛 <see cref="QueryCells"/>（单元范围内全部实体）、精筛 <see cref="QueryRadius"/>（按欧氏距离）。
    /// - 非线程安全。
    /// </summary>
    public sealed class AoiGrid
    {
        private readonly SpatialGrid m_Grid;

        public AoiGrid(float cellSize)
        {
            if (!(cellSize > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "单元尺寸必须为正数。");
            }

            m_Grid = new SpatialGrid(cellSize);
        }

        /// <summary>单元边长（世界单位）。</summary>
        public float CellSize
        {
            get { return m_Grid.CellSize; }
        }

        /// <summary>当前实体数量。</summary>
        public int EntityCount
        {
            get { return m_Grid.Count; }
        }

        /// <summary>底层 Core 网格（供需要矩形/最近邻/回调查询的调用方直接使用）。</summary>
        public SpatialGrid Grid
        {
            get { return m_Grid; }
        }

        /// <summary>插入或更新实体位置；跨单元时重新归桶。</summary>
        public void AddOrUpdate(int entityId, float x, float y)
        {
            m_Grid.AddOrUpdate(entityId, x, y);
        }

        /// <summary>移除实体；不存在返回 false。</summary>
        public bool Remove(int entityId)
        {
            return m_Grid.Remove(entityId);
        }

        /// <summary>读取实体位置。</summary>
        public bool TryGetPosition(int entityId, out float x, out float y)
        {
            return m_Grid.TryGetPosition(entityId, out x, out y);
        }

        /// <summary>粗筛（分配版）：观察点单元周围 (2*cellRadius+1)² 个单元内的全部实体。</summary>
        public List<int> QueryCells(float x, float y, int cellRadius)
        {
            List<int> results = new List<int>();
            QueryCells(x, y, cellRadius, results);
            return results;
        }

        /// <summary>粗筛（非分配版）。</summary>
        public void QueryCells(float x, float y, int cellRadius, List<int> resultsInto)
        {
            if (resultsInto == null)
            {
                throw new ArgumentNullException(nameof(resultsInto));
            }

            m_Grid.QueryCells(x, y, cellRadius, resultsInto);
        }

        /// <summary>精筛（分配版）：欧氏距离 ≤ radius 的实体。</summary>
        public List<int> QueryRadius(float x, float y, float radius)
        {
            List<int> results = new List<int>();
            QueryRadius(x, y, radius, results);
            return results;
        }

        /// <summary>精筛（非分配版）。</summary>
        public void QueryRadius(float x, float y, float radius, List<int> resultsInto)
        {
            if (resultsInto == null)
            {
                throw new ArgumentNullException(nameof(resultsInto));
            }

            m_Grid.QueryRadius(x, y, radius, resultsInto);
        }

        /// <summary>清空全部实体。</summary>
        public void Clear()
        {
            m_Grid.Clear();
        }
    }
}
