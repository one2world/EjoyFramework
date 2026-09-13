//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Debugger;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// PerformanceCollector 单测（Phase 16）。
    /// 覆盖：Sample / 环形 buffer 覆盖 / Snapshot 统计 (min/avg/max/p95/p99) /
    ///       SpikeFrameCount 阈值判定 / Reset / CopyDeltaSeconds 顺序。
    /// </summary>
    public class PerformanceCollectorTests
    {
        [Test]
        public void Empty_Snapshot_HasZeroSamples()
        {
            var c = new PerformanceCollector(16);
            var s = c.Snapshot;
            Assert.AreEqual(16, s.Capacity);
            Assert.AreEqual(0, s.SampleCount);
            Assert.AreEqual(0, s.SpikeFrameCount);
            Assert.AreEqual(0f, s.AvgFps, 0.0001f);
        }

        [Test]
        public void Sample_FilledCount_StopsAtCapacity()
        {
            var c = new PerformanceCollector(8);
            for (int i = 0; i < 20; i++) c.Sample(0.016f, 1024L * 1024);
            Assert.AreEqual(8, c.FilledCount);
        }

        [Test]
        public void Snapshot_AvgDeltaTime_IsCorrect()
        {
            var c = new PerformanceCollector(16);
            for (int i = 0; i < 4; i++) c.Sample(0.020f, 1024 * 1024);
            for (int i = 0; i < 4; i++) c.Sample(0.040f, 2048 * 1024);
            var s = c.Snapshot;
            Assert.AreEqual(8, s.SampleCount);
            Assert.AreEqual(0.030f, s.AvgDeltaTime, 0.0001f);
            Assert.AreEqual(0.020f, s.MinDeltaTime, 0.0001f);
            Assert.AreEqual(0.040f, s.MaxDeltaTime, 0.0001f);
        }

        [Test]
        public void Snapshot_ManagedMemoryStats_Correct()
        {
            var c = new PerformanceCollector(16);
            c.Sample(0.016f, 1_000_000);
            c.Sample(0.016f, 2_000_000);
            c.Sample(0.016f, 3_000_000);
            var s = c.Snapshot;
            Assert.AreEqual(1_000_000L, s.MinManagedMemory);
            Assert.AreEqual(2_000_000L, s.AvgManagedMemory);
            Assert.AreEqual(3_000_000L, s.MaxManagedMemory);
        }

        [Test]
        public void SpikeFrameCount_IncrementsAboveThreshold()
        {
            var c = new PerformanceCollector(16);
            c.SpikeThresholdSeconds = 1f / 30f;   // ~33.3ms
            c.Sample(0.020f, 0);   // 50fps 不算
            c.Sample(0.050f, 0);   // 20fps 算
            c.Sample(0.040f, 0);   // 25fps 算
            c.Sample(0.016f, 0);   // 60fps 不算
            Assert.AreEqual(2, c.SpikeFrameCount);
        }

        [Test]
        public void RingBuffer_OldestSamplesAreOverwritten()
        {
            var c = new PerformanceCollector(8);
            // 写 12 个，capacity=8 → 应覆盖最早的 4 个，保留 0.050..0.120
            for (int i = 1; i <= 12; i++) c.Sample(i * 0.010f, 0);

            var buf = new float[8];
            int n = c.CopyDeltaSeconds(buf);
            Assert.AreEqual(8, n);
            Assert.AreEqual(0.050f, buf[0], 0.0001f, "最旧应该是 0.050（前 4 个被覆盖）");
            Assert.AreEqual(0.120f, buf[7], 0.0001f, "最新应该是 0.120");
        }

        [Test]
        public void P95_P99_Reflect_TailSamples()
        {
            var c = new PerformanceCollector(100);
            // 99 帧正常 + 1 帧 spike
            for (int i = 0; i < 99; i++) c.Sample(0.016f, 0);
            c.Sample(0.100f, 0);

            var s = c.Snapshot;
            Assert.AreEqual(0.016f, s.MinDeltaTime, 0.0001f);
            Assert.AreEqual(0.100f, s.MaxDeltaTime, 0.0001f);
            Assert.AreEqual(0.100f, s.P99DeltaTime, 0.0001f, "p99 应该包含 spike");
            // p95 在 100 帧中第 95 个 (sorted)，spike 在最后一个，所以 p95 仍是 0.016
            Assert.AreEqual(0.016f, s.P95DeltaTime, 0.0001f);
        }

        [Test]
        public void Reset_ClearsAllState()
        {
            var c = new PerformanceCollector(8);
            for (int i = 0; i < 10; i++) c.Sample(0.050f, 1024);
            Assert.Greater(c.FilledCount, 0);
            Assert.Greater(c.SpikeFrameCount, 0);

            c.Reset();
            Assert.AreEqual(0, c.FilledCount);
            Assert.AreEqual(0, c.SpikeFrameCount);
            Assert.AreEqual(0f, c.Snapshot.AvgDeltaTime, 0.0001f);
        }

        [Test]
        public void Capacity_TooSmall_Throws()
        {
            Assert.Throws<FrameworkException>(() => new PerformanceCollector(4));
        }

        [Test]
        public void NegativeDelta_IsClampedToZero()
        {
            var c = new PerformanceCollector(8);
            c.Sample(-0.5f, -100);   // 异常输入
            var s = c.Snapshot;
            Assert.AreEqual(0f, s.MinDeltaTime, 0.0001f);
            Assert.AreEqual(0L, s.MinManagedMemory);
        }

        [Test]
        public void AvgFps_DerivedFromAvgDelta()
        {
            var c = new PerformanceCollector(8);
            for (int i = 0; i < 4; i++) c.Sample(1f / 60f, 0);
            var s = c.Snapshot;
            Assert.AreEqual(60f, s.AvgFps, 0.1f);
        }
    }
}
