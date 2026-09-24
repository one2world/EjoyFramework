//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Benchmarking;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests
{
    /// <summary>WS4-M4：基准基建——统计、执行器（注入时钟）、报告读写、基线对比判定、真机采集直方图、分配探针自校准。</summary>
    public sealed class BenchmarkInfraTests
    {
        private sealed class FakeProbe : IAllocationProbe
        {
            public long PerRun;
            public bool IsAvailable { get { return true; } }
            public string Unit { get { return "allocs"; } }
            public void Begin() { }
            public long End() { return PerRun; }
        }

        [TearDown]
        public void TearDown()
        {
            BenchmarkRunner.ClockOverride = null;
            BenchmarkRunner.ClockFrequencyOverride = 0;
        }

        // ---------------- 统计 / 执行器 ----------------

        [Test]
        public void Summarize_OddEvenMedian_CeilRankP95_SampleStdDev()
        {
            BenchmarkResult odd = BenchmarkRunner.Summarize("a", new double[] { 5, 1, 3, 2, 4 }, 10);
            Assert.AreEqual(1, odd.MinNs);
            Assert.AreEqual(3, odd.MedianNs);
            Assert.AreEqual(3, odd.MeanNs);
            Assert.AreEqual(5, odd.P95Ns, "ceil(0.95*5)=5 → 第 5 个。");
            Assert.AreEqual(Math.Sqrt(2.5), odd.StdDevNs, 1e-12, "样本标准差（n-1）。");

            BenchmarkResult even = BenchmarkRunner.Summarize("b", new double[] { 4, 1, 3, 2 }, 10);
            Assert.AreEqual(2.5, even.MedianNs);
            double[] twenty = new double[20];
            for (int i = 0; i < 20; i++) twenty[i] = i + 1;
            Assert.AreEqual(19, BenchmarkRunner.Summarize("c", twenty, 1).P95Ns, "ceil(0.95*20)=19。");
        }

        [Test]
        public void Runner_WithInjectedClock_MeasuresNsPerOp_CalibratesOps_MeasuresAlloc()
        {
            long ticks = 0;
            BenchmarkRunner.ClockOverride = () => ticks;
            BenchmarkRunner.ClockFrequencyOverride = 1000000000L;   // 1 tick = 1ns
            FakeProbe probe = new FakeProbe();

            Action<long> body = n => { ticks += 250 * n; probe.PerRun = 3 * n; };

            BenchmarkOptions o = new BenchmarkOptions { WarmupRuns = 1, MinSampleMillis = 1.0, Samples = 5 };
            BenchmarkResult r = BenchmarkRunner.Run("fake", body, o, probe);
            Assert.AreEqual(250.0, r.MedianNs, 1e-9);
            Assert.AreEqual(250.0, r.MinNs, 1e-9);
            Assert.AreEqual(0.0, r.StdDevNs, 1e-9);
            Assert.GreaterOrEqual(r.OpsPerSample * 250.0, 1e6, "每样本不少于 1ms。");
            Assert.AreEqual(5, r.Samples);
            Assert.IsTrue(r.AllocMeasured);
            Assert.AreEqual(3.0, r.AllocPerOp, 1e-9);
            Assert.AreEqual("allocs", r.AllocUnit);

            Assert.Throws<FrameworkException>(() => BenchmarkRunner.Run("", body));
            Assert.Throws<FrameworkException>(() => BenchmarkRunner.Run("x", null));
        }

        [Test]
        public void Runner_UnavailableProbe_MarksAllocUnmeasured()
        {
            BenchmarkResult r = BenchmarkRunner.Run("noop", n => { for (long i = 0; i < n; i++) { } }, BenchmarkOptions.Quick(), null);
            Assert.IsFalse(r.AllocMeasured, "没有探针就不给分配数，避免把\"未测\"读成\"零分配\"。");
            Assert.Greater(r.OpsPerSample, 0);
        }

        // ---------------- 报告 ----------------

        private static BenchmarkResult Result(string name, double median, double min, double alloc, bool measured = true)
        {
            return new BenchmarkResult
            {
                Name = name, Samples = 7, OpsPerSample = 1024, MinNs = min, MedianNs = median, MeanNs = median, P95Ns = median * 1.1,
                StdDevNs = 0.5, AllocPerOp = alloc, AllocMeasured = measured, AllocUnit = measured ? "allocs" : null,
            };
        }

        [Test]
        public void Report_RoundTrips_AndValidates()
        {
            BenchmarkReport report = new BenchmarkReport();
            report.SetEnvironment("suite", "EditMode");
            report.SetEnvironment("device", "PC|Win|RTX");
            report.SetEnvironment("suite", "PlayMode");   // 同键替换
            report.Add(Result("Pool.List", 21.25, 20.5, 0));
            report.Add(Result("Text.Format", 80.125, 79, 0, false));

            BenchmarkReport back = BenchmarkReport.Parse(report.ToString());
            Assert.AreEqual("PlayMode", back.GetEnvironment("suite"));
            Assert.AreEqual(2, back.Environment.Count);
            Assert.AreEqual(2, back.Results.Count);
            BenchmarkResult a = back.Find("Pool.List");
            Assert.AreEqual(21.25, a.MedianNs);
            Assert.IsTrue(a.AllocMeasured);
            Assert.AreEqual(0.0, a.AllocPerOp);
            Assert.IsFalse(back.Find("Text.Format").AllocMeasured);

            Assert.Throws<FrameworkException>(() => report.Add(Result("bad\tname", 1, 1, 0)));
            Assert.Throws<FrameworkException>(() => BenchmarkReport.Parse("not a report"));
            string broken = report.ToString().Replace("Pool.List\t7", "Pool.List");
            FrameworkException ex = Assert.Throws<FrameworkException>(() => BenchmarkReport.Parse(broken));
            StringAssert.Contains("应为 10 列", ex.Message);
        }

        [Test]
        public void Compare_VerdictsAreNoiseAware()
        {
            BenchmarkReport baseline = new BenchmarkReport();
            baseline.Add(Result("same", 100, 95, 0));
            baseline.Add(Result("slower", 100, 95, 0));
            baseline.Add(Result("noisy", 100, 95, 0));
            baseline.Add(Result("faster", 100, 95, 0));
            baseline.Add(Result("alloc", 100, 95, 0));
            baseline.Add(Result("fixed", 100, 95, 2));
            baseline.Add(Result("gone", 100, 95, 0));

            BenchmarkReport current = new BenchmarkReport();
            current.Add(Result("same", 110, 100, 0));
            current.Add(Result("slower", 140, 130, 0));
            current.Add(Result("noisy", 140, 90, 0));    // 中位慢了但最快样本不慢于基线中位：噪声
            current.Add(Result("faster", 60, 55, 0));
            current.Add(Result("alloc", 100, 95, 1));
            current.Add(Result("fixed", 100, 95, 0));
            current.Add(Result("new", 10, 9, 0));

            Dictionary<string, BenchmarkVerdict> v = new Dictionary<string, BenchmarkVerdict>();
            foreach (BenchmarkDelta d in BenchmarkComparison.Compare(baseline, current)) v[d.Name] = d.Verdict;
            Assert.AreEqual(BenchmarkVerdict.Same, v["same"]);
            Assert.AreEqual(BenchmarkVerdict.Slower, v["slower"]);
            Assert.AreEqual(BenchmarkVerdict.Same, v["noisy"]);
            Assert.AreEqual(BenchmarkVerdict.Faster, v["faster"]);
            Assert.AreEqual(BenchmarkVerdict.AllocRegression, v["alloc"]);
            Assert.AreEqual(BenchmarkVerdict.AllocImproved, v["fixed"]);
            Assert.AreEqual(BenchmarkVerdict.Added, v["new"]);
            Assert.AreEqual(BenchmarkVerdict.Removed, v["gone"]);
        }

        // ---------------- 真机采集 ----------------

        [Test]
        public void PerfCapture_HistogramPercentiles_HitchesPeaksMarkers()
        {
            PerfCapture c = new PerfCapture();
            c.Begin("town");
            for (int i = 0; i < 90; i++) c.AddFrame(10f, 8f);
            c.Mark("enter");
            for (int i = 0; i < 9; i++) c.AddFrame(40f, 30f);
            c.AddFrame(120f, 0f);                      // 无工作耗时数据的帧不进工作耗时统计
            c.AddFrame(0f, 5f);                        // 非法帧忽略
            c.AddMemorySample(100, 800, 300);
            c.AddMemorySample(150, 700, 350);
            PerfCaptureSummary s = c.End();

            Assert.AreEqual(100, s.Frames);
            Assert.AreEqual(13.8f, s.AvgFrameMs, 1e-4f);
            Assert.AreEqual(10.1f, s.P50FrameMs, 1e-4f, "分位取桶上沿（0.1ms 桶）。");
            Assert.AreEqual(40.1f, s.P95FrameMs, 1e-4f);
            Assert.AreEqual(40.1f, s.P99FrameMs, 1e-4f);
            Assert.AreEqual(120f, s.MaxFrameMs);
            Assert.AreEqual(1, s.Hitches50);
            Assert.AreEqual(1, s.Hitches100);
            Assert.AreEqual(1.38, s.DurationSeconds, 1e-6);
            Assert.AreEqual(10f, s.AvgWorkMs, 1e-4f, "(90*8 + 9*30) / 99 = 10。");
            Assert.AreEqual(150, s.PeakManagedMB);
            Assert.AreEqual(800, s.PeakTotalMB);
            Assert.AreEqual(350, s.PeakGraphicsMB);
            Assert.AreEqual(1, c.Markers.Count);
            Assert.AreEqual(0.9, c.Markers[0].Key, 1e-6);

            string text = c.ToText(new[] { new KeyValuePair<string, string>("device", "X") });
            StringAssert.StartsWith("# ejoy-perfcapture 1\n# label\ttown\n# device\tX\n", text);
            StringAssert.Contains("p95_ms\t40.1\n", text);
            StringAssert.Contains("marker\t0.9\tenter\n", text);

            c.AddFrame(10f, 1f);
            Assert.AreEqual(100, c.Summary.Frames, "结束后不再计入。");
            Assert.Throws<FrameworkException>(() => c.Begin("bad\tlabel"));
        }

        [Test]
        public void PerfCapture_OverflowFramesClampToTopBucket()
        {
            PerfCapture c = new PerfCapture();
            c.Begin("load");
            c.AddFrame(5000f, 0f);
            PerfCaptureSummary s = c.End();
            Assert.AreEqual(250f, s.P50FrameMs, "超出 250ms 的帧计入溢出桶，分位报 250（最大值另见 max）。");
            Assert.AreEqual(5000f, s.MaxFrameMs);
        }

        [Test]
        public void PerfCapture_AddFrame_DoesNotAllocate()
        {
            PerfCapture c = new PerfCapture();
            c.Begin("zero");
            TestDelegate body = () =>
            {
                for (int i = 0; i < 1000; i++) c.AddFrame(8f + (i % 30), 6f);
                c.AddMemorySample(1, 2, 3);
                PerfCaptureSummary s = c.Summary;
                if (s.Frames < 0) throw new InvalidOperationException();
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        // ---------------- Unity 分配探针自校准 ----------------

        [Test]
        public void UnityAllocationProbe_CountsKnownAllocations_AndZeroForAllocFreeCode()
        {
            UnityAllocationProbe probe = new UnityAllocationProbe();
            Assume.That(probe.IsAvailable, NUnit.Framework.Is.True, "Profiler Recorder 在本运行时不可用。");
            object sink = null;
            Action allocate = () => { for (int i = 0; i < 10; i++) sink = new byte[16]; };
            Action nothing = () => { int x = 0; for (int i = 0; i < 10; i++) x += i; if (x < 0) sink = null; };
            allocate();
            nothing();

            probe.Begin();
            allocate();
            long counted = probe.End();
            probe.Begin();
            nothing();
            long none = probe.End();
            GC.KeepAlive(sink);

            Assert.AreEqual(10, counted, "探针必须是活的：10 次已知分配。");
            Assert.AreEqual(0, none);
        }
    }
}
