//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Spatial
{
    /// <summary>
    /// 二维空间哈希网格（XZ 平面；开放世界的流送/索敌/感知/种群都按地面平面查询）。
    /// 元素是 int 句柄（实体 id / 区块 id），位置由调用方 AddOrUpdate 推送。
    ///
    /// 设计：
    ///   • 元素记录存在一张槽位表（数组），每个 cell 维护一条**侵入式链表**（槽位内的 Next/Prev 下标），
    ///     不为每个 cell 分配 HashSet：插入/移动/删除 O(1)，查询遍历 cell 链表零分配。
    ///   • cell 字典键是 (cx, cz) 打包成的 long，一次哈希；元素移动时只有跨 cell 才改链表。
    ///   • 查询写入调用方提供的 List（先 Clear），或走 <see cref="ISpatialVisitor"/> 回调不收集。
    ///   • 圆形查询先按 cell 粗筛再按距离精筛；矩形查询按 cell 覆盖精筛；最近邻查询按环扩张、可提前终止。
    ///
    /// 线程契约：非线程安全；主线程或调用方自行同步。
    /// </summary>
    public sealed class SpatialGrid
    {
        private struct Slot
        {
            public int Id;
            public float X;
            public float Z;
            public long Cell;
            public int Next;     // 同 cell 链表
            public int Prev;
            public bool InUse;
        }

        private const int None = -1;

        private readonly float m_CellSize;
        private readonly Dictionary<long, int> m_CellHeads = new Dictionary<long, int>();
        private readonly Dictionary<int, int> m_IdToSlot = new Dictionary<int, int>();
        private Slot[] m_Slots = new Slot[64];
        private int m_SlotCount;
        private readonly Stack<int> m_FreeSlots = new Stack<int>();

        public SpatialGrid(float cellSize)
        {
            if (!(cellSize > 0f))
            {
                throw new FrameworkException("SpatialGrid：cellSize 必须为正数。");
            }

            m_CellSize = cellSize;
        }

        /// <summary>单元边长。</summary>
        public float CellSize
        {
            get { return m_CellSize; }
        }

        /// <summary>元素数量。</summary>
        public int Count
        {
            get { return m_IdToSlot.Count; }
        }

        /// <summary>非空 cell 数量。</summary>
        public int OccupiedCellCount
        {
            get { return m_CellHeads.Count; }
        }

        // ================================================================
        //  维护
        // ================================================================

        /// <summary>插入或更新位置。跨 cell 时 O(1) 重链。</summary>
        public void AddOrUpdate(int id, float x, float z)
        {
            long cell = CellKey(x, z);
            int slot;
            if (m_IdToSlot.TryGetValue(id, out slot))
            {
                ref Slot s = ref m_Slots[slot];
                s.X = x;
                s.Z = z;
                if (s.Cell != cell)
                {
                    Unlink(slot);
                    s.Cell = cell;
                    Link(slot);
                }

                return;
            }

            slot = AcquireSlot();
            m_IdToSlot.Add(id, slot);
            ref Slot n = ref m_Slots[slot];
            n.Id = id;
            n.X = x;
            n.Z = z;
            n.Cell = cell;
            n.InUse = true;
            Link(slot);
        }

        /// <summary>移除元素；不存在返回 false。</summary>
        public bool Remove(int id)
        {
            int slot;
            if (!m_IdToSlot.TryGetValue(id, out slot))
            {
                return false;
            }

            Unlink(slot);
            m_IdToSlot.Remove(id);
            ref Slot s = ref m_Slots[slot];
            s.InUse = false;
            s.Id = 0;
            m_FreeSlots.Push(slot);
            return true;
        }

        /// <summary>是否包含元素。</summary>
        public bool Contains(int id)
        {
            return m_IdToSlot.ContainsKey(id);
        }

        /// <summary>读取元素位置。</summary>
        public bool TryGetPosition(int id, out float x, out float z)
        {
            int slot;
            if (m_IdToSlot.TryGetValue(id, out slot))
            {
                x = m_Slots[slot].X;
                z = m_Slots[slot].Z;
                return true;
            }

            x = 0f;
            z = 0f;
            return false;
        }

        /// <summary>清空（容量保留）。</summary>
        public void Clear()
        {
            m_CellHeads.Clear();
            m_IdToSlot.Clear();
            m_FreeSlots.Clear();
            Array.Clear(m_Slots, 0, m_SlotCount);
            m_SlotCount = 0;
        }

        // ================================================================
        //  查询（全部零分配）
        // ================================================================

        /// <summary>圆形范围查询：距离 ≤ radius 的元素 id 写入 results（先 Clear）。返回数量。</summary>
        public int QueryRadius(float x, float z, float radius, List<int> results)
        {
            if (results == null)
            {
                throw new FrameworkException("SpatialGrid.QueryRadius：results 不能为 null。");
            }

            results.Clear();
            if (radius < 0f) radius = 0f;
            float radiusSq = radius * radius;
            int minCx = FloorCell(x - radius), maxCx = FloorCell(x + radius);
            int minCz = FloorCell(z - radius), maxCz = FloorCell(z + radius);
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    int head;
                    if (!m_CellHeads.TryGetValue(Pack(cx, cz), out head)) continue;
                    for (int i = head; i != None; i = m_Slots[i].Next)
                    {
                        float dx = m_Slots[i].X - x;
                        float dz = m_Slots[i].Z - z;
                        if (dx * dx + dz * dz <= radiusSq) results.Add(m_Slots[i].Id);
                    }
                }
            }

            return results.Count;
        }

        /// <summary>圆形范围查询（回调版，不收集）。visitor 返回 false 可提前终止。</summary>
        public void QueryRadius(float x, float z, float radius, ISpatialVisitor visitor)
        {
            if (visitor == null)
            {
                throw new FrameworkException("SpatialGrid.QueryRadius：visitor 不能为 null。");
            }

            if (radius < 0f) radius = 0f;
            float radiusSq = radius * radius;
            int minCx = FloorCell(x - radius), maxCx = FloorCell(x + radius);
            int minCz = FloorCell(z - radius), maxCz = FloorCell(z + radius);
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    int head;
                    if (!m_CellHeads.TryGetValue(Pack(cx, cz), out head)) continue;
                    for (int i = head; i != None; i = m_Slots[i].Next)
                    {
                        float dx = m_Slots[i].X - x;
                        float dz = m_Slots[i].Z - z;
                        float distSq = dx * dx + dz * dz;
                        if (distSq <= radiusSq && !visitor.Visit(m_Slots[i].Id, distSq)) return;
                    }
                }
            }
        }

        /// <summary>轴对齐矩形查询：[minX,maxX]×[minZ,maxZ] 内的元素写入 results（先 Clear）。</summary>
        public int QueryRect(float minX, float minZ, float maxX, float maxZ, List<int> results)
        {
            if (results == null)
            {
                throw new FrameworkException("SpatialGrid.QueryRect：results 不能为 null。");
            }

            results.Clear();
            if (maxX < minX || maxZ < minZ) return 0;
            int minCx = FloorCell(minX), maxCx = FloorCell(maxX);
            int minCz = FloorCell(minZ), maxCz = FloorCell(maxZ);
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                for (int cz = minCz; cz <= maxCz; cz++)
                {
                    int head;
                    if (!m_CellHeads.TryGetValue(Pack(cx, cz), out head)) continue;
                    for (int i = head; i != None; i = m_Slots[i].Next)
                    {
                        float px = m_Slots[i].X, pz = m_Slots[i].Z;
                        if (px >= minX && px <= maxX && pz >= minZ && pz <= maxZ) results.Add(m_Slots[i].Id);
                    }
                }
            }

            return results.Count;
        }

        /// <summary>按 cell 半径查询：以 (x,z) 所在 cell 为中心、cellRadius 圈内所有 cell 的元素（不按距离精筛）。</summary>
        public int QueryCells(float x, float z, int cellRadius, List<int> results)
        {
            if (results == null)
            {
                throw new FrameworkException("SpatialGrid.QueryCells：results 不能为 null。");
            }

            results.Clear();
            if (cellRadius < 0) cellRadius = 0;
            int cx0 = FloorCell(x), cz0 = FloorCell(z);
            for (int cx = cx0 - cellRadius; cx <= cx0 + cellRadius; cx++)
            {
                for (int cz = cz0 - cellRadius; cz <= cz0 + cellRadius; cz++)
                {
                    int head;
                    if (!m_CellHeads.TryGetValue(Pack(cx, cz), out head)) continue;
                    for (int i = head; i != None; i = m_Slots[i].Next) results.Add(m_Slots[i].Id);
                }
            }

            return results.Count;
        }

        /// <summary>
        /// 最近邻：按 cell 环由内向外扩张，找到候选后再多查一环保证正确性（对角 cell 可能更近）。
        /// 找不到（超过 maxRadius 或网格为空）返回 false。
        /// </summary>
        public bool QueryNearest(float x, float z, float maxRadius, out int id, out float distance, int excludeId = int.MinValue)
        {
            id = 0;
            distance = 0f;
            if (m_IdToSlot.Count == 0 || maxRadius < 0f) return false;

            int cx0 = FloorCell(x), cz0 = FloorCell(z);
            int maxRing = (int)Math.Ceiling(maxRadius / m_CellSize) + 1;
            float bestSq = float.MaxValue;
            int bestId = 0;
            bool found = false;

            for (int ring = 0; ring <= maxRing; ring++)
            {
                // 已找到候选且当前环的最小可能距离已超过候选距离：不可能更近，停止。
                if (found)
                {
                    float ringMin = (ring - 1) * m_CellSize;
                    if (ringMin > 0f && ringMin * ringMin > bestSq) break;
                }

                for (int cx = cx0 - ring; cx <= cx0 + ring; cx++)
                {
                    for (int cz = cz0 - ring; cz <= cz0 + ring; cz++)
                    {
                        if (ring > 0 && cx != cx0 - ring && cx != cx0 + ring && cz != cz0 - ring && cz != cz0 + ring) continue;   // 只扫环
                        int head;
                        if (!m_CellHeads.TryGetValue(Pack(cx, cz), out head)) continue;
                        for (int i = head; i != None; i = m_Slots[i].Next)
                        {
                            if (m_Slots[i].Id == excludeId) continue;
                            float dx = m_Slots[i].X - x;
                            float dz = m_Slots[i].Z - z;
                            float d = dx * dx + dz * dz;
                            if (d < bestSq)
                            {
                                bestSq = d;
                                bestId = m_Slots[i].Id;
                                found = true;
                            }
                        }
                    }
                }
            }

            if (!found || bestSq > maxRadius * maxRadius) return false;
            id = bestId;
            distance = (float)Math.Sqrt(bestSq);
            return true;
        }

        /// <summary>单元坐标（供流送分区与调试使用）。</summary>
        public void GetCell(float x, float z, out int cx, out int cz)
        {
            cx = FloorCell(x);
            cz = FloorCell(z);
        }

        // ================================================================
        //  内部
        // ================================================================

        private int FloorCell(float v)
        {
            // Floor 而非截断：原点两侧单元必须对称（-0.1 → -1）。
            // 用除法而不是乘倒数：Mono 会以更高精度求值 float 乘法（ECMA 允许），-10 * 0.1f 在 double 中间值下
            // 是 -1.0000000149 → floor 成 -2，边界元素落错 cell；除法按 IEEE 精确舍入，-10 / 10 恒为 -1。
            return (int)Math.Floor(v / m_CellSize);
        }

        private long CellKey(float x, float z)
        {
            return Pack(FloorCell(x), FloorCell(z));
        }

        private static long Pack(int cx, int cz)
        {
            return ((long)cx << 32) | (uint)cz;
        }

        private int AcquireSlot()
        {
            if (m_FreeSlots.Count > 0) return m_FreeSlots.Pop();
            if (m_SlotCount == m_Slots.Length) Array.Resize(ref m_Slots, m_Slots.Length * 2);
            return m_SlotCount++;
        }

        private void Link(int slot)
        {
            ref Slot s = ref m_Slots[slot];
            int head;
            if (m_CellHeads.TryGetValue(s.Cell, out head))
            {
                s.Next = head;
                s.Prev = None;
                m_Slots[head].Prev = slot;
                m_CellHeads[s.Cell] = slot;
            }
            else
            {
                s.Next = None;
                s.Prev = None;
                m_CellHeads.Add(s.Cell, slot);
            }
        }

        private void Unlink(int slot)
        {
            ref Slot s = ref m_Slots[slot];
            if (s.Prev != None) m_Slots[s.Prev].Next = s.Next;
            else
            {
                if (s.Next != None) m_CellHeads[s.Cell] = s.Next;
                else m_CellHeads.Remove(s.Cell);
            }

            if (s.Next != None) m_Slots[s.Next].Prev = s.Prev;
            s.Next = None;
            s.Prev = None;
        }
    }

    /// <summary>空间查询回调；返回 false 提前终止。</summary>
    public interface ISpatialVisitor
    {
        bool Visit(int id, float distanceSquared);
    }
}
