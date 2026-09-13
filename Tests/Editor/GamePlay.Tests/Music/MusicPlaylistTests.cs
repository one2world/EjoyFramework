//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Music;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Music
{
    /// <summary>
    /// 针对 <see cref="MusicPlaylist"/> 顺序 / 循环 / 随机推进语义的单元测试。
    /// </summary>
    [TestFixture]
    public class MusicPlaylistTests
    {
        [Test]
        public void Sequential_StopsAtEnd()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Sequential);
            list.AddRange(new[] { "a", "b", "c" });

            Assert.IsNull(list.Current); // 尚未开始。
            Assert.AreEqual("a", list.Next());
            Assert.AreEqual("a", list.Current);
            Assert.AreEqual("b", list.Next());
            Assert.AreEqual("c", list.Next());
            // 越过末尾后停止：Next 与 Current 均为 null。
            Assert.IsNull(list.Next());
            Assert.IsNull(list.Current);
            // 继续 Next 保持终止态。
            Assert.IsNull(list.Next());
        }

        [Test]
        public void Loop_Wraps()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Loop);
            list.AddRange(new[] { "a", "b" });

            Assert.AreEqual("a", list.Next());
            Assert.AreEqual("b", list.Next());
            Assert.AreEqual("a", list.Next()); // 回绕。
            Assert.AreEqual("b", list.Next());
        }

        [Test]
        public void Shuffle_Deterministic_WithInjectedPick()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Shuffle);
            list.AddRange(new[] { "a", "b", "c", "d" });

            // 防偏置洗牌：首支曲目从全部 N 个候选中抽（pick(N)）；之后每支从 N-1 个「非当前曲目」
            // 候选里抽（pick(N-1)），返回下标落在 prev 及之后时自动 +1 还原，从而每个非当前曲目概率均等。
            // 注入序列：
            //   pick(4)=0           → a(0)
            //   pick(3)=2, prev=0   → 2>=0 → 3 → d
            //   pick(3)=1, prev=3   → 1<3  → 1 → b
            //   pick(3)=0, prev=1   → 0<1  → 0 → a
            Queue<int> picks = new Queue<int>(new[] { 0, 2, 1, 0 });
            System.Func<int, int> pick = max => picks.Dequeue();

            Assert.AreEqual("a", list.Next(pick));
            Assert.AreEqual("d", list.Next(pick));
            Assert.AreEqual("b", list.Next(pick));
            Assert.AreEqual("a", list.Next(pick));
        }

        [Test]
        public void Shuffle_AvoidsImmediateRepeat()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Shuffle);
            list.AddRange(new[] { "a", "b", "c" });

            // 第一次选 index 1 => "b"。
            Assert.AreEqual("b", list.Next(max => 1));
            // 第二次注入器再次返回相同 index 1；应避免紧邻重复，确定性后移一位 => index 2 => "c"。
            Assert.AreEqual("c", list.Next(max => 1));
        }

        [Test]
        public void Shuffle_SingleTrack_AlwaysSame()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Shuffle);
            list.Add("only");

            Assert.AreEqual("only", list.Next(max => 0));
            Assert.AreEqual("only", list.Next(max => 0));
        }

        [Test]
        public void Reset_ReturnsToStart()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Sequential);
            list.AddRange(new[] { "a", "b", "c" });

            Assert.AreEqual("a", list.Next());
            Assert.AreEqual("b", list.Next());

            list.Reset();
            Assert.IsNull(list.Current);
            Assert.AreEqual("a", list.Next()); // 复位后从头开始。
        }

        [Test]
        public void Empty_Next_ReturnsNull()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Loop);
            Assert.AreEqual(0, list.Count);
            Assert.IsNull(list.Next());
            Assert.IsNull(list.Current);
        }

        [Test]
        public void Add_IgnoresEmpty_AddRange_IgnoresNull()
        {
            MusicPlaylist list = new MusicPlaylist();
            list.Add(null);
            list.Add(string.Empty);
            list.AddRange(null);
            Assert.AreEqual(0, list.Count);

            list.AddRange(new[] { "a", null, "b" });
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void Clear_ResetsEverything()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Loop);
            list.AddRange(new[] { "a", "b" });
            list.Next();

            list.Clear();
            Assert.AreEqual(0, list.Count);
            Assert.IsNull(list.Current);
            Assert.IsNull(list.Next());
        }

        [Test]
        public void Mode_CanBeChangedAtRuntime()
        {
            MusicPlaylist list = new MusicPlaylist(PlaylistMode.Sequential);
            list.AddRange(new[] { "a", "b" });

            Assert.AreEqual("a", list.Next());
            Assert.AreEqual("b", list.Next());
            // 切到 Loop 后应回绕。
            list.Mode = PlaylistMode.Loop;
            Assert.AreEqual("a", list.Next());
        }
    }
}
