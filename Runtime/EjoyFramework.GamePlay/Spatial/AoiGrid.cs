//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Spatial
{
    /// <summary>
    /// 覆盖 2D 空间的均匀空间哈希网格（uniform spatial-hash grid），用于快速空间近邻查询
    /// （兴趣管理 AOI、单位索敌等均可复用）。属于引擎无关的 GamePlay 基础设施（GamePlay.Core），
    /// 不依赖任何上层子系统（Netcode/Units 等），由各子系统按需引用。
    ///
    /// 设计要点：
    /// - 空间被切分为边长 <see cref="CellSize"/>（世界单位）的正方形单元；实体按其当前坐标所在单元归桶。
    ///   单元索引 = (floor(x/cellSize), floor(y/cellSize))，对负坐标正确向下取整（见 <see cref="CellCoord"/>）。
    /// - <see cref="AddOrUpdate"/> 既可插入也可移动实体；当实体跨单元移动时，会把它从旧桶迁移到新桶（重新哈希）。
    /// - 两阶段查询：
    ///   * 粗筛（broad-phase）<see cref="QueryCells"/> 返回观察点单元周围 (2*cellRadius+1)² 个单元内的全部实体，
    ///     可能含略微超出半径的实体，但开销极低；
    ///   * 精筛（narrow-phase）<see cref="QueryRadius"/> 在粗筛基础上按精确欧氏距离过滤（使用平方距离避免开方）。
    /// - 提供 non-alloc 重载：复用调用方传入的 <see cref="List{T}"/>（先清空再填充），避免高频查询造成 GC 压力。
    ///
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）；游戏侧负责把世界 XZ 映射到本网格的 (x, y)。
    /// </summary>
    public sealed class AoiGrid
    {
        /// <summary>
        /// 单元尺寸（世界单位），构造后不可变。
        /// </summary>
        private readonly float m_CellSize;

        /// <summary>
        /// 单元坐标 -> 该单元内实体 id 集合。仅保留非空桶；桶清空后随即移除，避免字典无限膨胀。
        /// </summary>
        private readonly Dictionary<CellCoord, HashSet<int>> m_Cells;

        /// <summary>
        /// 实体 id -> 其当前世界坐标，便于移动时定位旧单元并支持 <see cref="TryGetPosition"/>。
        /// </summary>
        private readonly Dictionary<int, Position> m_Entities;

        /// <summary>
        /// 构造空间哈希网格。
        /// </summary>
        /// <param name="cellSize">单元尺寸（世界单位），必须为正。</param>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="cellSize"/> 不是正数（含 NaN）时抛出。</exception>
        public AoiGrid(float cellSize)
        {
            if (!(cellSize > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "单元尺寸必须为正数。");
            }

            m_CellSize = cellSize;
            m_Cells = new Dictionary<CellCoord, HashSet<int>>();
            m_Entities = new Dictionary<int, Position>();
        }

        /// <summary>
        /// 单元尺寸（世界单位）。
        /// </summary>
        public float CellSize
        {
            get { return m_CellSize; }
        }

        /// <summary>
        /// 当前网格中被跟踪的实体数量。
        /// </summary>
        public int EntityCount
        {
            get { return m_Entities.Count; }
        }

        /// <summary>
        /// 插入实体或更新其位置：若实体不存在则插入到目标单元；若已存在且跨单元移动，则从旧单元迁移到新单元（重新哈希）。
        /// </summary>
        /// <param name="entityId">实体 id。</param>
        /// <param name="x">世界 X 坐标。</param>
        /// <param name="y">世界 Y 坐标。</param>
        public void AddOrUpdate(int entityId, float x, float y)
        {
            CellCoord newCell = CellCoord.FromWorld(x, y, m_CellSize);

            if (m_Entities.TryGetValue(entityId, out Position old))
            {
                CellCoord oldCell = CellCoord.FromWorld(old.X, old.Y, m_CellSize);
                if (!oldCell.Equals(newCell))
                {
                    // 跨单元移动：先从旧桶移除，再加入新桶。
                    RemoveFromCell(oldCell, entityId);
                    AddToCell(newCell, entityId);
                }
                // 同单元内移动：桶归属不变，仅刷新坐标。
            }
            else
            {
                AddToCell(newCell, entityId);
            }

            m_Entities[entityId] = new Position(x, y);
        }

        /// <summary>
        /// 从网格中移除实体。
        /// </summary>
        /// <param name="entityId">实体 id。</param>
        /// <returns>实体存在并被移除返回 true；不存在返回 false。</returns>
        public bool Remove(int entityId)
        {
            if (!m_Entities.TryGetValue(entityId, out Position pos))
            {
                return false;
            }

            CellCoord cell = CellCoord.FromWorld(pos.X, pos.Y, m_CellSize);
            RemoveFromCell(cell, entityId);
            m_Entities.Remove(entityId);
            return true;
        }

        /// <summary>
        /// 尝试获取实体当前坐标。
        /// </summary>
        /// <param name="entityId">实体 id。</param>
        /// <param name="x">输出世界 X 坐标；实体不存在时为 0。</param>
        /// <param name="y">输出世界 Y 坐标；实体不存在时为 0。</param>
        /// <returns>实体存在返回 true，否则返回 false。</returns>
        public bool TryGetPosition(int entityId, out float x, out float y)
        {
            if (m_Entities.TryGetValue(entityId, out Position pos))
            {
                x = pos.X;
                y = pos.Y;
                return true;
            }

            x = 0f;
            y = 0f;
            return false;
        }

        /// <summary>
        /// 粗筛：返回观察点 (x, y) 所在单元周围 (2*cellRadius+1)² 个单元内的全部实体 id（分配新列表）。
        /// 结果可能包含略微超出实际半径的实体；如需精确半径请使用 <see cref="QueryRadius(float,float,float)"/>。
        /// </summary>
        /// <param name="x">观察点世界 X 坐标。</param>
        /// <param name="y">观察点世界 Y 坐标。</param>
        /// <param name="cellRadius">向外扩展的单元层数（非负）；0 表示仅观察点所在单元。</param>
        /// <returns>命中实体 id 的新列表。</returns>
        public List<int> QueryCells(float x, float y, int cellRadius)
        {
            List<int> results = new List<int>();
            QueryCells(x, y, cellRadius, results);
            return results;
        }

        /// <summary>
        /// 粗筛（non-alloc 重载）：先清空 <paramref name="resultsInto"/>，再填入观察点周围
        /// (2*cellRadius+1)² 个单元内的全部实体 id，供高频查询复用列表、避免 GC。
        /// </summary>
        /// <param name="x">观察点世界 X 坐标。</param>
        /// <param name="y">观察点世界 Y 坐标。</param>
        /// <param name="cellRadius">向外扩展的单元层数（负值按 0 处理）。</param>
        /// <param name="resultsInto">用于装载结果的列表，调用前会被清空；不可为 null。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="resultsInto"/> 为 null 时抛出。</exception>
        public void QueryCells(float x, float y, int cellRadius, List<int> resultsInto)
        {
            if (resultsInto == null)
            {
                throw new ArgumentNullException(nameof(resultsInto));
            }

            resultsInto.Clear();

            if (cellRadius < 0)
            {
                cellRadius = 0;
            }

            CellCoord center = CellCoord.FromWorld(x, y, m_CellSize);
            for (int cx = center.X - cellRadius; cx <= center.X + cellRadius; cx++)
            {
                for (int cy = center.Y - cellRadius; cy <= center.Y + cellRadius; cy++)
                {
                    if (m_Cells.TryGetValue(new CellCoord(cx, cy), out HashSet<int> bucket))
                    {
                        foreach (int id in bucket)
                        {
                            resultsInto.Add(id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 精筛：返回距观察点 (x, y) 欧氏距离不超过 <paramref name="radius"/> 的实体 id（分配新列表）。
        /// 内部先以足够的单元层数做粗筛，再按平方距离精确过滤（避免开方）。
        /// </summary>
        /// <param name="x">观察点世界 X 坐标。</param>
        /// <param name="y">观察点世界 Y 坐标。</param>
        /// <param name="radius">查询半径（世界单位，非负）。</param>
        /// <returns>命中实体 id 的新列表。</returns>
        public List<int> QueryRadius(float x, float y, float radius)
        {
            List<int> results = new List<int>();
            QueryRadius(x, y, radius, results);
            return results;
        }

        /// <summary>
        /// 精筛（non-alloc 重载）：先清空 <paramref name="resultsInto"/>，再填入距观察点欧氏距离不超过
        /// <paramref name="radius"/> 的实体 id（按平方距离过滤，避免开方）。
        /// </summary>
        /// <param name="x">观察点世界 X 坐标。</param>
        /// <param name="y">观察点世界 Y 坐标。</param>
        /// <param name="radius">查询半径（世界单位，负值按 0 处理）。</param>
        /// <param name="resultsInto">用于装载结果的列表，调用前会被清空；不可为 null。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="resultsInto"/> 为 null 时抛出。</exception>
        public void QueryRadius(float x, float y, float radius, List<int> resultsInto)
        {
            if (resultsInto == null)
            {
                throw new ArgumentNullException(nameof(resultsInto));
            }

            resultsInto.Clear();

            if (radius < 0f)
            {
                radius = 0f;
            }

            // 半径覆盖的单元层数：向上取整保证半径范围内的单元全部被粗筛覆盖。
            int cellRadius = (int)Math.Ceiling(radius / m_CellSize);
            float radiusSq = radius * radius;

            CellCoord center = CellCoord.FromWorld(x, y, m_CellSize);
            for (int cx = center.X - cellRadius; cx <= center.X + cellRadius; cx++)
            {
                for (int cy = center.Y - cellRadius; cy <= center.Y + cellRadius; cy++)
                {
                    if (!m_Cells.TryGetValue(new CellCoord(cx, cy), out HashSet<int> bucket))
                    {
                        continue;
                    }

                    foreach (int id in bucket)
                    {
                        Position pos = m_Entities[id];
                        float dx = pos.X - x;
                        float dy = pos.Y - y;
                        if (dx * dx + dy * dy <= radiusSq)
                        {
                            resultsInto.Add(id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清空网格：移除全部实体与单元桶。
        /// </summary>
        public void Clear()
        {
            m_Cells.Clear();
            m_Entities.Clear();
        }

        /// <summary>
        /// 将实体加入指定单元的桶；桶不存在时创建。
        /// </summary>
        private void AddToCell(CellCoord cell, int entityId)
        {
            if (!m_Cells.TryGetValue(cell, out HashSet<int> bucket))
            {
                bucket = new HashSet<int>();
                m_Cells[cell] = bucket;
            }

            bucket.Add(entityId);
        }

        /// <summary>
        /// 将实体从指定单元的桶移除；桶清空后一并删除该桶，保持字典紧凑。
        /// </summary>
        private void RemoveFromCell(CellCoord cell, int entityId)
        {
            if (m_Cells.TryGetValue(cell, out HashSet<int> bucket))
            {
                bucket.Remove(entityId);
                if (bucket.Count == 0)
                {
                    m_Cells.Remove(cell);
                }
            }
        }

        /// <summary>
        /// 紧凑的二维坐标记录，仅在网格内部使用。
        /// </summary>
        private readonly struct Position
        {
            public readonly float X;
            public readonly float Y;

            public Position(float x, float y)
            {
                X = x;
                Y = y;
            }
        }
    }
}
