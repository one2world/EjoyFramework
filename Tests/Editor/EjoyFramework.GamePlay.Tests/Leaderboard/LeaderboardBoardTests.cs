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
    /// 针对 <see cref="LeaderboardBoard"/> 快照写入与排序、按 Id 查询、Self 设置、
    /// 邻居窗口裁剪以及变更事件触发的单元测试。
    /// </summary>
    [TestFixture]
    public class LeaderboardBoardTests
    {
        // 构造一份 5 人榜单（名次 1..5），故意乱序传入以验证内部按名次升序存储。
        private static List<LeaderboardEntry> FivePlayersUnsorted()
        {
            return new List<LeaderboardEntry>
            {
                new LeaderboardEntry(3, "p3", "C", 80),
                new LeaderboardEntry(1, "p1", "A", 100),
                new LeaderboardEntry(5, "p5", "E", 60),
                new LeaderboardEntry(2, "p2", "B", 90),
                new LeaderboardEntry(4, "p4", "D", 70),
            };
        }

        [Test]
        public void Constructor_StoresId_AndStartsEmpty()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");

            Assert.AreEqual("weekly", board.Id);
            Assert.AreEqual(0, board.Count);
            Assert.IsNull(board.Self);
            Assert.AreEqual(0, board.Entries.Count);
        }

        [Test]
        public void SetEntries_StoresSortedByRankAscending()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");

            board.SetEntries(FivePlayersUnsorted());

            Assert.AreEqual(5, board.Count);
            for (int i = 0; i < board.Entries.Count; i++)
            {
                Assert.AreEqual(i + 1, board.Entries[i].Rank, "Entries 必须按名次升序存储");
            }

            Assert.AreEqual("p1", board.Entries[0].PlayerId);
            Assert.AreEqual("p5", board.Entries[4].PlayerId);
        }

        [Test]
        public void SetEntries_TiedRanks_PreserveInputOrder_Stable()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");

            // 三个并列名次 2（输入顺序 a,b,c）：稳定排序后必须保持 a,b,c 不被打乱。
            board.SetEntries(new List<LeaderboardEntry>
            {
                new LeaderboardEntry(1, "first", "F", 100),
                new LeaderboardEntry(2, "tie_a", "A", 90),
                new LeaderboardEntry(2, "tie_b", "B", 90),
                new LeaderboardEntry(2, "tie_c", "C", 90),
                new LeaderboardEntry(3, "last", "L", 80),
            });

            Assert.AreEqual("first", board.Entries[0].PlayerId);
            Assert.AreEqual("tie_a", board.Entries[1].PlayerId);
            Assert.AreEqual("tie_b", board.Entries[2].PlayerId);
            Assert.AreEqual("tie_c", board.Entries[3].PlayerId);
            Assert.AreEqual("last", board.Entries[4].PlayerId);
        }

        [Test]
        public void SetEntries_Null_ClearsSnapshot()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            Assert.AreEqual(5, board.Count);

            board.SetEntries(null);

            Assert.AreEqual(0, board.Count);
            Assert.AreEqual(0, board.Entries.Count);
        }

        [Test]
        public void SetEntries_Replaces_PreviousSnapshot()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());

            board.SetEntries(new List<LeaderboardEntry>
            {
                new LeaderboardEntry(1, "x1", "X", 10),
            });

            Assert.AreEqual(1, board.Count);
            Assert.IsTrue(board.TryGet("x1", out _));
            Assert.IsFalse(board.TryGet("p1", out _), "旧快照应被整体替换");
        }

        [Test]
        public void TryGet_Hit_ReturnsEntry()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());

            bool found = board.TryGet("p3", out LeaderboardEntry entry);

            Assert.IsTrue(found);
            Assert.AreEqual("p3", entry.PlayerId);
            Assert.AreEqual(3, entry.Rank);
            Assert.AreEqual(80, entry.Score);
        }

        [Test]
        public void TryGet_Miss_ReturnsFalseAndDefault()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());

            bool found = board.TryGet("does-not-exist", out LeaderboardEntry entry);

            Assert.IsFalse(found);
            Assert.AreEqual(default(LeaderboardEntry).PlayerId, entry.PlayerId);
        }

        [Test]
        public void TryGet_NullOrEmptyId_ReturnsFalse()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());

            Assert.IsFalse(board.TryGet(null, out _));
            Assert.IsFalse(board.TryGet(string.Empty, out _));
        }

        [Test]
        public void SetSelf_StoresSelf()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            LeaderboardEntry self = new LeaderboardEntry(2, "p2", "B", 90);

            board.SetSelf(self);

            Assert.IsNotNull(board.Self);
            Assert.AreEqual("p2", board.Self.Value.PlayerId);
            Assert.AreEqual(2, board.Self.Value.Rank);
        }

        [Test]
        public void GetAroundSelf_Middle_ReturnsSymmetricWindow()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(3, "p3", "C", 80));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(1);

            // p3 居中，radius=1 => p2, p3, p4。
            Assert.AreEqual(3, window.Count);
            Assert.AreEqual("p2", window[0].PlayerId);
            Assert.AreEqual("p3", window[1].PlayerId);
            Assert.AreEqual("p4", window[2].PlayerId);
        }

        [Test]
        public void GetAroundSelf_ClampsAtTop()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(1, "p1", "A", 100));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(2);

            // p1 在顶部，上方无人，radius=2 => p1, p2, p3。
            Assert.AreEqual(3, window.Count);
            Assert.AreEqual("p1", window[0].PlayerId);
            Assert.AreEqual("p2", window[1].PlayerId);
            Assert.AreEqual("p3", window[2].PlayerId);
        }

        [Test]
        public void GetAroundSelf_ClampsAtBottom()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(5, "p5", "E", 60));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(2);

            // p5 在底部，下方无人，radius=2 => p3, p4, p5。
            Assert.AreEqual(3, window.Count);
            Assert.AreEqual("p3", window[0].PlayerId);
            Assert.AreEqual("p4", window[1].PlayerId);
            Assert.AreEqual("p5", window[2].PlayerId);
        }

        [Test]
        public void GetAroundSelf_RadiusZero_ReturnsOnlySelf()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(3, "p3", "C", 80));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(0);

            Assert.AreEqual(1, window.Count);
            Assert.AreEqual("p3", window[0].PlayerId);
        }

        [Test]
        public void GetAroundSelf_NegativeRadius_TreatedAsZero()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(3, "p3", "C", 80));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(-5);

            Assert.AreEqual(1, window.Count);
            Assert.AreEqual("p3", window[0].PlayerId);
        }

        [Test]
        public void GetAroundSelf_SelfNull_ReturnsEmpty()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(2);

            Assert.IsNotNull(window);
            Assert.AreEqual(0, window.Count);
        }

        [Test]
        public void GetAroundSelf_SelfNotInEntries_ReturnsEmpty()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(99, "ghost", "Ghost", 1));

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(2);

            Assert.IsNotNull(window);
            Assert.AreEqual(0, window.Count);
        }

        [Test]
        public void OnChanged_FiresOnSetEntries()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            int fired = 0;
            LeaderboardBoard fromArg = null;
            board.OnChanged += b =>
            {
                fired++;
                fromArg = b;
            };

            board.SetEntries(FivePlayersUnsorted());

            Assert.AreEqual(1, fired);
            Assert.AreSame(board, fromArg);
        }

        [Test]
        public void OnChanged_FiresOnSetSelf()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            int fired = 0;
            board.OnChanged += b => fired++;

            board.SetSelf(new LeaderboardEntry(1, "p1", "A", 100));

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void OnChanged_FiresOncePerMutation()
        {
            LeaderboardBoard board = new LeaderboardBoard("weekly");
            int fired = 0;
            board.OnChanged += b => fired++;

            board.SetEntries(FivePlayersUnsorted());
            board.SetSelf(new LeaderboardEntry(1, "p1", "A", 100));

            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Integration_RankThenSetEntries_AndWindowAroundSelf()
        {
            // 端到端：本地计算名次 -> 写入榜单 -> 取“我”的邻居窗口。
            List<(string, string, long)> rows = new List<(string, string, long)>
            {
                ("p1", "A", 100),
                ("p2", "B", 90),
                ("p3", "C", 80),
                ("p4", "D", 70),
                ("p5", "E", 60),
            };

            IReadOnlyList<LeaderboardEntry> ranked = Ranking.Rank(rows);

            LeaderboardBoard board = new LeaderboardBoard("season");
            board.SetEntries(ranked);
            Assert.IsTrue(board.TryGet("p2", out LeaderboardEntry me));
            board.SetSelf(me);

            IReadOnlyList<LeaderboardEntry> window = board.GetAroundSelf(1);

            Assert.AreEqual(3, window.Count);
            Assert.AreEqual("p1", window[0].PlayerId);
            Assert.AreEqual("p2", window[1].PlayerId);
            Assert.AreEqual("p3", window[2].PlayerId);
        }
    }
}
