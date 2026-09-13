//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Spatial;

namespace EjoyFramework.GamePlay.Netcode.Aoi
{
    /// <summary>
    /// 兴趣管理追踪器：基于 <see cref="AoiGrid"/>，按「观察者」维护其当前视野内的实体集合，
    /// 并在观察者或实体移动后计算进入（enter）/ 离开（leave）的增量差分，供上层做差异化同步（如只对新进入视野的实体补发快照）。
    ///
    /// 设计要点：
    /// - 每个观察者保存上一次计算得到的视野集合（<see cref="HashSet{T}"/>）。
    /// - <see cref="UpdateObserver"/> 以观察者<b>在网格中的当前位置</b>为中心，按 <see cref="ViewRadius"/> 精确半径查询视野，
    ///   再与上次视野做差分：entered = 现有且此前没有；left = 此前有而现在没有；随后用新视野覆盖存储。<b>观察者自身被排除在视野之外。</b>
    /// - 视野查询直接复用 <see cref="AoiGrid.QueryRadius(float,float,float,List{int})"/> 的 non-alloc 重载，
    ///   并复用内部临时列表；视野集合采用每观察者「双缓冲」<see cref="HashSet{T}"/>（current/scratch 互换），
    ///   稳态下不再每次调用分配（每个观察者首两帧各有一次预热分配，之后零分配）。
    ///
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class AoiTracker
    {
        /// <summary>
        /// 底层空间网格，提供位置查询。
        /// </summary>
        private readonly AoiGrid m_Grid;

        /// <summary>
        /// 观察者 id -> 其上一次计算得到的视野集合（实体 id）。
        /// </summary>
        private readonly Dictionary<int, HashSet<int>> m_Views;

        /// <summary>
        /// 观察者 id -> 备用视野集合（双缓冲）。<see cref="UpdateObserver"/> 把本次视野构建到这里，
        /// 与 <see cref="m_Views"/> 中的上次视野做差分后两者互换：本次视野成为新的 m_Views，
        /// 旧视野清空后留作下次的 scratch。借此避免每次调用分配新的 <see cref="HashSet{T}"/>。
        /// </summary>
        private readonly Dictionary<int, HashSet<int>> m_ViewScratch;

        /// <summary>
        /// 复用的临时查询缓冲，避免每次 <see cref="UpdateObserver"/> 分配。
        /// </summary>
        private readonly List<int> m_QueryBuffer;

        /// <summary>
        /// 视野半径（世界单位）。
        /// </summary>
        private float m_ViewRadius;

        /// <summary>
        /// 构造追踪器。
        /// </summary>
        /// <param name="grid">底层空间网格，不可为 null。</param>
        /// <param name="viewRadius">视野半径（世界单位），必须为正。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="grid"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="viewRadius"/> 不是正数（含 NaN）时抛出。</exception>
        public AoiTracker(AoiGrid grid, float viewRadius)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (!(viewRadius > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(viewRadius), viewRadius, "视野半径必须为正数。");
            }

            m_Grid = grid;
            m_ViewRadius = viewRadius;
            m_Views = new Dictionary<int, HashSet<int>>();
            m_ViewScratch = new Dictionary<int, HashSet<int>>();
            m_QueryBuffer = new List<int>();
        }

        /// <summary>
        /// 视野半径（世界单位）。可在运行时调整；新值会在下一次 <see cref="UpdateObserver"/> 时生效。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">当设置的值不是正数（含 NaN）时抛出。</exception>
        public float ViewRadius
        {
            get { return m_ViewRadius; }
            set
            {
                if (!(value > 0f))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "视野半径必须为正数。");
                }

