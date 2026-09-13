//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Sequencing;

namespace EjoyFramework.GamePlay.Tests.Sequencing
{
    public class SequenceTrackTests
    {
        private const double Delta = 1e-6d;

        [Test]
        public void Ctor_StoresId()
        {
            var track = new SequenceTrack("camera");
            Assert.AreEqual("camera", track.Id);
        }

        [Test]
        public void Ctor_NullOrEmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new SequenceTrack(null));
            Assert.Throws<System.ArgumentException>(() => new SequenceTrack(string.Empty));
        }

        [Test]
        public void Add_KeepsMarkersTimeSorted_RegardlessOfInsertOrder()
        {
            var track = new SequenceTrack("t");
            track.Add(2.0, "b")
                 .Add(0.5, "a")
                 .Add(3.0, "c")
                 .Add(1.0, "x");

            var markers = track.Markers;
            Assert.AreEqual(4, markers.Count);
            Assert.AreEqual(0.5, markers[0].Time, Delta);
            Assert.AreEqual(1.0, markers[1].Time, Delta);
            Assert.AreEqual(2.0, markers[2].Time, Delta);
            Assert.AreEqual(3.0, markers[3].Time, Delta);

            Assert.AreEqual("a", markers[0].Key);
            Assert.AreEqual("x", markers[1].Key);
            Assert.AreEqual("b", markers[2].Key);
            Assert.AreEqual("c", markers[3].Key);
        }

        [Test]
        public void Add_SameTime_KeepsStableInsertionOrder()
        {
            var track = new SequenceTrack("t");
            track.Add(1.0, "first")
                 .Add(1.0, "second")
                 .Add(1.0, "third");

            var markers = track.Markers;
            Assert.AreEqual(3, markers.Count);
            Assert.AreEqual("first", markers[0].Key);
            Assert.AreEqual("second", markers[1].Key);
            Assert.AreEqual("third", markers[2].Key);
        }

        [Test]
        public void Add_AssignsTrackIdAndPayload()
        {
            var payload = new object();
            var track = new SequenceTrack("audio");
            track.Add(0.25, "sfx.boom", payload);

            var marker = track.Markers[0];
            Assert.AreEqual("audio", marker.TrackId);
            Assert.AreEqual("sfx.boom", marker.Key);
            Assert.AreSame(payload, marker.Payload);
        }

        [Test]
        public void Duration_IsLastMarkerTime_ZeroWhenEmpty()
        {
            var empty = new SequenceTrack("t");
            Assert.AreEqual(0.0, empty.Duration, Delta);

            empty.Add(1.5, "a").Add(0.5, "b");
            Assert.AreEqual(1.5, empty.Duration, Delta);
        }

        [Test]
        public void Add_ReturnsSelf_ForFluentChaining()
        {
            var track = new SequenceTrack("t");
            Assert.AreSame(track, track.Add(0.0, "k"));
        }
    }
}
