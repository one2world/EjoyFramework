//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Leaderboard
{
    /// <summary>
    /// 客户端本地名次计算辅助。纯逻辑、与引擎无关，可独立单元测试。
    /// 采用“标准竞赛排名”（standard competition ranking）：分数相同的记录共享同一名次，
    /// 并列之后的名次会跳过相应的位置（如分数 [100,90,90,80] 降序 => 名次 1,2,2,4）。
    /// </summary>
    public static class Ranking
    {
        /// <summary>
        /// 根据原始数据行计算名次，返回按名次升序排列的不可变记录列表。
        /// 排序稳定：分数相同的记录保持输入先后顺序（输入顺序即并列内部的展示顺序）。
        /// </summary>
        /// <param name="rows">原始数据行：玩家 Id、显示名、分数。</param>
        /// <param name="descending">true => 分数越高名次越靠前（第 1 名）；false => 分数越低名次越靠前。</param>
        /// <returns>按名次升序排列的记录列表；rows 为 null 时返回空列表。</returns>
        public static IReadOnlyList<LeaderboardEntry> Rank(
            IEnumerable<(string playerId, string displayName, long score)> rows,
            bool descending = true)
        {
            if (rows == null)
            {
                return Array.Empty<LeaderboardEntry>();
            }

            // 先物化为带原始下标的列表，原始下标用于在分数相同时维持稳定（输入）顺序。
            List<(string playerId, string displayName, long score, int index)> buffer =
                new List<(string, string, long, int)>();
            int order = 0;
            foreach ((string playerId, string displayName, long score) row in rows)
            {
                buffer.Add((row.playerId, row.displayName, row.score, order));
                order++;
            }

            if (buffer.Count == 0)
            {
                return Array.Empty<LeaderboardEntry>();
            }

            // 稳定排序：主键为分数（按方向比较），次键为原始下标（升序）以保证并列内部稳定。
            buffer.Sort((a, b) =>
            {
                int scoreCompare = descending ? b.score.CompareTo(a.score) : a.score.CompareTo(b.score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                return a.index.CompareTo(b.index);
            });

            // 赋名次：与前一条分数相同则沿用其名次；否则名次 = 当前位置 + 1（位置从 0 起，故并列后产生跳号）。
            List<LeaderboardEntry> result = new List<LeaderboardEntry>(buffer.Count);
            int currentRank = 1;
            long previousScore = 0;
            bool hasPrevious = false;
            for (int i = 0; i < buffer.Count; i++)
            {
                (string playerId, string displayName, long score, int index) row = buffer[i];

                if (hasPrevious && row.score == previousScore)
                {
                    // 与前一条并列，沿用其名次。
                }
                else
                {
                    // 标准竞赛排名：名次直接取当前位置（1 起），从而在并列后跳号。
                    currentRank = i + 1;
                }

                result.Add(new LeaderboardEntry(currentRank, row.playerId, row.displayName, row.score));
                previousScore = row.score;
                hasPrevious = true;
            }

            return result;
        }
    }
}