                m_ViewRadius = value;
            }
        }

        /// <summary>
        /// 重新计算指定观察者的视野集合，并产出相对上次视野的增量差分。
        ///
        /// 行为：
        /// - 以观察者在网格中的<b>当前位置</b>为中心，按 <see cref="ViewRadius"/> 精确半径查询视野，并排除观察者自身。
        /// - <paramref name="enteredInto"/> = 本次在视野中但上次不在的实体（进入视野）。
        /// - <paramref name="leftInto"/> = 上次在视野中但本次不在的实体（离开视野）。
        /// - 两个输出列表均在<b>方法开始时被清空</b>，调用方无需预先清理。
        /// - 计算完成后，用本次视野覆盖该观察者存储的视野。
        /// - 若观察者尚未加入网格（无位置），则视野为空：此时 <paramref name="leftInto"/> 含上次视野的全部实体，<paramref name="enteredInto"/> 为空。
        /// </summary>
        /// <param name="observerId">观察者实体 id。</param>
        /// <param name="enteredInto">装载「进入视野」实体的列表，方法开始时被清空；不可为 null。</param>
        /// <param name="leftInto">装载「离开视野」实体的列表，方法开始时被清空；不可为 null。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="enteredInto"/> 或 <paramref name="leftInto"/> 为 null 时抛出。</exception>
        public void UpdateObserver(int observerId, List<int> enteredInto, List<int> leftInto)
        {
            if (enteredInto == null)
            {
                throw new ArgumentNullException(nameof(enteredInto));
            }

            if (leftInto == null)
            {
                throw new ArgumentNullException(nameof(leftInto));
            }

            enteredInto.Clear();
            leftInto.Clear();

            // 取上次视野（可能不存在）。
            m_Views.TryGetValue(observerId, out HashSet<int> previous);

            // 取/建本次视野所用的 scratch 集合（双缓冲，避免每次分配）；复用前清空。
            // 注意：首帧后 scratch 槽可能登记为 null（见末尾互换说明），故需对 null 也走新建分支。
            if (!m_ViewScratch.TryGetValue(observerId, out HashSet<int> current) || current == null)
            {
                current = new HashSet<int>();
            }
            else
            {
                current.Clear();
            }

            // 计算本次视野：观察者必须在网格中才有位置；否则视野为空。
            if (m_Grid.TryGetPosition(observerId, out float ox, out float oy))
            {
                m_Grid.QueryRadius(ox, oy, m_ViewRadius, m_QueryBuffer);
                for (int i = 0; i < m_QueryBuffer.Count; i++)
                {
                    int id = m_QueryBuffer[i];
                    if (id == observerId)
                    {
                        // 排除观察者自身。
                        continue;
                    }

                    current.Add(id);
                }
            }

            // entered = current - previous。
            foreach (int id in current)
            {
                if (previous == null || !previous.Contains(id))
                {
                    enteredInto.Add(id);
                }
            }

            // left = previous - current。
            if (previous != null)
            {
                foreach (int id in previous)
                {
                    if (!current.Contains(id))
                    {
                        leftInto.Add(id);
                    }
                }
            }

            // 双缓冲互换：本次视野（current）成为新的存储视野；旧视野（previous）留作下次的 scratch。
            // 首次（previous 为 null）时，scratch 槽暂置空，下次调用会按需新建一次（仅首帧分配）。
            m_Views[observerId] = current;
            m_ViewScratch[observerId] = previous;
        }

        /// <summary>
        /// 获取观察者当前存储的视野集合（只读视图）。观察者从未被 <see cref="UpdateObserver"/> 过时返回空集合。
        /// </summary>
        /// <param name="observerId">观察者实体 id。</param>
        /// <returns>视野内实体 id 的只读集合。</returns>
        public IReadOnlyCollection<int> GetView(int observerId)
        {
            if (m_Views.TryGetValue(observerId, out HashSet<int> view))
            {
                return view;
            }

            return Array.Empty<int>();
        }

        /// <summary>
        /// 移除观察者及其存储的视野。观察者不存在时为无操作（no-op）。
        /// </summary>
        /// <param name="observerId">观察者实体 id。</param>
        public void RemoveObserver(int observerId)
        {
            m_Views.Remove(observerId);
            m_ViewScratch.Remove(observerId);
        }

        /// <summary>
        /// 清空全部观察者及其视野（不影响底层网格）。
        /// </summary>
        public void Clear()
        {
            m_Views.Clear();
            m_ViewScratch.Clear();
        }
    }
}
