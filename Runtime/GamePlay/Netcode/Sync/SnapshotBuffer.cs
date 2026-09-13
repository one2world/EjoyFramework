//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Netcode.Sync
{
    /// <summary>
    /// 客户端侧的快照抖动缓冲（jitter buffer）。
    /// <para>
    /// 内部按 <see cref="WorldSnapshot.ServerTimeSec"/> 升序保存最近收到的若干快照，
    /// 容量有界：超出 <see cref="Capacity"/> 时丢弃最旧的快照。
    /// 该缓冲吸收网络抖动，为 <see cref="SnapshotInterpolator"/> 提供可插值的连续历史。
    /// </para>
    /// <para>
    /// 入队策略（<see cref="Add"/>）：
    /// 乱序但仍新于当前最旧快照的会被插入到正确位置；
    /// 重复的 <see cref="WorldSnapshot.ServerTimeSec"/> 会被忽略（保留先到者）；
    /// 当缓冲已满时，早于当前最旧快照（即比将被淘汰者更旧）的快照会被直接忽略，
    /// 因为它落在缓冲覆盖的时间窗口之外，已无插值价值。
    /// </para>
    /// </summary>
    public sealed class SnapshotBuffer
    {
        private readonly int m_Capacity;
        private readonly List<WorldSnapshot> m_Snapshots;

        /// <summary>
        /// 构造抖动缓冲。
        /// </summary>
        /// <param name="capacity">最大保留的快照数量，必须大于 0，默认 32。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 小于等于 0 时抛出。</exception>
        public SnapshotBuffer(int capacity = 32)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须为正。");
            }

            m_Capacity = capacity;
            m_Snapshots = new List<WorldSnapshot>(capacity);
        }

        /// <summary>
        /// 当前缓冲中的快照数量。
        /// </summary>
        public int Count
        {
            get { return m_Snapshots.Count; }
        }

        /// <summary>
        /// 缓冲容量上限。
        /// </summary>
        public int Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>
        /// 时间最新（<see cref="WorldSnapshot.ServerTimeSec"/> 最大）的快照；缓冲为空时返回 null。
        /// </summary>
        public WorldSnapshot Latest
        {
            get { return m_Snapshots.Count > 0 ? m_Snapshots[m_Snapshots.Count - 1] : null; }
        }

        /// <summary>
        /// 时间最旧（<see cref="WorldSnapshot.ServerTimeSec"/> 最小）的快照；缓冲为空时返回 null。
        /// </summary>
        public WorldSnapshot Oldest
        {
            get { return m_Snapshots.Count > 0 ? m_Snapshots[0] : null; }
        }

        /// <summary>
        /// 加入一个快照，保持按 <see cref="WorldSnapshot.ServerTimeSec"/> 升序排列。
        /// <para>
        /// 与同时间戳的快照重复则忽略；缓冲已满时，比当前最旧快照还旧者忽略；
        /// 否则插入正确位置并在超容时淘汰最旧快照。
        /// </para>
        /// </summary>
        /// <param name="snapshot">待加入的世界快照，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> 为 null 时抛出。</exception>
        public void Add(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            int count = m_Snapshots.Count;

            // 缓冲已满且新快照不比最旧者更新 —— 落在窗口之外，无插值价值，忽略。
            if (count >= m_Capacity && snapshot.ServerTimeSec <= m_Snapshots[0].ServerTimeSec)
            {
                return;
            }

            int insertIndex = FindInsertIndex(snapshot.ServerTimeSec);

            // 时间戳重复（紧邻位置已有同一 ServerTimeSec）—— 忽略，保留先到者。
            if (insertIndex < count && m_Snapshots[insertIndex].ServerTimeSec == snapshot.ServerTimeSec)
            {
                return;
            }
            if (insertIndex > 0 && m_Snapshots[insertIndex - 1].ServerTimeSec == snapshot.ServerTimeSec)
            {
                return;
            }

            m_Snapshots.Insert(insertIndex, snapshot);

            // 超容则从头部淘汰最旧者。
            if (m_Snapshots.Count > m_Capacity)
            {
                m_Snapshots.RemoveAt(0);
            }
        }

        /// <summary>
        /// 返回包夹 <paramref name="renderTimeSec"/> 的两个相邻快照用于插值：
        /// 满足 <c>from.ServerTimeSec &lt;= renderTimeSec &lt;= to.ServerTimeSec</c>。
        /// <para>
        /// 边界处理（均钳制，不做外推）：
        /// renderTime 早于最旧快照 → 返回 (Oldest, Oldest, t=0)；
        /// renderTime 晚于最新快照 → 返回 (Latest, Latest, t=1)；
        /// 缓冲仅有一个快照 → 返回该快照自身配对，t 依在窗口左右而取 0 或 1。
        /// 仅当缓冲为空时返回 false。
        /// </para>
        /// </summary>
        /// <param name="renderTimeSec">目标渲染时间（服务器时间轴上的秒）。</param>
        /// <param name="from">输出：较早一侧快照。</param>
        /// <param name="to">输出：较晚一侧快照。</param>
        /// <param name="t">输出：from→to 之间的插值因子，钳制于 [0,1]。</param>
        /// <returns>缓冲非空返回 true；为空返回 false。</returns>
        public bool GetInterpolationPair(double renderTimeSec, out WorldSnapshot from, out WorldSnapshot to, out float t)
        {
            int count = m_Snapshots.Count;
            if (count == 0)
            {
                from = null;
                to = null;
                t = 0f;
                return false;
            }

            // 早于最旧 —— 钳制到最旧。
            if (renderTimeSec <= m_Snapshots[0].ServerTimeSec)
            {
                from = m_Snapshots[0];
                to = m_Snapshots[0];
                t = 0f;
                return true;
            }

            // 晚于最新 —— 钳制到最新（不外推）。
            WorldSnapshot newest = m_Snapshots[count - 1];
            if (renderTimeSec >= newest.ServerTimeSec)
            {
                from = newest;
                to = newest;
                t = 1f;
                return true;
            }

            // 在窗口内：寻找满足 from <= renderTime < to 的相邻区间。
            for (int i = 0; i < count - 1; i++)
            {
                WorldSnapshot lo = m_Snapshots[i];
                WorldSnapshot hi = m_Snapshots[i + 1];
                if (renderTimeSec >= lo.ServerTimeSec && renderTimeSec <= hi.ServerTimeSec)
                {
                    from = lo;
                    to = hi;
                    double span = hi.ServerTimeSec - lo.ServerTimeSec;
                    t = span > 0d ? GameMath.Clamp01((float)((renderTimeSec - lo.ServerTimeSec) / span)) : 0f;
                    return true;
                }
            }

            // 理论不可达（前面的边界判断已覆盖），兜底返回最新。
            from = newest;
            to = newest;
            t = 1f;
            return true;
        }

        /// <summary>
        /// 清空缓冲。
        /// </summary>
        public void Clear()
        {
            m_Snapshots.Clear();
        }

        /// <summary>
        /// 二分查找升序插入位置（首个 ServerTimeSec &gt;= 目标的索引）。
        /// </summary>
        private int FindInsertIndex(double serverTimeSec)
        {
            int low = 0;
            int high = m_Snapshots.Count;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (m_Snapshots[mid].ServerTimeSec < serverTimeSec)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }
    }
}
