//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Sequencing;

namespace EjoyFramework.GamePlay.Tests.Sequencing
{
    public class SequenceDirectorPlaybackTests
    {
        private const double Delta = 1e-6d;

        private static List<string> Capture(SequenceDirector director)
        {
            var fired = new List<string>();
            director.OnMarker += (d, m) => fired.Add(m.Key);
            return fired;
        }

        [Test]
        public void Seek_RepositionsWithoutFiringMarkers()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.5, "early").Add(1.5, "late"));
            var fired = Capture(director);

            director.Play();
            // 跳转越过 "early"，但 Seek 不触发任何标记。
            director.Seek(1.0);
            Assert.AreEqual(1.0, director.Time, Delta);
            CollectionAssert.IsEmpty(fired);

            // 跳转后继续推进，只触发严格大于 1.0 的 "late"，不补发 "early"。
            director.Tick(1.0);
            CollectionAssert.AreEqual(new[] { "late" }, fired);
        }

        [Test]
        public void Seek_ClampsToDurationRange()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(2.0, "x"));

            director.Seek(-5.0);
            Assert.AreEqual(0.0, director.Time, Delta);

            director.Seek(100.0);
            Assert.AreEqual(2.0, director.Time, Delta);
        }

        [Test]
        public void Seek_MarkerExactlyAtSeekPoint_NotRefiredAfterward()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(1.0, "at"));
            var fired = Capture(director);

            director.Play();
            director.Seek(1.0); // 正好落在标记点上。
            director.Tick(1.0); // 后续只触发严格 > 1.0，"at" 不补发。
            CollectionAssert.IsEmpty(fired);
        }

        [Test]
        public void OnComplete_FiresOnceAtEnd_WhenNotLooping()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(1.0, "x"));
            int completeCount = 0;
            director.OnComplete += d => completeCount++;

            director.Play();
            director.Tick(1.0);
            Assert.AreEqual(1, completeCount);
            Assert.IsFalse(director.IsPlaying);
            Assert.AreEqual(1.0, director.Time, Delta);

            // 再次 Tick（已停止）不应再触发 OnComplete。
            director.Tick(1.0);
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void OnComplete_DoesNotFire_WhenLooping()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(1.0, "x"));
            int completeCount = 0;
            director.OnComplete += d => completeCount++;
            director.Loop = true;

            director.Play();
            director.Tick(1.0);
            director.Tick(1.0);
            Assert.AreEqual(0, completeCount);
            Assert.IsTrue(director.IsPlaying);
        }

        [Test]
        public void Loop_WrapsAndRefiresNextLap()
        {
            var director = new SequenceDirector();
            // 标记在 0.5；显式时长 1.0 构成 1 秒循环（时长长于最后一个标记）。
            director.AddTrack(new SequenceTrack("a").Add(0.5, "beat"));
            director.Duration = 1.0;
            var fired = Capture(director);
            director.Loop = true;

            director.Play();

            // 第一圈：0 -> 0.8，越过 0.5 一次。
            director.Tick(0.8);
            CollectionAssert.AreEqual(new[] { "beat" }, fired);
            Assert.AreEqual(0.8, director.Time, Delta);

            // 0.8 -> 1.2 环绕：尾段 (0.8,1.0] 无标记，新一圈 [0,0.2] 无标记。
            director.Tick(0.4);
            Assert.AreEqual(1, fired.Count); // 尚未再次越过 0.5。
            Assert.AreEqual(0.2, director.Time, Delta);

            // 第二圈继续到 0.6，再次越过 0.5。
            director.Tick(0.4);
            CollectionAssert.AreEqual(new[] { "beat", "beat" }, fired);
            Assert.AreEqual(0.6, director.Time, Delta);
        }

        [Test]
        public void Loop_SingleTickSpanningEnd_FiresTailThenWrappedHead()
        {
            var director = new SequenceDirector();
            // 尾段标记 0.9，头段标记（下一圈）0.1；显式时长 1.0 构成 1 秒循环。
            director.AddTrack(new SequenceTrack("a").Add(0.9, "tail").Add(0.1, "head"));
            director.Duration = 1.0;
            var fired = Capture(director);
            director.Loop = true;

            director.Play();
            // 从 0.5 起步：先 Seek 到 0.5（不触发），再单帧推进 0.7 -> 1.2 环绕。
            director.Seek(0.5);
            director.Tick(0.7);

            // 尾段 (0.5,1.0] 触发 "tail"；环绕头段 [0,0.2] 触发 "head"。
            CollectionAssert.AreEqual(new[] { "tail", "head" }, fired);
            Assert.AreEqual(0.2, director.Time, Delta);
        }

        // ---- t = 0 标记的明确行为：在序列开始后的首个推进帧触发，且只触发一次。 ----

        [Test]
        public void ZeroTimeMarker_FiresOnFirstTick_NotOnPlay()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.0, "start").Add(0.5, "mid"));
            var fired = Capture(director);

            director.Play();
            // Play 本身不触发任何标记。
            CollectionAssert.IsEmpty(fired);

            // 首个推进帧（dt>0）纳入 t=0 标记，按时间序：start(0) 然后 mid(0.5)。
            director.Tick(0.5);
            CollectionAssert.AreEqual(new[] { "start", "mid" }, fired);
        }

        [Test]
        public void ZeroTimeMarker_FiresExactlyOnce_NotReFiredOnNextTick()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.0, "start").Add(1.0, "end"));
            var fired = Capture(director);

            director.Play();
            director.Tick(0.4); // 首帧：仅 start。
            CollectionAssert.AreEqual(new[] { "start" }, fired);

            director.Tick(0.4); // 第二帧：不再重发 start。
            CollectionAssert.AreEqual(new[] { "start" }, fired);
        }

        [Test]
        public void ZeroTimeMarker_RefiresAfterStopAndReplay()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.0, "start"));
            var fired = Capture(director);

            director.Play();
            director.Tick(0.2);
            CollectionAssert.AreEqual(new[] { "start" }, fired);

            // Stop 重置到 0；重新播放后首帧应再次触发 t=0 标记。
            director.Stop();
            director.Play();
            director.Tick(0.2);
            CollectionAssert.AreEqual(new[] { "start", "start" }, fired);
        }

        [Test]
        public void ZeroTimeMarker_NotFiredAfterSeekPastZero()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.0, "start").Add(1.0, "end"));
            var fired = Capture(director);

            director.Play();
            // Seek 取消"首帧闭下界"约定：跳过 0 后不应补发 t=0 标记。
            director.Seek(0.5);
            director.Tick(1.0);
            CollectionAssert.AreEqual(new[] { "end" }, fired);
        }
    }
}
