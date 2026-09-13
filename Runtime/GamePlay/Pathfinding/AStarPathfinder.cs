//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Pathfinding
{
    /// <summary>
    /// A* 网格寻路器：在 <see cref="IPathGrid"/> 上搜索从起点到终点的最短（最小代价）路径。
    /// 适用于塔防 / 策略 / RPG 等基于格子的移动寻路。
    ///
    /// 纯逻辑、引擎无关：不依赖 UnityEngine，仅依赖 System.*。单线程使用，非线程安全
    /// （内部复用的 open/closed/came-from 缓冲在并发调用下会相互破坏）。
    ///
    /// 邻接与启发（admissible，可采纳）：
    /// - 默认 4 邻接（上下左右）；<see cref="AllowDiagonal"/> 为真时 8 邻接（含四个对角）。
    /// - 4 邻接启发：曼哈顿距离 (|dx| + |dy|)。
    /// - 8 邻接启发：八分（octile）距离 = (max - min) + 1.414 × min，其中 min/max 为 |dx|、|dy| 的小/大值。
    /// - 两种启发都对"最小进入代价 = 1"成立，因而可采纳（不会高估），保证 A* 求得最优路径。
    ///
    /// 代价模型：
    /// - g 代价累加目标邻居的 <see cref="IPathGrid.GetCost"/>（"进入"代价）。
    /// - 直行进入：g += GetCost(neighbor)。
    /// - 对角进入：g += 1.414213562… × GetCost(neighbor)（对角距离更长，按 √2 缩放进入代价）。
    /// - 阻挡格子（<see cref="IPathGrid.IsWalkable"/> 为 false）永不进入。
    ///
    /// 8 邻接的"切角"规则（corner rule）：
    /// - 一次对角移动 (dx, dy) 只有在其两个正交相邻格 (x+dx, y) 与 (x, y+dy) <b>均可行走</b>时才允许。
    /// - 因此当两面墙在对角处相邻时，不能从墙缝斜穿（不切角）；仅缺一面墙时仍可斜行。
    ///
    /// 开放集与确定性：
    /// - 开放集为二叉最小堆（按 f = g + h 排序），出堆/入堆均为 O(log n)。
    /// - 确定性的平局打破：f 相等时优先 h 更小者（更靠近终点，倾向更直的路径）；
    ///   f 与 h 仍相等时按入堆序号（插入顺序）打破，保证同一输入始终得到同一条路径。
    ///
    /// GC 友好：open/closed/came-from/g 等内部缓冲在多次调用间复用，仅在网格尺寸变化时重建。
    /// </summary>
    public sealed class AStarPathfinder
    {
        // √2：对角移动相对直行的距离系数，也用于 8 邻接启发。
        private const float Sqrt2 = 1.41421356237309515f;

        private IPathGrid m_Grid;
        private bool m_AllowDiagonal;

        // 当前缓冲对应的网格尺寸；尺寸变化时重建按格子索引的数组。
        private int m_Width;
        private int m_Height;

        // 复用缓冲（按格子线性索引 index = y * width + x）。
        private float[] m_GScore;        // 起点到该格子的已知最小 g 代价
        private int[] m_CameFrom;        // 父格子线性索引；-1 表示无父
        private int[] m_State;           // 0=未访问，1=在开放集，2=在关闭集（仅当 m_StateGen[i]==m_CurrentGen 时有效）
        private int[] m_StateGen;        // 每格最近写入 m_State 的搜索代际；用于 O(1) 重置而非每次清空整表
        private int m_CurrentGen;        // 当前搜索代际，每次 FindPath 递增
        private int[] m_OpenIndex;       // 该格子在堆数组中的位置（用于 decrease-key）；-1 表示不在堆中

        // 二叉最小堆：保存格子线性索引；排序键取自 m_HeapF / m_HeapH / m_HeapSeq。
        private int[] m_Heap;            // 堆内容（格子线性索引）
        private float[] m_HeapF;         // 与 m_Heap 平行：该格子的 f 值
        private float[] m_HeapH;         // 与 m_Heap 平行：该格子的 h 值（平局次键）
        private long[] m_HeapSeq;        // 与 m_Heap 平行：入堆序号（平局末键，保证确定性）
        private int m_HeapCount;
        private long m_Sequence;         // 单调递增的入堆序号源

        // 4 邻接 / 8 邻接的方向增量（顺序固定，配合 m_HeapSeq 给出确定性平局）。
        private static readonly int[] s_Dx4 = { 1, -1, 0, 0 };
        private static readonly int[] s_Dy4 = { 0, 0, 1, -1 };
        private static readonly int[] s_Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] s_Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// 构造一个绑定到指定网格的寻路器。
        /// </summary>
        /// <param name="grid">待搜索的网格，不能为 null。</param>
        /// <param name="allowDiagonal">是否允许对角（8 邻接）移动，默认 false（4 邻接）。</param>
        /// <exception cref="ArgumentNullException">当 grid 为 null 时抛出。</exception>
        public AStarPathfinder(IPathGrid grid, bool allowDiagonal = false)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            m_Grid = grid;
            m_AllowDiagonal = allowDiagonal;
            EnsureBuffers();
        }

        /// <summary>
        /// 是否允许对角（8 邻接）移动。
        /// </summary>
        public bool AllowDiagonal
        {
            get { return m_AllowDiagonal; }
            set { m_AllowDiagonal = value; }
        }

        /// <summary>
        /// 在网格上搜索从 start 到 goal 的最短路径，并写入 <paramref name="resultInto"/>（写入前先清空）。
        /// </summary>
        /// <param name="start">起点格子坐标。</param>
        /// <param name="goal">终点格子坐标。</param>
        /// <param name="resultInto">输出缓冲：成功时填入 start..goal（含两端）的格子序列；其它情况被清空。不能为 null。</param>
        /// <returns>
        /// <see cref="PathStatus.Found"/> 找到路径；
        /// <see cref="PathStatus.NoPath"/> 端点合法但不连通；
        /// <see cref="PathStatus.InvalidEndpoint"/> 起点或终点越界 / 不可行走。
        /// </returns>
        /// <exception cref="ArgumentNullException">当 resultInto 为 null 时抛出。</exception>
        public PathStatus FindPath(GridCoord start, GridCoord goal, List<GridCoord> resultInto)
        {
            if (resultInto == null)
            {
                throw new ArgumentNullException(nameof(resultInto));
            }

            resultInto.Clear();

            // 端点合法性：越界或不可行走 => InvalidEndpoint。
            if (!m_Grid.IsWalkable(start.X, start.Y) || !m_Grid.IsWalkable(goal.X, goal.Y))
            {
                return PathStatus.InvalidEndpoint;
            }

            EnsureBuffers();
            ResetState();

            int width = m_Width;
            int startIndex = start.Y * width + start.X;
            int goalIndex = goal.Y * width + goal.X;

            // 起点 == 终点：单格子路径，直接返回。
            if (startIndex == goalIndex)
            {
                resultInto.Add(start);
                return PathStatus.Found;
            }

            int goalX = goal.X;
            int goalY = goal.Y;

            m_GScore[startIndex] = 0f;
            m_CameFrom[startIndex] = -1;
            float startH = Heuristic(start.X, start.Y, goalX, goalY);
            HeapPush(startIndex, startH /* f = g(0) + h */, startH);
            SetState(startIndex, 1);

            int[] dx = m_AllowDiagonal ? s_Dx8 : s_Dx4;
            int[] dy = m_AllowDiagonal ? s_Dy8 : s_Dy4;
            int dirCount = dx.Length;

            while (m_HeapCount > 0)
            {
                int current = HeapPop();
                if (current == goalIndex)
                {
                    BuildPath(goalIndex, startIndex, resultInto);
                    return PathStatus.Found;
                }

                SetState(current, 2); // 移入关闭集
                int cx = current % width;
                int cy = current / width;
                float currentG = m_GScore[current];

                for (int d = 0; d < dirCount; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];

                    if (!m_Grid.IsWalkable(nx, ny))
                    {
                        continue;
                    }

                    bool diagonal = dx[d] != 0 && dy[d] != 0;
                    if (diagonal)
                    {
                        // 切角规则：对角移动要求两个正交相邻格均可行走，禁止从两墙之间斜穿。
                        if (!m_Grid.IsWalkable(cx + dx[d], cy) || !m_Grid.IsWalkable(cx, cy + dy[d]))
                        {
                            continue;
                        }
                    }

                    int neighbor = ny * width + nx;
                    int neighborState = GetState(neighbor);
                    if (neighborState == 2)
                    {
                        // 已在关闭集；启发可采纳 + 一致，无需重开。
                        continue;
                    }

                    float enterCost = m_Grid.GetCost(nx, ny);
                    float stepCost = diagonal ? enterCost * Sqrt2 : enterCost;
                    float tentativeG = currentG + stepCost;

                    if (neighborState == 1 && tentativeG >= m_GScore[neighbor])
                    {
                        // 已在开放集且已有不更优的 g，跳过。
                        continue;
                    }

                    m_CameFrom[neighbor] = current;
                    m_GScore[neighbor] = tentativeG;
                    float h = Heuristic(nx, ny, goalX, goalY);
                    float f = tentativeG + h;

                    if (neighborState == 1)
                    {
                        HeapDecrease(neighbor, f, h);
                    }
                    else
                    {
                        HeapPush(neighbor, f, h);
                        SetState(neighbor, 1);
                    }
                }
            }

            // 开放集耗尽仍未到达终点 => 不连通。
            return PathStatus.NoPath;
        }

        /// <summary>
        /// 便捷重载：内部分配一个新列表保存结果并返回（仅在找到路径时非空）。
        /// 频繁调用时优先使用 <see cref="FindPath(GridCoord, GridCoord, List{GridCoord})"/> 复用缓冲以减少 GC。
        /// </summary>
        /// <param name="start">起点格子坐标。</param>
        /// <param name="goal">终点格子坐标。</param>
        /// <returns>成功时为 start..goal 的格子序列；<see cref="PathStatus.NoPath"/> / <see cref="PathStatus.InvalidEndpoint"/> 时为空列表。</returns>
        public List<GridCoord> FindPath(GridCoord start, GridCoord goal)
        {
            var result = new List<GridCoord>();
            FindPath(start, goal, result);
            return result;
        }

        // 计算启发值（到终点的估计代价）。4 邻接用曼哈顿，8 邻接用八分距离。
        private float Heuristic(int x, int y, int goalX, int goalY)
        {
            int dx = x - goalX;
            if (dx < 0)
            {
                dx = -dx;
            }

            int dy = y - goalY;
            if (dy < 0)
            {
                dy = -dy;
            }

            if (!m_AllowDiagonal)
            {
                return dx + dy;
            }

            // 八分（octile）距离：沿对角走 min 步，再直行 (max - min) 步。
            int min = dx < dy ? dx : dy;
            int max = dx < dy ? dy : dx;
            return (max - min) + Sqrt2 * min;
        }

        // 从终点沿父指针回溯并反转，得到 start..goal 的顺序序列。
        private void BuildPath(int goalIndex, int startIndex, List<GridCoord> resultInto)
        {
            int width = m_Width;
            int node = goalIndex;
            while (node != -1)
            {
                resultInto.Add(new GridCoord(node % width, node / width));
                if (node == startIndex)
                {
                    break;
                }

                node = m_CameFrom[node];
            }

            resultInto.Reverse();
        }

        // 确保按格子索引的缓冲与当前网格尺寸一致；尺寸变化时重建。
        private void EnsureBuffers()
        {
            int width = m_Grid.Width;
            int height = m_Grid.Height;
            int count = width * height;

            if (m_GScore != null && m_Width == width && m_Height == height)
            {
                return;
            }

            m_Width = width;
            m_Height = height;

            m_GScore = new float[count];
            m_CameFrom = new int[count];
            m_State = new int[count];
            m_StateGen = new int[count];
            m_OpenIndex = new int[count];

            // 堆容量上界即格子总数（每个格子最多入堆一次有效项）。
            m_Heap = new int[count];
            m_HeapF = new float[count];
            m_HeapH = new float[count];
            m_HeapSeq = new long[count];
        }

        // 重置每次搜索的状态（不重新分配数组）。
        private void ResetState()
        {
            // O(1) 重置：递增搜索代际即可让全表 m_State 逻辑失效，无需每次清空整张网格
            // （大网格上每次 FindPath 通常只触达少量格子，整表清零是主要浪费）。
            // m_OpenIndex / m_GScore / m_CameFrom 无需清空：它们都在「写后才读」——只有 GetState==1
            // 的格子才会读取，而该状态只在本代际 HeapPush + 写入这些字段之后才会出现。
            m_CurrentGen++;
            if (m_CurrentGen == int.MaxValue)
            {
                // 代际回绕（极罕见）：清零戳记表一次后从 1 重新开始，避免与陈旧戳记冲突。
                Array.Clear(m_StateGen, 0, m_StateGen.Length);
                m_CurrentGen = 1;
            }

            m_HeapCount = 0;
            m_Sequence = 0;
        }

        // m_State 读：仅当戳记等于当前代际时有效，否则视为未访问(0)。
        private int GetState(int index)
        {
            return m_StateGen[index] == m_CurrentGen ? m_State[index] : 0;
        }

        // m_State 写：同时盖上当前代际戳记。
        private void SetState(int index, int state)
        {
            m_State[index] = state;
            m_StateGen[index] = m_CurrentGen;
        }

        // ---- 二叉最小堆 ----
        // 比较优先级：f 小者优先；f 相等取 h 小者；再相等取入堆序号小者（确定性）。

        private void HeapPush(int node, float f, float h)
        {
            int i = m_HeapCount++;
            m_Heap[i] = node;
            m_HeapF[i] = f;
            m_HeapH[i] = h;
            m_HeapSeq[i] = m_Sequence++;
            m_OpenIndex[node] = i;
            HeapSiftUp(i);
        }

        private int HeapPop()
        {
            int top = m_Heap[0];
            m_OpenIndex[top] = -1;

            int last = --m_HeapCount;
            if (last > 0)
            {
                MoveHeapSlot(last, 0);
                HeapSiftDown(0);
            }

            return top;
        }

        // 降低某个已在堆中节点的键（g 变小导致 f/h 更新），重新上浮。
        private void HeapDecrease(int node, float f, float h)
        {
            int i = m_OpenIndex[node];
            if (i < 0)
            {
                // 理论上不该发生（调用方保证在开放集中）；防御性回退为压入。
                HeapPush(node, f, h);
                return;
            }

            m_HeapF[i] = f;
            m_HeapH[i] = h;
            // 序号保持不变；新键只会更优，故只需上浮。
            HeapSiftUp(i);
        }

        private void HeapSiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (HeapLess(i, parent))
                {
                    HeapSwap(i, parent);
                    i = parent;
                }
                else
                {
                    break;
                }
            }
        }

        private void HeapSiftDown(int i)
        {
            int n = m_HeapCount;
            while (true)
            {
                int left = (i << 1) + 1;
                int right = left + 1;
                int smallest = i;

                if (left < n && HeapLess(left, smallest))
                {
                    smallest = left;
                }

                if (right < n && HeapLess(right, smallest))
                {
                    smallest = right;
                }

                if (smallest == i)
                {
                    break;
                }

                HeapSwap(i, smallest);
                i = smallest;
            }
        }

        // 堆序比较：a 是否严格优先于 b。
        private bool HeapLess(int a, int b)
        {
            float fa = m_HeapF[a];
            float fb = m_HeapF[b];
            if (fa < fb)
            {
                return true;
            }

            if (fa > fb)
            {
                return false;
            }

            float ha = m_HeapH[a];
            float hb = m_HeapH[b];
            if (ha < hb)
            {
                return true;
            }

            if (ha > hb)
            {
                return false;
            }

            // 末级平局：入堆序号小者优先，保证确定性。
            return m_HeapSeq[a] < m_HeapSeq[b];
        }

        private void HeapSwap(int a, int b)
        {
            int nodeA = m_Heap[a];
            int nodeB = m_Heap[b];

            m_Heap[a] = nodeB;
            m_Heap[b] = nodeA;

            float tf = m_HeapF[a];
            m_HeapF[a] = m_HeapF[b];
            m_HeapF[b] = tf;

            float th = m_HeapH[a];
            m_HeapH[a] = m_HeapH[b];
            m_HeapH[b] = th;

            long ts = m_HeapSeq[a];
            m_HeapSeq[a] = m_HeapSeq[b];
            m_HeapSeq[b] = ts;

            m_OpenIndex[nodeB] = a;
            m_OpenIndex[nodeA] = b;
        }

        // 把堆槽 from 的内容整体搬到 to（用于 pop 时把末尾元素提到堆顶）。
        private void MoveHeapSlot(int from, int to)
        {
            int node = m_Heap[from];
            m_Heap[to] = node;
            m_HeapF[to] = m_HeapF[from];
            m_HeapH[to] = m_HeapH[from];
            m_HeapSeq[to] = m_HeapSeq[from];
            m_OpenIndex[node] = to;
        }
    }
}
