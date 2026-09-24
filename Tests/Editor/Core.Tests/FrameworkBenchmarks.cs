//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using EjoyFramework.Core;
using EjoyFramework.Core.Benchmarking;
using EjoyFramework.Core.Diagnostics;
using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Spatial;
using EjoyFramework.Core.Streaming;
using EjoyFramework.Core.Telemetry;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 框架热路径微基准（WS4-M4）。每个基准：<see cref="BenchmarkRunner"/> 测 ns/次，<see cref="UnityAllocationProbe"/> 测分配次数，
    /// 声明为零分配的路径硬断言 0 分配；时间只记录不断言（机器差异）。
    ///
    /// 结果写入工程内 <c>TestResults/Benchmarks/editmode-&lt;UTC&gt;.tsv</c> 与 <c>editmode-latest.tsv</c>；
    /// 若存在 <c>TestResults/Benchmarks/editmode-baseline.tsv</c>（或环境变量 EJOY_BENCH_BASELINE 指定），同时输出对比结论。
    /// 运行：<c>EjoyFramework/Tools~/RunBenchmarks.ps1</c>，或 unity-cli <c>--filter Benchmarks</c>。
    /// </summary>
    [Category("Benchmark")]
    public sealed class FrameworkBenchmarks
    {
        private static readonly BenchmarkOptions s_Options = BenchmarkOptions.Quick();
        private BenchmarkReport m_Report;
        private UnityAllocationProbe m_Probe;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Framework.MarkMainThread();
            m_Probe = new UnityAllocationProbe();
            m_Report = new BenchmarkReport();
            m_Report.SetEnvironment("suite", "EditMode");
            m_Report.SetEnvironment("utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            m_Report.SetEnvironment("unity", Application.unityVersion);
            m_Report.SetEnvironment("os", SystemInfo.operatingSystem);
            m_Report.SetEnvironment("cpu", SystemInfo.processorType + " x" + SystemInfo.processorCount);
            m_Report.SetEnvironment("memory_mb", SystemInfo.systemMemorySize.ToString());
            m_Report.SetEnvironment("alloc_probe", m_Probe.IsAvailable ? "gc-alloc-recorder" : "unavailable");
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (m_Report == null || m_Report.Results.Count == 0) return;
            string dir = Path.GetFullPath(Path.Combine("TestResults", "Benchmarks"));
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            m_Report.Save(Path.Combine(dir, "editmode-" + stamp + ".tsv"));
            m_Report.Save(Path.Combine(dir, "editmode-latest.tsv"));

            string baselinePath = Environment.GetEnvironmentVariable("EJOY_BENCH_BASELINE");
            if (string.IsNullOrEmpty(baselinePath)) baselinePath = Path.Combine(dir, "editmode-baseline.tsv");
            StringBuilder sb = new StringBuilder();
            sb.Append("[Benchmarks] ").Append(m_Report.Results.Count).Append(" 项 → ").Append(dir).Append('\n');
            for (int i = 0; i < m_Report.Results.Count; i++)
            {
                BenchmarkResult r = m_Report.Results[i];
                sb.AppendFormat("  {0,-36} {1,10:F1} ns/op  cv {2:P0}  alloc {3}\n", r.Name, r.MedianNs, r.CoefficientOfVariation,
                    r.AllocMeasured ? r.AllocPerOp.ToString("0.###") : "-");
            }

            if (File.Exists(baselinePath))
            {
                sb.Append("  对比基线 ").Append(baselinePath).Append('\n');
                foreach (BenchmarkDelta d in BenchmarkComparison.Compare(BenchmarkReport.Load(baselinePath), m_Report))
                {
                    if (d.Verdict == BenchmarkVerdict.Same) continue;
                    sb.AppendFormat("  {0,-36} {1} ×{2:F2}\n", d.Name, d.Verdict, d.Ratio);
                }
            }

            Debug.Log(sb.ToString());
        }

        private BenchmarkResult Run(string name, Action<long> body, bool zeroAlloc)
        {
            BenchmarkResult r = BenchmarkRunner.Run(name, body, s_Options, m_Probe);
            m_Report.Add(r);
            if (zeroAlloc && r.AllocMeasured) Assert.AreEqual(0.0, r.AllocPerOp, name + " 声明为零分配，实测 " + r.AllocPerOp + " 次/op。");
            Assert.Greater(r.MedianNs, 0.0);
            return r;
        }

        // ---------------- 池 ----------------

        [Test]
        public void Pool_ListPoolGetRelease()
        {
            Run("Pool.ListPool.GetRelease", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    List<int> list = ListPool<int>.Get();
                    list.Add(1);
                    ListPool<int>.Release(list);
                }
            }, true);
        }

        [Test]
        public void Pool_BufferPoolRentReturn4K()
        {
            Run("Pool.BufferPool.RentReturn4K", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    byte[] buffer = BufferPool<byte>.Rent(4096);
                    buffer[0] = 1;
                    BufferPool<byte>.Return(buffer);
                }
            }, true);
        }

        [Test]
        public void Heap_PushPop64()
        {
            BinaryHeap<int> heap = new BinaryHeap<int>(Comparer<int>.Default, 128);
            Run("Heap.PushPop64", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    for (int k = 0; k < 64; k++) heap.Push((k * 7919) & 1023);
                    while (heap.Count > 0) heap.Pop();
                }
            }, true);
        }

        // ---------------- 空间 / 流送 ----------------

        [Test]
        public void Spatial_QueryRadius_1000Entities()
        {
            SpatialGrid grid = new SpatialGrid(16f);
            System.Random rng = new System.Random(7);
            for (int id = 0; id < 1000; id++) grid.AddOrUpdate(id, (float)rng.NextDouble() * 400f, (float)rng.NextDouble() * 400f);
            List<int> results = new List<int>(256);
            Run("Spatial.QueryRadius20.1000", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    results.Clear();
                    grid.QueryRadius((i * 37) % 400, (i * 91) % 400, 20f, results);
                }
            }, true);
        }

        [Test]
        public void Streaming_ObserverMoveReevaluate_1681Cells()
        {
            WorldStreamingManager streaming = new WorldStreamingManager();
            try
            {
                NullHandler handler = new NullHandler(streaming);
                streaming.SetHandler(handler);
                streaming.CellSize = 10f;
                streaming.ConfigureLayer(0, new StreamingLayerSettings { LoadRadius = 60f, UnloadRadius = 80f });
                streaming.MaxLoadStartsPerFrame = 1000;
                streaming.MaxLoadsInFlight = 1000;
                streaming.MaxUnloadsPerFrame = 1000;
                for (int cx = -20; cx <= 20; cx++)
                    for (int cz = -20; cz <= 20; cz++)
                        streaming.RegisterCell(0, cx, cz, 0);
                float x = 0f;
                Run("Streaming.MoveReevaluate.1681", n =>
                {
                    for (long i = 0; i < n; i++)
                    {
                        x = x > 100f ? -100f : x + 3f;   // 每次移动超过重评估阈值
                        streaming.SetObserver(1, x, 0f);
                        streaming.Update(0.016f, 0.016f);
                    }
                }, true);
            }
            finally
            {
                streaming.Shutdown();
            }
        }

        private sealed class NullHandler : IWorldStreamingHandler
        {
            private readonly WorldStreamingManager m_Mgr;
            public NullHandler(WorldStreamingManager mgr) { m_Mgr = mgr; }
            public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod) { m_Mgr.NotifyLoaded(cellId, true); }
            public void CancelLoad(int cellId) { }
            public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey) { m_Mgr.NotifyUnloaded(cellId); }
            public void OnLodChanged(int cellId, int fromLod, int toLod) { }
        }

        // ---------------- 文本 / LiveOps ----------------

        [Test]
        public void Text_TempTextFormat()
        {
            Run("Text.TempText.Format", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    using (TempText t = TempText.Rent(64))
                    {
                        t.Append("FPS ").Append(59.94f, "F1").Append(" | ").Append((int)i).Append(" ms");
                    }
                }
            }, true);
        }

        [Test]
        public void Telemetry_Record()
        {
            TelemetryManager telemetry = new TelemetryManager();
            try
            {
                telemetry.BatchSize = 256;           // 攒满即封批（无后端：进离线队列，超上限丢最旧），内存有界
                telemetry.FlushIntervalSeconds = 0f;
                telemetry.StartSession("bench", "dev", "1");
                Run("Telemetry.Record", n =>
                {
                    for (long i = 0; i < n; i++)
                    {
                        TelemetryRecord r = default(TelemetryRecord);
                        r.Kind = TelemetryKind.FrameWindow;
                        r.F0 = i;
                        telemetry.Record(ref r);
                    }
                }, false);   // 含每 256 条一次封批；Record 本身的零分配由 TelemetryManagerTests 断言
            }
            finally
            {
                telemetry.Shutdown();
            }
        }

        [Test]
        public void Crash_Fingerprint()
        {
            const string msg = "NullReferenceException: Object reference not set to an instance of an object";
            const string stack = "Game.Enemy.Tick () (at Assets/Game/Enemy.cs:42)\nGame.World.Update () (at Assets/Game/World.cs:10)\n"
                                 + "EjoyFramework.Core.Framework.Update (System.Single, System.Single) (at Packages/x.cs:1)\n";
            uint sink = 0;
            Run("Crash.Fingerprint", n =>
            {
                for (long i = 0; i < n; i++) sink ^= CrashFingerprint.Compute(msg, stack);
            }, true);
            Assert.AreNotEqual(0u, sink | 1u);
        }

        [Test]
        public void Quality_AutoControllerFeed()
        {
            AutoQualityController controller = new AutoQualityController();
            int level = 3;
            Run("Quality.AutoController.Feed", n =>
            {
                for (long i = 0; i < n; i++)
                {
                    AutoQualityDecision d = controller.Feed((i & 1) == 0 ? 20f : 12f, 0.016f, level, 0, 4);
                    if (d == AutoQualityDecision.LevelDown && level > 0) level--;
                    if (d == AutoQualityDecision.LevelUp && level < 4) level++;
                }
            }, true);
        }
    }
}
