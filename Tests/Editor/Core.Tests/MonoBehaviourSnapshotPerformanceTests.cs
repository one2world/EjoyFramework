//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Unity;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EjoyFramework.Tests
{
    [TestFixture]
    public sealed class MonoBehaviourSnapshotPerformanceTests
    {
        private const int WarmupIterations = 128;
        private const int GeneratedIterations = 4096;
        private const int EndToEndIterations = 512;

        // Editor-mode budgets are intentionally loose. The JSON report carries the exact measurements.
        private const double MaxGeneratedCaptureMicroseconds = 2000d;
        private const double MaxGeneratedRestoreMicroseconds = 5000d;
        private const double MaxEndToEndRoundTripMicroseconds = 10000d;
        private const double MaxGeneratedCaptureGcAllocEvents = 0.01d;
        private const double MaxGeneratedRestoreGcAllocEvents = 256d;
        private const double MaxEndToEndRoundTripGcAllocEvents = 512d;

        [Test]
        public void GeneratedArenaSnapshot_RuntimePerformanceBudget()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                MonoBehaviourSnapshotSampleResourceBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Missing prefab resource: "
                + MonoBehaviourSnapshotSampleResourceBuilder.PrefabPath);

            GameObject source = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObject target = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            try
            {
                MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap sourceMap
                    = MonoBehaviourSnapshotSampleResourceBuilder.CreateObjectMap(source);
                MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap targetMap
                    = MonoBehaviourSnapshotSampleResourceBuilder.CreateObjectMap(target);
                var sourceContext = new MonoBehaviourSnapshotContext(sourceMap);
                var targetContext = new MonoBehaviourSnapshotContext(targetMap);

                byte[] snapshot = CaptureToArray(sourceMap, sourceContext);
                Assert.That(snapshot.Length, Is.GreaterThan(0));

                OperationReport generatedCapture;
                ByteBuffer captureBuffer = ByteBuffer.Acquire();
                try
                {
                    // Grow once before measurement so the capture hot path is measured without buffer expansion noise.
                    CaptureGenerated(sourceMap, sourceContext, captureBuffer);
                    generatedCapture = Measure(
                        "generated_capture_reused_buffer",
                        WarmupIterations,
                        GeneratedIterations,
                        () =>
                        {
                            captureBuffer.Reset();
                            CaptureGenerated(sourceMap, sourceContext, captureBuffer);
                        });
                }
                finally
                {
                    captureBuffer.Release();
                }

                OperationReport generatedRestore;
                ByteBuffer restoreBuffer = ByteBuffer.Acquire(snapshot);
                try
                {
                    generatedRestore = Measure(
                        "generated_restore_reused_snapshot",
                        WarmupIterations,
                        GeneratedIterations,
                        () =>
                        {
                            restoreBuffer.Wrap(snapshot);
                            RestoreGenerated(targetMap, targetContext, restoreBuffer);
                        });
                }
                finally
                {
                    restoreBuffer.Release();
                }

                OperationReport endToEndRoundTrip = Measure(
                    "resource_helper_capture_restore_roundtrip",
                    WarmupIterations,
                    EndToEndIterations,
                    () =>
                    {
                        byte[] bytes = MonoBehaviourSnapshotSampleResourceBuilder.CaptureArenaSnapshot(source);
                        MonoBehaviourSnapshotSampleResourceBuilder.RestoreArenaSnapshot(target, bytes);
                    });

                var report = new PerformanceReport
                {
                    unityVersion = Application.unityVersion,
                    snapshotBytes = snapshot.Length,
                    warmupIterations = WarmupIterations,
                    generatedIterations = GeneratedIterations,
                    endToEndIterations = EndToEndIterations,
                    generatedCapture = generatedCapture,
                    generatedRestore = generatedRestore,
                    endToEndRoundTrip = endToEndRoundTrip,
                };

                string reportPath = WriteReport(report);
                WriteTestLog(JsonUtility.ToJson(report, true));
                WriteTestLog("MonoBehaviour snapshot performance report: " + reportPath);

                Assert.That(generatedCapture.microsecondsPerOperation, Is.LessThan(MaxGeneratedCaptureMicroseconds));
                Assert.That(generatedCapture.gcAllocEventsPerOperation,
                    Is.LessThanOrEqualTo(MaxGeneratedCaptureGcAllocEvents));
                Assert.That(generatedRestore.microsecondsPerOperation, Is.LessThan(MaxGeneratedRestoreMicroseconds));
                Assert.That(generatedRestore.gcAllocEventsPerOperation,
                    Is.LessThanOrEqualTo(MaxGeneratedRestoreGcAllocEvents));
                Assert.That(endToEndRoundTrip.microsecondsPerOperation, Is.LessThan(MaxEndToEndRoundTripMicroseconds));
                Assert.That(endToEndRoundTrip.gcAllocEventsPerOperation,
                    Is.LessThanOrEqualTo(MaxEndToEndRoundTripGcAllocEvents));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        private static byte[] CaptureToArray(
            MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap map,
            MonoBehaviourSnapshotContext context)
        {
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                CaptureGenerated(map, context, buffer);
                return buffer.ToArray();
            }
            finally
            {
                buffer.Release();
            }
        }

        private static void CaptureGenerated(
            MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap map,
            MonoBehaviourSnapshotContext context,
            ByteBuffer buffer)
        {
            ((IMonoBehaviourSnapshot)map.Hero).CaptureSnapshot(buffer, context);
            ((IMonoBehaviourSnapshot)map.Boss).CaptureSnapshot(buffer, context);
            ((IMonoBehaviourSnapshot)map.Hub).CaptureSnapshot(buffer, context);
        }

        private static void RestoreGenerated(
            MonoBehaviourSnapshotSampleResourceBuilder.SnapshotSampleObjectMap map,
            MonoBehaviourSnapshotContext context,
            ByteBuffer buffer)
        {
            ((IMonoBehaviourSnapshot)map.Hero).RestoreSnapshot(buffer, context);
            ((IMonoBehaviourSnapshot)map.Boss).RestoreSnapshot(buffer, context);
            ((IMonoBehaviourSnapshot)map.Hub).RestoreSnapshot(buffer, context);
        }

        private static OperationReport Measure(string name, int warmupIterations, int iterations, Action action)
        {
            for (int i = 0; i < warmupIterations; i++)
            {
                action();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var options = ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                | ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using (var gcAllocRecorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1, options))
            {
                Assert.That(gcAllocRecorder.Valid, Is.True, "GC.Alloc ProfilerRecorder must be valid.");
                gcAllocRecorder.Start();

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    action();
                }
                stopwatch.Stop();
                gcAllocRecorder.Stop();

                long gcAllocEvents = gcAllocRecorder.Count > 0 ? gcAllocRecorder.GetSample(0).Count : 0;

                double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return new OperationReport
                {
                    name = name,
                    iterations = iterations,
                    totalMilliseconds = totalMilliseconds,
                    microsecondsPerOperation = totalMilliseconds * 1000d / iterations,
                    totalGcAllocEvents = gcAllocEvents,
                    gcAllocEventsPerOperation = (double)gcAllocEvents / iterations,
                };
            }
        }

        private static string WriteReport(PerformanceReport report)
        {
            string dir = Path.Combine("Temp", "EjoyFramework");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "MonoBehaviourSnapshotPerformance.latest.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            return Path.GetFullPath(path);
        }

        private static void WriteTestLog(string message)
        {
            try
            {
                TestContext.WriteLine(message);
            }
            catch (NullReferenceException)
            {
                UnityEngine.Debug.Log(message);
            }
        }

        [Serializable]
        private sealed class PerformanceReport
        {
            public string unityVersion;
            public int snapshotBytes;
            public int warmupIterations;
            public int generatedIterations;
            public int endToEndIterations;
            public OperationReport generatedCapture;
            public OperationReport generatedRestore;
            public OperationReport endToEndRoundTrip;
        }

        [Serializable]
        private struct OperationReport
        {
            public string name;
            public int iterations;
            public double totalMilliseconds;
            public double microsecondsPerOperation;
            public long totalGcAllocEvents;
            public double gcAllocEventsPerOperation;
        }
    }
}
