//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Tests
{
    /// <summary>WS4-M1：遥测——采样率、封批与二进制格式、离线队列/重试/丢弃、跨线程完成回报、零分配记录。</summary>
    public sealed class TelemetryManagerTests
    {
        private sealed class FakeBackend : ITelemetryBackend
        {
            public readonly List<TelemetryBatch> Sent = new List<TelemetryBatch>();
            public readonly List<byte[]> Payloads = new List<byte[]>();
            public bool Hold;
            public bool Fail;
            private Action<TelemetryBatch, bool> m_Pending;
            private TelemetryBatch m_PendingBatch;

            public void Send(TelemetryBatch batch, Action<TelemetryBatch, bool> onComplete)
            {
                Sent.Add(batch);
                byte[] copy = new byte[batch.Length];
                Array.Copy(batch.Payload, copy, batch.Length);
                Payloads.Add(copy);
                if (Hold) { m_Pending = onComplete; m_PendingBatch = batch; return; }
                onComplete(batch, !Fail);
            }

            public void CompleteHeld(bool success)
            {
                var cb = m_Pending; var b = m_PendingBatch;
                m_Pending = null; m_PendingBatch = null;
                cb?.Invoke(b, success);
            }

            public void CompleteHeldOnWorkerThread(bool success)
            {
                var cb = m_Pending; var b = m_PendingBatch;
                m_Pending = null; m_PendingBatch = null;
                var t = new Thread(() => cb?.Invoke(b, success));
                t.Start();
                t.Join();
            }
        }

        private TelemetryManager m_Mgr;
        private FakeBackend m_Backend;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Mgr = new TelemetryManager();
            m_Backend = new FakeBackend();
            m_Mgr.SetBackend(m_Backend);
            m_Mgr.BatchSize = 4;
        }

        [TearDown]
        public void TearDown()
        {
            m_Mgr.Shutdown();
        }

        private static TelemetryRecord Frame(float avgMs)
        {
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.FrameWindow;
            r.F0 = avgMs;
            r.I0 = 600;
            return r;
        }

        [Test]
        public void SessionSampling_RespectsRate_AndIsSeedable()
        {
            m_Mgr.SampleRate = 0f;
            Assert.IsFalse(m_Mgr.StartSession("s0", "dev", "1.0"));
            m_Mgr.SampleRate = 1f;
            Assert.IsTrue(m_Mgr.StartSession("s1", "dev", "1.0"));

            m_Mgr.SampleRate = 0.5f;
            int sampled = 0;
            m_Mgr.SeedSampling(12345);
            for (int i = 0; i < 1000; i++) if (m_Mgr.StartSession("s" + i, "dev", "1.0")) sampled++;
            Assert.Greater(sampled, 400);
            Assert.Less(sampled, 600);

            m_Mgr.SeedSampling(12345);
            int again = 0;
            for (int i = 0; i < 1000; i++) if (m_Mgr.StartSession("s" + i, "dev", "1.0")) again++;
            Assert.AreEqual(sampled, again, "同种子应可复现。");
        }

        [Test]
        public void Disabled_OrUnsampled_DropsRecords()
        {
            m_Mgr.SampleRate = 0f;
            m_Mgr.StartSession("s", "dev", "1.0");
            TelemetryRecord r = Frame(16f);
            m_Mgr.Record(ref r);
            Assert.AreEqual(0, m_Mgr.PendingRecordCount);

            m_Mgr.SampleRate = 1f;
            m_Mgr.StartSession("s2", "dev", "1.0");
            m_Mgr.Enabled = false;
            m_Mgr.Record(ref r);
            Assert.AreEqual(0, m_Mgr.PendingRecordCount);
            Assert.IsFalse(m_Mgr.IsSampling);
        }

        [Test]
        public void BatchSize_SealsAndSends_WithParseableBinary()
        {
            m_Mgr.StartSession("sess-1", "device-x", "2.3.4");
            for (int i = 0; i < 4; i++) { TelemetryRecord r = Frame(10f + i); m_Mgr.Record(ref r); }
            Assert.AreEqual(0, m_Mgr.PendingRecordCount, "达到 BatchSize 应立即封批。");
            m_Mgr.Update(0f, 0f);   // 发送 + 处理完成
            Assert.AreEqual(1, m_Backend.Sent.Count);
            Assert.AreEqual(1, m_Mgr.TotalBatchesSent);
            Assert.AreEqual(4, m_Mgr.TotalRecords);

            ByteBuffer buffer = ByteBuffer.Acquire(m_Backend.Payloads[0]);
            Assert.AreEqual(TelemetryBatch.Magic, buffer.ReadUInt());
            Assert.AreEqual(TelemetryBatch.FormatVersion, buffer.ReadInt());
            Assert.AreEqual("sess-1", buffer.ReadString());
            Assert.AreEqual("device-x", buffer.ReadString());
            Assert.AreEqual("2.3.4", buffer.ReadString());
            Assert.AreEqual(4, buffer.ReadInt());
            TelemetryRecord parsed = default(TelemetryRecord);
            parsed.ReadFrom(buffer);
            Assert.AreEqual(TelemetryKind.FrameWindow, parsed.Kind);
            Assert.AreEqual(10f, parsed.F0);
            Assert.AreEqual(600, parsed.I0);
            for (int i = 1; i < 4; i++) parsed.ReadFrom(buffer);
            Assert.AreEqual(13f, parsed.F0);
            Assert.AreEqual(0, buffer.Remaining, "批次尾部不应有多余字节。");
            buffer.Release();
        }

        [Test]
        public void FlushInterval_SealsPartialBatch()
        {
            m_Mgr.FlushIntervalSeconds = 5f;
            m_Mgr.StartSession("s", "d", "v");
            TelemetryRecord r = Frame(1f);
            m_Mgr.Record(ref r);
            m_Mgr.Update(3f, 3f);
            Assert.AreEqual(1, m_Mgr.PendingRecordCount);
            m_Mgr.Update(3f, 3f);
            Assert.AreEqual(0, m_Mgr.PendingRecordCount);
            Assert.AreEqual(1, m_Backend.Sent.Count);
        }

        [Test]
        public void Failure_RequeuesAndRetries_DroppedWhenQueueFull()
        {
            m_Mgr.MaxQueuedBatches = 2;
            m_Backend.Fail = true;
            m_Mgr.StartSession("s", "d", "v");
            for (int b = 0; b < 3; b++)
            {
                for (int i = 0; i < 4; i++) { TelemetryRecord r = Frame(1f); m_Mgr.Record(ref r); }
            }

            for (int i = 0; i < 6; i++) m_Mgr.Update(0f, 0f);
            Assert.Greater(m_Mgr.TotalBatchesFailed, 0);
            Assert.Greater(m_Mgr.TotalBatchesDropped, 0, "队列满时应丢弃。");
            Assert.LessOrEqual(m_Mgr.QueuedBatchCount, 3);

            m_Backend.Fail = false;
            for (int i = 0; i < 6; i++) m_Mgr.Update(0f, 0f);
            Assert.AreEqual(0, m_Mgr.QueuedBatchCount, "后端恢复后应把队列发完。");
            Assert.Greater(m_Mgr.TotalBatchesSent, 0);
        }

        [Test]
        public void Completion_FromWorkerThread_IsProcessedOnMainThread()
        {
            m_Backend.Hold = true;
            m_Mgr.StartSession("s", "d", "v");
            for (int i = 0; i < 4; i++) { TelemetryRecord r = Frame(1f); m_Mgr.Record(ref r); }
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(1, m_Mgr.QueuedBatchCount, "在途批次计入队列数。");
            Assert.AreEqual(0, m_Mgr.TotalBatchesSent);

            m_Backend.CompleteHeldOnWorkerThread(true);
            Assert.AreEqual(0, m_Mgr.TotalBatchesSent, "完成回报在 Update 前不应改变主线程状态。");
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(1, m_Mgr.TotalBatchesSent);
            Assert.AreEqual(0, m_Mgr.QueuedBatchCount);
        }

        [Test]
        public void OnlyOneBatchInFlight_AtATime()
        {
            m_Backend.Hold = true;
            m_Mgr.StartSession("s", "d", "v");
            for (int i = 0; i < 8; i++) { TelemetryRecord r = Frame(1f); m_Mgr.Record(ref r); }
            m_Mgr.Update(0f, 0f);
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(1, m_Backend.Sent.Count, "上一批未完成不应再发。");
            m_Backend.CompleteHeld(true);
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(2, m_Backend.Sent.Count);
        }

        [Test]
        public void EndSession_FlushesRemaining()
        {
            m_Mgr.StartSession("s", "d", "v");
            TelemetryRecord r = Frame(1f);
            m_Mgr.Record(ref r);
            m_Mgr.EndSession();
            Assert.AreEqual(1, m_Backend.Sent.Count);
            Assert.IsFalse(m_Mgr.IsSampling);
        }

        [Test]
        public void BackendThrowing_IsTreatedAsFailure()
        {
            var throwing = new ThrowingBackend();
            m_Mgr.SetBackend(throwing);
            m_Mgr.StartSession("s", "d", "v");
            for (int i = 0; i < 4; i++) { TelemetryRecord r = Frame(1f); m_Mgr.Record(ref r); }
            Assert.DoesNotThrow(() => m_Mgr.Update(0f, 0f));
            m_Mgr.Update(0f, 0f);
            Assert.Greater(m_Mgr.TotalBatchesFailed, 0);
        }

        [Test]
        public void Record_SteadyState_DoesNotAllocate()
        {
            m_Mgr.BatchSize = 1000;
            m_Mgr.StartSession("s", "d", "v");
            TestDelegate body = () =>
            {
                for (int i = 0; i < 64; i++) { TelemetryRecord r = Frame(i); m_Mgr.Record(ref r); }
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        private sealed class ThrowingBackend : ITelemetryBackend
        {
            public void Send(TelemetryBatch batch, Action<TelemetryBatch, bool> onComplete) { throw new InvalidOperationException("boom"); }
        }
    }
}
