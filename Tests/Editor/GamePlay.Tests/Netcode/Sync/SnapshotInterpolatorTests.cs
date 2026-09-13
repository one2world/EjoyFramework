//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Sync;

namespace EjoyFramework.GamePlay.Tests.Netcode.Sync
{
    public class SnapshotInterpolatorTests
    {
        private const float Delta = 1e-3f;

        private static EntitySnapshot Find(IReadOnlyList<EntitySnapshot> list, int entityId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].EntityId == entityId)
                {
                    return list[i];
                }
            }

            Assert.Fail("结果中未找到实体 " + entityId);
            return default;
        }

        [Test]
        public void Sample_MovingEntity_HalfWay_IsMidpoint()
        {
            // 实体 1：t=0 时 x=0，t=1 时 x=10；渲染时间 0.5 → x≈5。
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f }));
            buffer.Add(s1);

            // 延迟 0，渲染时间 = 0.5。
            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(0.5d);

            Assert.AreEqual(1, sampled.Count);
            Assert.AreEqual(5f, Find(sampled, 1).Values[0], Delta);
        }

        [Test]
        public void Sample_AppliesInterpolationDelay()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f }));
            buffer.Add(s1);

            // now=1.0，延迟=0.5 → 渲染时间=0.5 → x≈5。
            var interp = new SnapshotInterpolator(buffer, 0.5d);
            var sampled = interp.Sample(1.0d);

            Assert.AreEqual(5f, Find(sampled, 1).Values[0], Delta);
        }

        [Test]
        public void Sample_MultiComponentValues_LerpedPerComponent()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f, 0f, 0f, 0f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f, -10f, 4f, 90f }));
            buffer.Add(s1);

            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var values = Find(interp.Sample(0.5d), 1).Values;

            Assert.AreEqual(5f, values[0], Delta);
            Assert.AreEqual(-5f, values[1], Delta);
            Assert.AreEqual(2f, values[2], Delta);
            Assert.AreEqual(45f, values[3], Delta);
        }

        [Test]
        public void Sample_EntityOnlyInFromSnapshot_UsesFromValues()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 3f }));
            s0.AddEntity(new EntitySnapshot(2, new[] { 7f })); // 仅在 from
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 5f }));
            buffer.Add(s1);

            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(0.5d);

            // 实体 2 仅在 from，应直接取 from 的值（无淡出）。
            Assert.AreEqual(2, sampled.Count);
            Assert.AreEqual(7f, Find(sampled, 2).Values[0], Delta);
            // 实体 1 正常插值。
            Assert.AreEqual(4f, Find(sampled, 1).Values[0], Delta);
        }

        [Test]
        public void Sample_EntityOnlyInToSnapshot_UsesToValues()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f }));
            s1.AddEntity(new EntitySnapshot(9, new[] { 42f })); // 仅在 to
            buffer.Add(s1);

            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(0.5d);

            Assert.AreEqual(2, sampled.Count);
            Assert.AreEqual(42f, Find(sampled, 9).Values[0], Delta);
        }

        [Test]
        public void Sample_EmptyBuffer_ReturnsEmptyList()
        {
            var buffer = new SnapshotBuffer(8);
            var interp = new SnapshotInterpolator(buffer, 0.1d);
            var sampled = interp.Sample(1.0d);

            Assert.IsNotNull(sampled);
            Assert.AreEqual(0, sampled.Count);
        }

        [Test]
        public void Sample_BeforeOldest_ClampsToOldestValues()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 1.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 100f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 2.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 200f }));
            buffer.Add(s1);

            // 渲染时间 = 0.0 - 0 = 0.0，早于最旧 1.0 → 钳制到最旧值 100。
            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(0.0d);

            Assert.AreEqual(100f, Find(sampled, 1).Values[0], Delta);
        }

        [Test]
        public void Sample_AfterLatest_ClampsToLatestValues_NoExtrapolation()
        {
            var buffer = new SnapshotBuffer(8);

            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f }));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f }));
            buffer.Add(s1);

            // 渲染时间 = 5.0，晚于最新 1.0 → 钳制到最新值 10（不外推到 50）。
            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(5.0d);

            Assert.AreEqual(10f, Find(sampled, 1).Values[0], Delta);
        }

        [Test]
        public void Sample_ReturnsFreshList_DoesNotShareInternalArrays()
        {
            var buffer = new SnapshotBuffer(8);

            float[] sourceValues = { 0f };
            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, sourceValues));
            buffer.Add(s0);

            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(2, new[] { 5f })); // 仅 to，会走拷贝路径
            buffer.Add(s1);

            var interp = new SnapshotInterpolator(buffer, 0.0d);
            var sampled = interp.Sample(0.0d); // 钳到最旧 → 实体 1 走 CopyValues

            // 改写采样结果不应回写到源快照内部数组。
            var got = Find(sampled, 1);
            got.Values[0] = 999f;
            Assert.AreEqual(0f, sourceValues[0], Delta, "采样结果应是源数据的拷贝");
        }

        [Test]
        public void SampleInto_ReusesValueArraysAcrossFrames()
        {
            var buffer = new SnapshotBuffer(8);
            var s0 = new WorldSnapshot(0u, 0.0d);
            s0.AddEntity(new EntitySnapshot(1, new[] { 0f, 2f }));
            buffer.Add(s0);
            var s1 = new WorldSnapshot(1u, 1.0d);
            s1.AddEntity(new EntitySnapshot(1, new[] { 10f, 4f }));
            buffer.Add(s1);

            var interp = new SnapshotInterpolator(buffer, 0d);
            var into = new List<EntitySnapshot>();
            interp.Sample(0.25d, into);
            float[] firstFrameValues = into[0].Values;

            interp.Sample(0.75d, into);

            Assert.AreSame(firstFrameValues, into[0].Values);
            Assert.AreEqual(7.5f, into[0].Values[0], Delta);
        }

        [Test]
        public void Sample_SeparateCalls_DoNotShareValueArrays()
        {
            var buffer = new SnapshotBuffer(8);
            var snapshot = new WorldSnapshot(0u, 0.0d);
            snapshot.AddEntity(new EntitySnapshot(1, new[] { 3f }));
            buffer.Add(snapshot);

            var interp = new SnapshotInterpolator(buffer, 0d);
            var first = interp.Sample(0d);
            var second = interp.Sample(0d);

            Assert.AreNotSame(first[0].Values, second[0].Values);
        }
    }
}
