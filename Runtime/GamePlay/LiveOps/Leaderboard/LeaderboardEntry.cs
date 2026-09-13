//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Leaderboard
{
    /// <summary>
    /// 排行榜单条记录。纯数据、不可变值类型，与引擎无关。
    /// 名次 <see cref="Rank"/> 一般由服务器下发，或由 <see cref="Ranking.Rank"/> 本地计算得到。
    /// </summary>
    public readonly struct LeaderboardEntry
    {
        private readonly int m_Rank;
        private readonly string m_PlayerId;
        private readonly string m_DisplayName;
        private readonly long m_Score;

        /// <summary>
        /// 构造一条排行榜记录。
        /// </summary>
        /// <param name="rank">名次（从 1 开始；并列时共享同一名次）。</param>
        /// <param name="playerId">玩家唯一标识。</param>
        /// <param name="displayName">玩家显示名。</param>
        /// <param name="score">分数。</param>
        public LeaderboardEntry(int rank, string playerId, string displayName, long score)
        {
            m_Rank = rank;
            m_PlayerId = playerId;
            m_DisplayName = displayName;
            m_Score = score;
        }

        /// <summary>
        /// 名次，从 1 开始；并列时多条记录共享同一名次。
        /// </summary>
        public int Rank
        {
            get { return m_Rank; }
        }

        /// <summary>
        /// 玩家唯一标识，用于定位与去重。
        /// </summary>
        public string PlayerId
        {
            get { return m_PlayerId; }
        }

        /// <summary>
        /// 玩家显示名，仅用于展示。
        /// </summary>
        public string DisplayName
        {
            get { return m_DisplayName; }
        }

        /// <summary>
        /// 分数。
        /// </summary>
        public long Score
        {
            get { return m_Score; }
        }
    }
}
