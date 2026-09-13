//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Sequencing;

namespace EjoyFramework.GamePlay.Tests.Sequencing
{
    public class SequenceDirectorTests
    {
        private const double Delta = 1e-6d;

        /// <summary>
        /// 收集触发的标记 Key 顺序，便于断言触发次序与次数。
        /// </summary>
        private static List<string> Capture(SequenceDirector director)
        {
            var fired = new List<string>();
            director.OnMarker += (d, m) => fired.Add(m.Key);
            return fired;
        }

        [Test]
        public void AddTrack_DuplicateId_Throws()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("dup"));
            Assert.Throws<System.InvalidOperationException>(
                () => director.AddTrack(new SequenceTrack("dup")));
        }

        [Test]
        public void AddTrack_Null_Throws()
        {
            var director = new SequenceDirector();
            Assert.Throws<System.ArgumentNullException>(() => director.AddTrack(null));
        }

        [Test]
        public void Duration_IsMaxMarkerTimeAcrossTracks()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(1.0, "x"));
            director.AddTrack(new SequenceTrack("b").Add(3.5, "y"));
            director.AddTrack(new SequenceTrack("c").Add(2.0, "z"));

            Assert.AreEqual(3.5, director.Duration, Delta);
        }

        [Test]
        public void DefaultState_NotPlaying_SpeedOne()
        {
            var director = new SequenceDirector();
            Assert.IsFalse(director.IsPlaying);
            Assert.AreEqual(1f, director.Speed);
            Assert.AreEqual(0.0, director.Time, Delta);
            Assert.IsFalse(director.Loop);
        }

        [Test]
        public void Tick_WhenNotPlaying_DoesNotAdvanceOrFire()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.5, "x"));
            var fired = Capture(director);

            // 未调用 Play()，Tick 不应推进也不触发。
            director.Tick(1.0);
            Assert.AreEqual(0.0, director.Time, Delta);
            CollectionAssert.IsEmpty(fired);
        }

        [Test]
        public void Tick_FiresMarkersCrossed_InTimeOrderAcrossTracks()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("cam").Add(0.3, "cam.shake").Add(0.9, "cam.cut"));
            director.AddTrack(new SequenceTrack("audio").Add(0.1, "audio.hit").Add(0.6, "audio.swell"));
            var fired = Capture(director);

            director.Play();
            director.Tick(1.0);

            // 跨轨道按时间升序：0.1, 0.3, 0.6, 0.9
            CollectionAssert.AreEqual(
                new[] { "audio.hit", "cam.shake", "audio.swell", "cam.cut" }, fired);
        }

        [Test]
        public void Tick_SameTimeAcrossTracks_FiresInTrackAddOrder()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("first").Add(0.5, "A"));
            director.AddTrack(new SequenceTrack("second").Add(0.5, "B"));
            var fired = Capture(director);

            director.Play();
            director.Tick(1.0);

            // 相同时间 0.5：按轨道添加顺序 first -> second。
            CollectionAssert.AreEqual(new[] { "A", "B" }, fired);
        }

        [Test]
        public void Tick_DoesNotRefireMarkersOnSubsequentTicks()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.5, "x"));
            var fired = Capture(director);

            director.Play();
            director.Tick(1.0);
            Assert.AreEqual(1, fired.Count);

            // 再次推进（已越过末尾，非循环停止），不应重复触发。
            director.Tick(1.0);
            Assert.AreEqual(1, fired.Count);
        }

        [Test]
        public void Tick_PartialTicksAccumulate_CrossMarkerExactlyOnce()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.7, "mid"));
            var fired = Capture(director);

            director.Play();

            // 第一帧 0.5：尚未到 0.7。
            director.Tick(0.5);
            CollectionAssert.IsEmpty(fired);
            Assert.AreEqual(0.5, director.Time, Delta);

            // 第二帧 0.5 -> 累计 1.0：越过 0.7 恰好一次。
            director.Tick(0.5);
            CollectionAssert.AreEqual(new[] { "mid" }, fired);
        }

        [Test]
        public void Tick_MarkerExactlyAtUpperBound_Fires()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.5, "edge"));
            var fired = Capture(director);

            director.Play();
            // 半开区间 (0, 0.5]：恰好等于上界应触发。
            director.Tick(0.5);
            CollectionAssert.AreEqual(new[] { "edge" }, fired);
        }

        [Test]
        public void Speed_ScalesAdvancement()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(1.0, "x"));
            var fired = Capture(director);

            director.Speed = 2f;
            director.Play();

            // 0.6 * 2 = 1.2，越过 1.0。
            director.Tick(0.6);
            CollectionAssert.AreEqual(new[] { "x" }, fired);
        }

        [Test]
        public void Pause_StopsAdvancing()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(0.8, "x"));
            var fired = Capture(director);

            director.Play();
            director.Tick(0.3);
            Assert.AreEqual(0.3, director.Time, Delta);

            director.Pause();
            director.Tick(1.0); // 暂停期间不推进。
            Assert.AreEqual(0.3, director.Time, Delta);
            CollectionAssert.IsEmpty(fired);
        }

        [Test]
        public void Stop_ResetsTimeToZeroAndPauses()
        {
            var director = new SequenceDirector();
            director.AddTrack(new SequenceTrack("a").Add(2.0, "x"));

            director.Play();
            director.Tick(0.5);
            Assert.AreEqual(0.5, director.Time, Delta);

            director.Stop();
            Assert.AreEqual(0.0, director.Time, Delta);
            Assert.IsFalse(director.IsPlaying);
        }
    }
}
