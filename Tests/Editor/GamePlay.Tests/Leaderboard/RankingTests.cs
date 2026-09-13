//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Leaderboard;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Leaderboard
{
    /// <summary>
    /// 针对 <see cref="Ranking.Rank"/> 标准竞赛排名（并列共享名次、并列后跳号）、升降序与稳定性的单元测试。
    /// </summary>
    [TestFixture]
    public class RankingTests
    {
        [Test]
        public void Rank_Descending_StandardCompetition_TiesShareRankAndSkip()
        {
            // 分数 [100,90,90,80] 降序 => 名次 1,2,2,4。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p1", "A", 100),
                ("p2", "B", 90),
                ("p3", "C", 90),
                ("p4", "D", 80),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows);

            Assert.AreEqual(4, ranked.Count);
            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(2, ranked[1].Rank);
            Assert.AreEqual(2, ranked[2].Rank);
            Assert.AreEqual(4, ranked[3].Rank);

            Assert.AreEqual("p1", ranked[0].PlayerId);
            Assert.AreEqual("p4", ranked[3].PlayerId);
        }

        [Test]
        public void Rank_Descending_SortsHighScoreFirst()
        {
            // 乱序输入，验证最终按分数降序、名次自 1 起。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p2", "B", 50),
                ("p1", "A", 100),
                ("p3", "C", 10),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows, descending: true);

            Assert.AreEqual("p1", ranked[0].PlayerId);
            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual("p2", ranked[1].PlayerId);
            Assert.AreEqual(2, ranked[1].Rank);
            Assert.AreEqual("p3", ranked[2].PlayerId);
            Assert.AreEqual(3, ranked[2].Rank);
        }

        [Test]
        public void Rank_Ascending_LowScoreFirst_WithTies()
        {
            // 升序：分数越低名次越靠前。分数 [10,20,20,30] 升序 => 名次 1,2,2,4。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p4", "D", 30),
                ("p1", "A", 10),
                ("p2", "B", 20),
                ("p3", "C", 20),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows, descending: false);

            Assert.AreEqual("p1", ranked[0].PlayerId);
            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(2, ranked[1].Rank);
            Assert.AreEqual(2, ranked[2].Rank);
            Assert.AreEqual(4, ranked[3].Rank);
            Assert.AreEqual("p4", ranked[3].PlayerId);
        }

        [Test]
        public void Rank_TieOrder_IsStable_PreservesInputOrder()
        {
            // 三个并列分数，验证并列内部保持输入先后顺序（p_a, p_b, p_c）。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p_a", "A", 50),
                ("p_b", "B", 50),
                ("p_c", "C", 50),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows);

            Assert.AreEqual("p_a", ranked[0].PlayerId);
            Assert.AreEqual("p_b", ranked[1].PlayerId);
            Assert.AreEqual("p_c", ranked[2].PlayerId);
            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(1, ranked[1].Rank);
            Assert.AreEqual(1, ranked[2].Rank);
        }

        [Test]
        public void Rank_LeadingTie_ThenSkips()
        {
            // 起手并列：分数 [100,100,90] 降序 => 名次 1,1,3。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p1", "A", 100),
                ("p2", "B", 100),
                ("p3", "C", 90),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows);

            Assert.AreEqual(1, ranked[0].Rank);
            Assert.AreEqual(1, ranked[1].Rank);
            Assert.AreEqual(3, ranked[2].Rank);
        }

        [Test]
        public void Rank_NullRows_ReturnsEmpty()
        {
            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(null);

            Assert.IsNotNull(ranked);
            Assert.AreEqual(0, ranked.Count);
        }

        [Test]
        public void Rank_EmptyRows_ReturnsEmpty()
        {
            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(new List<(string, string, long)>());

            Assert.IsNotNull(ranked);
            Assert.AreEqual(0, ranked.Count);
        }

        [Test]
        public void Rank_PreservesScoreAndDisplayName()
        {
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p1", "Alice", 100),
                ("p2", "Bob", 90),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows);

            Assert.AreEqual("Alice", ranked[0].DisplayName);
            Assert.AreEqual(100, ranked[0].Score);
            Assert.AreEqual("Bob", ranked[1].DisplayName);
            Assert.AreEqual(90, ranked[1].Score);
        }
    }
}
