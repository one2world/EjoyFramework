//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace EjoyFramework.GamePlay.Leaderboard
{
    /// <summary>
    /// 单个排行榜的本地数据模型。纯逻辑、与引擎无关，可独立单元测试。
    /// 持有一份不可变快照（按名次升序存储），并维护“我”的记录与一个 PlayerId 索引，
    /// 以便 O(1) 命中查询 <see cref="TryGet"/> 与定位“我”的邻居窗口 <see cref="GetAroundSelf"/>。
    /// 数据由游戏侧通过 HTTP 拉取后写入；该类型不负责任何网络请求。
    /// </summary>
    public sealed class LeaderboardBoard
    {
        private readonly string m_Id;

        // 按名次升序存储的当前快照；以只读列表对外暴露，读路径零分配。
        private List<LeaderboardEntry> m_Entries;

        // PlayerId -> 在 m_Entries 中的下标，用于 TryGet 与 GetAroundSelf 的 O(1) 定位。
        private readonly Dictionary<string, int> m_PlayerIndex;

        // “我”的记录；null 表示未设置。
        private LeaderboardEntry? m_Self;

        /// <summary>
        /// 构造一个空排行榜。
        /// </summary>
        /// <param name="id">排行榜标识（如赛季、玩法维度）。</param>
        public LeaderboardBoard(string id)
        {
            m_Id = id;
            m_Entries = new List<LeaderboardEntry>();
            m_PlayerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            m_Self = null;
        }

        /// <summary>
        /// 排行榜标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 当前快照中的记录数。读路径零分配。
        /// </summary>
        public int Count
        {
            get { return m_Entries.Count; }
        }

        /// <summary>
        /// 当前快照（按名次升序）。只读视图，外部不可修改。
        /// </summary>
        public IReadOnlyList<LeaderboardEntry> Entries
        {
            get { return m_Entries; }
        }

        /// <summary>
        /// “我”的记录；未设置时为 null。
        /// </summary>
        public LeaderboardEntry? Self
        {
            get { return m_Self; }
        }

        /// <summary>
        /// 用一批新记录整体替换当前快照。内部按名次升序存储并重建 PlayerId 索引；
        /// 同一 PlayerId 多次出现时，以后出现者覆盖前者的索引。完成后触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="entries">新的记录集合；为 null 时视为清空。</param>
        public void SetEntries(IEnumerable<LeaderboardEntry> entries)
        {
            List<LeaderboardEntry> next = new List<LeaderboardEntry>();
            if (entries != null)
            {
                foreach (LeaderboardEntry entry in entries)
                {
                    next.Add(entry);
                }
            }

            // 按名次升序排序，确保 Entries 始终有序。必须用「稳定」排序：List.Sort 是非稳定的，
            // 会打乱并列名次（Ranking.Rank 会刻意产生并列）的相对顺序，破坏 GetAroundSelf 窗口与展示顺序
            // 的确定性。OrderBy 为稳定排序，保证并列名次保持输入顺序。
            next = next.OrderBy(e => e.Rank).ToList();

            m_Entries = next;

            m_PlayerIndex.Clear();
            for (int i = 0; i < next.Count; i++)
            {
                string playerId = next[i].PlayerId;
                if (!string.IsNullOrEmpty(playerId))
                {
                    m_PlayerIndex[playerId] = i;
                }
            }

            RaiseChanged();
        }

        /// <summary>
        /// 设置“我”的记录，并触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="self">“我”的记录。</param>
        public void SetSelf(LeaderboardEntry self)
        {
            m_Self = self;
            RaiseChanged();
        }

        /// <summary>
        /// 按 PlayerId 查询记录。命中返回 true。读路径零分配。
        /// </summary>
        /// <param name="playerId">玩家唯一标识。</param>
        /// <param name="entry">命中时输出对应记录；未命中为默认值。</param>
        /// <returns>命中返回 true，否则 false。</returns>
        public bool TryGet(string playerId, out LeaderboardEntry entry)
        {
            if (!string.IsNullOrEmpty(playerId) && m_PlayerIndex.TryGetValue(playerId, out int index))
            {
                entry = m_Entries[index];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>
        /// 取“我”周围的连续窗口：以 Self 的 PlayerId 在当前快照中定位，返回下标
        /// [pos-radius .. pos+radius] 的连续片段（在边界处自动裁剪）。
        /// 当 Self 为 null、或 Self 不在当前快照中时，返回空列表。
        /// </summary>
        /// <param name="radius">半径；小于 0 时按 0 处理（仅返回“我”自身那一条）。</param>
        /// <returns>“我”周围的连续窗口（按名次升序）；无法定位时为空列表。</returns>
        public IReadOnlyList<LeaderboardEntry> GetAroundSelf(int radius)
        {
            if (m_Self == null)
            {
                return Array.Empty<LeaderboardEntry>();
            }

            string selfId = m_Self.Value.PlayerId;
            if (string.IsNullOrEmpty(selfId) || !m_PlayerIndex.TryGetValue(selfId, out int pos))
            {
                return Array.Empty<LeaderboardEntry>();
            }

            int r = radius < 0 ? 0 : radius;

            int start = pos - r;
            if (start < 0)
            {
                start = 0;
            }

            int end = pos + r;
            if (end > m_Entries.Count - 1)
            {
                end = m_Entries.Count - 1;
            }

            List<LeaderboardEntry> window = new List<LeaderboardEntry>(end - start + 1);
            for (int i = start; i <= end; i++)
            {
                window.Add(m_Entries[i]);
            }

            return window;
        }

        /// <summary>
        /// 触发 <see cref="OnChanged"/>。先快照委托引用，避免“迭代中订阅者增删自身”的风险。
        /// </summary>
        private void RaiseChanged()
        {
            Action<LeaderboardBoard> handler = OnChanged;
            handler?.Invoke(this);
        }

        /// <summary>
        /// 快照（<see cref="SetEntries"/>）或“我”的记录（<see cref="SetSelf"/>）变更时触发。参数为本榜单实例。
        /// </summary>
        public event Action<LeaderboardBoard> OnChanged;
    }
}
