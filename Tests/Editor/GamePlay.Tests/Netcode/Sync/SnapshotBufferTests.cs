//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Sync;

namespace EjoyFramework.GamePlay.Tests.Netcode.Sync
{
    public class SnapshotBufferTests
    {
        private const float Delta = 1e-4f;

        private static WorldSnapshot Snap(uint tick, double time)
        {
            return new WorldSnapshot(tick, time);
        }

        [Test]
        public void Add_KeepsAscendingOrder_EvenWhenInsertedOutOfOrder()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(2u, 2.0d));
            buffer.Add(Snap(0u, 0.0d));
            buffer.Add(Snap(1u, 1.0d));

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(0.0d, buffer.Oldest.ServerTimeSec, 1e-9d);
            Assert.AreEqual(2.0d, buffer.Latest.ServerTimeSec, 1e-9d);
        }

        [Test]
        public void Add_DuplicateServerTime_IsIgnored()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(99u, 1.0d)); // 同时间戳，应被忽略，保留先到者

            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(1u, buffer.Latest.Tick, "应保留先到者");
        }

        [Test]
        public void Add_OverCapacity_EvictsOldest()
        {
            var buffer = new SnapshotBuffer(3);
            buffer.Add(Snap(0u, 0.0d));
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(2u, 2.0d));
            buffer.Add(Snap(3u, 3.0d)); // 超容，淘汰 t=0

            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(1.0d, buffer.Oldest.ServerTimeSec, 1e-9d, "最旧的 t=0 应被淘汰");
            Assert.AreEqual(3.0d, buffer.Latest.ServerTimeSec, 1e-9d);
        }

        [Test]
        public void Add_WhenFull_OlderThanOldest_IsIgnored()
        {
            var buffer = new SnapshotBuffer(2);
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(2u, 2.0d));
            buffer.Add(Snap(0u, 0.0d)); // 已满且比最旧还旧，落在窗口外，忽略

            Assert.AreEqual(2, buffer.Count);
            Assert.AreEqual(1.0d, buffer.Oldest.ServerTimeSec, 1e-9d);
            Assert.AreEqual(2.0d, buffer.Latest.ServerTimeSec, 1e-9d);
        }

        [Test]
        public void Latest_And_Oldest_NullWhenEmpty()
        {
            var buffer = new SnapshotBuffer(4);
            Assert.IsNull(buffer.Latest);
            Assert.IsNull(buffer.Oldest);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void Clear_EmptiesBuffer()
        {
            var buffer = new SnapshotBuffer(4);
            buffer.Add(Snap(0u, 0.0d));
            buffer.Add(Snap(1u, 1.0d));
            buffer.Clear();

            Assert.AreEqual(0, buffer.Count);
            Assert.IsNull(buffer.Latest);
        }

        [Test]
        public void GetInterpolationPair_EmptyBuffer_ReturnsFalse()
        {
            var buffer = new SnapshotBuffer(4);
            WorldSnapshot from, to;
            float t;
            Assert.IsFalse(buffer.GetInterpolationPair(0.5d, out from, out to, out t));
            Assert.IsNull(from);
            Assert.IsNull(to);
        }

        [Test]
        public void GetInterpolationPair_MidpointBracket_PicksCorrectPair_AndT()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(0u, 0.0d));
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(2u, 2.0d));

            WorldSnapshot from, to;
            float t;
            Assert.IsTrue(buffer.GetInterpolationPair(1.25d, out from, out to, out t));
            Assert.AreEqual(1.0d, from.ServerTimeSec, 1e-9d);
            Assert.AreEqual(2.0d, to.ServerTimeSec, 1e-9d);
            Assert.AreEqual(0.25f, t, Delta, "(1.25-1)/(2-1) = 0.25");
        }

        [Test]
        public void GetInterpolationPair_ExactMidpoint_TIsHalf()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(0u, 0.0d));
            buffer.Add(Snap(1u, 1.0d));

            WorldSnapshot from, to;
            float t;
            Assert.IsTrue(buffer.GetInterpolationPair(0.5d, out from, out to, out t));
            Assert.AreEqual(0.0d, from.ServerTimeSec, 1e-9d);
            Assert.AreEqual(1.0d, to.ServerTimeSec, 1e-9d);
            Assert.AreEqual(0.5f, t, Delta);
        }

        [Test]
        public void GetInterpolationPair_BeforeOldest_ClampsToOldest_T0()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(2u, 2.0d));

            WorldSnapshot from, to;
            float t;
            Assert.IsTrue(buffer.GetInterpolationPair(0.0d, out from, out to, out t));
            Assert.AreSame(buffer.Oldest, from);
            Assert.AreSame(buffer.Oldest, to);
            Assert.AreEqual(0f, t, Delta);
        }

        [Test]
        public void GetInterpolationPair_AfterLatest_ClampsToLatest_T1_NoExtrapolation()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(1u, 1.0d));
            buffer.Add(Snap(2u, 2.0d));

            WorldSnapshot from, to;
            float t;
            Assert.IsTrue(buffer.GetInterpolationPair(5.0d, out from, out to, out t));
            Assert.AreSame(buffer.Latest, from);
            Assert.AreSame(buffer.Latest, to);
            Assert.AreEqual(1f, t, Delta);
        }

        [Test]
        public void GetInterpolationPair_SingleSnapshot_ReturnsSelfPair()
        {
            var buffer = new SnapshotBuffer(8);
            buffer.Add(Snap(3u, 3.0d));

            WorldSnapshot from, to;
            float t;
            Assert.IsTrue(buffer.GetInterpolationPair(3.0d, out from, out to, out t));
            Assert.AreSame(buffer.Latest, from);
            Assert.AreSame(buffer.Latest, to);
        }
    }
}
