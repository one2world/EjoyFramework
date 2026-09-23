//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Core.Telemetry
{
    /// <summary>
    /// <see cref="ITelemetryManager"/> 实现。
    ///
    /// 记录攒在定长 struct 数组里（零分配）；封批时租一次 BufferPool 写成二进制，交给后端；
    /// 后端回调可能来自任意线程 → 结果先进 <see cref="m_Completed"/>（锁保护），主线程 Update 里统一处理
    /// （成功归还缓冲、失败重排队），保证所有状态变更都在主线程。
    /// 采样率用可播种的 xorshift 而不是 System.Random：可测试、无分配。
    /// </summary>
    internal sealed class TelemetryManager : FrameworkModule, ITelemetryManager
    {
        private struct Completion
        {
            public TelemetryBatch Batch;
            public bool Success;
        }

        private readonly object m_Lock = new object();
        private readonly Queue<TelemetryBatch> m_Queue = new Queue<TelemetryBatch>();
        private readonly List<Completion> m_Completed = new List<Completion>();
        private readonly List<Completion> m_CompletedScratch = new List<Completion>();
        private readonly Stack<TelemetryBatch> m_BatchPool = new Stack<TelemetryBatch>();
        private readonly Action<TelemetryBatch, bool> m_OnSendComplete;

        private ITelemetryBackend m_Backend;
        private TelemetryRecord[] m_Pending = new TelemetryRecord[256];
        private int m_PendingCount;
        private bool m_Enabled = true;
        private float m_SampleRate = 1f;
        private int m_BatchSize = 256;
        private float m_FlushInterval = 30f;
        private int m_MaxQueuedBatches = 32;
        private float m_SinceFlush;
        private float m_SessionTime;
        private string m_SessionId;
        private string m_DeviceInfo;
        private string m_AppVersion;
        private bool m_Sampling;
        private bool m_InSession;
        private TelemetryBatch m_InFlight;
        private int m_NextBatchId;
        private uint m_Rng = 0x9E3779B9u;
        private long m_TotalRecords;
        private long m_TotalSent;
        private long m_TotalFailed;
        private long m_TotalDropped;

        public TelemetryManager()
        {
            m_OnSendComplete = OnSendComplete;
        }

        public override int Priority { get { return 95; } }   // 晚于业务：本帧的采样在业务之后写入

        // ---- 配置 ----

        public bool Enabled { get { return m_Enabled; } set { m_Enabled = value; } }
        public float SampleRate { get { return m_SampleRate; } set { m_SampleRate = value < 0f ? 0f : value > 1f ? 1f : value; } }
        public int BatchSize { get { return m_BatchSize; } set { m_BatchSize = value < 1 ? 1 : value; } }
        public float FlushIntervalSeconds { get { return m_FlushInterval; } set { m_FlushInterval = value < 0f ? 0f : value; } }
        public int MaxQueuedBatches { get { return m_MaxQueuedBatches; } set { m_MaxQueuedBatches = value < 1 ? 1 : value; } }

        public void SetBackend(ITelemetryBackend backend)
        {
            m_Backend = backend;
        }

        /// <summary>播种采样骰子（测试与可复现分析用）。</summary>
        public void SeedSampling(uint seed)
        {
            m_Rng = seed == 0 ? 0x9E3779B9u : seed;
        }

        // ---- 会话 ----

        public bool StartSession(string sessionId, string deviceInfo, string appVersion)
        {
            Framework.EnsureMainThread(nameof(StartSession));
            if (string.IsNullOrEmpty(sessionId)) throw new FrameworkException("Telemetry：sessionId 不能为空。");
            if (m_InSession) EndSession();
            m_SessionId = sessionId;
            m_DeviceInfo = deviceInfo ?? string.Empty;
            m_AppVersion = appVersion ?? string.Empty;
            m_SessionTime = 0f;
            m_SinceFlush = 0f;
            m_InSession = true;
            m_Sampling = m_SampleRate >= 1f || (m_SampleRate > 0f && NextUnit() < m_SampleRate);
            return m_Sampling;
        }

        public void EndSession()
        {
            Framework.EnsureMainThread(nameof(EndSession));
            if (!m_InSession) return;
            Flush();
            m_InSession = false;
            m_Sampling = false;
        }

        public bool IsSampling { get { return m_InSession && m_Sampling && m_Enabled; } }

        // ---- 记录 ----

        public void Record(ref TelemetryRecord record)
        {
            if (!IsSampling) return;
            if (m_PendingCount == m_Pending.Length) Array.Resize(ref m_Pending, m_Pending.Length * 2);
            record.Timestamp = m_SessionTime;
            m_Pending[m_PendingCount++] = record;
            m_TotalRecords++;
            if (m_PendingCount >= m_BatchSize) SealBatch();
        }

        public void Flush()
        {
            Framework.EnsureMainThread(nameof(Flush));
            SealBatch();
            TrySend();
            ProcessCompletions();
        }

        // ---- 驱动 ----

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_InSession)
            {
                m_SessionTime += realElapseSeconds;
                m_SinceFlush += realElapseSeconds;
                if (m_FlushInterval > 0f && m_SinceFlush >= m_FlushInterval && m_PendingCount > 0)
                {
                    SealBatch();
                }
            }

            ProcessCompletions();
            TrySend();
            ProcessCompletions();   // 同步完成的后端：本帧内就结算，无需等到下一帧
        }

        private void SealBatch()
        {
            m_SinceFlush = 0f;
            if (m_PendingCount == 0) return;

            TelemetryBatch batch = m_BatchPool.Count > 0 ? m_BatchPool.Pop() : new TelemetryBatch();
            int payloadSize = 64 + Utf8Length(m_SessionId) + Utf8Length(m_DeviceInfo) + Utf8Length(m_AppVersion) + m_PendingCount * TelemetryRecord.SerializedSize;
            batch.Payload = BufferPool<byte>.Rent(payloadSize);
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                buffer.WriteUInt(TelemetryBatch.Magic);
                buffer.WriteInt(TelemetryBatch.FormatVersion);
                buffer.WriteString(m_SessionId);
                buffer.WriteString(m_DeviceInfo);
                buffer.WriteString(m_AppVersion);
                buffer.WriteInt(m_PendingCount);
                for (int i = 0; i < m_PendingCount; i++) m_Pending[i].WriteTo(buffer);
                if (buffer.Length > batch.Payload.Length)
                {
                    BufferPool<byte>.Return(batch.Payload);
                    batch.Payload = BufferPool<byte>.Rent(buffer.Length);
                }

                Array.Copy(buffer.RawBuffer, 0, batch.Payload, 0, buffer.Length);
                batch.Length = buffer.Length;
            }
            finally
            {
                buffer.Release();
            }

            batch.BatchId = ++m_NextBatchId;
            batch.SessionId = m_SessionId;
            batch.RecordCount = m_PendingCount;
            batch.Attempts = 0;
            m_PendingCount = 0;

            lock (m_Lock)
            {
                m_Queue.Enqueue(batch);
                while (m_Queue.Count > m_MaxQueuedBatches)
                {
                    TelemetryBatch dropped = m_Queue.Dequeue();
                    m_TotalDropped++;
                    Recycle(dropped);
                }
            }
        }

        private void TrySend()
        {
            if (m_Backend == null || m_InFlight != null) return;
            TelemetryBatch next;
            lock (m_Lock)
            {
                if (m_Queue.Count == 0) return;
                next = m_Queue.Dequeue();
            }

            m_InFlight = next;
            next.Attempts++;
            try
            {
                m_Backend.Send(next, m_OnSendComplete);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("Telemetry：后端 Send 抛出异常：{0}", ex);
                OnSendComplete(next, false);
            }
        }

        private void OnSendComplete(TelemetryBatch batch, bool success)
        {
            // 任意线程：只入队，主线程 Update 处理
            lock (m_Lock)
            {
                Completion c;
                c.Batch = batch;
                c.Success = success;
                m_Completed.Add(c);
            }
        }

        private void ProcessCompletions()
        {
            lock (m_Lock)
            {
                if (m_Completed.Count == 0) return;
                m_CompletedScratch.AddRange(m_Completed);
                m_Completed.Clear();
            }

            for (int i = 0; i < m_CompletedScratch.Count; i++)
            {
                Completion c = m_CompletedScratch[i];
                if (c.Batch == m_InFlight) m_InFlight = null;
                if (c.Success)
                {
                    m_TotalSent++;
                    Recycle(c.Batch);
                }
                else
                {
                    m_TotalFailed++;
                    lock (m_Lock)
                    {
                        // 失败的批次排回队尾；队列满时丢弃它自己（它是最"旧"的）
                        if (m_Queue.Count >= m_MaxQueuedBatches)
                        {
                            m_TotalDropped++;
                            Recycle(c.Batch);
                        }
                        else
                        {
                            m_Queue.Enqueue(c.Batch);
                        }
                    }
                }
            }

            m_CompletedScratch.Clear();
        }

        private void Recycle(TelemetryBatch batch)
        {
            if (batch.Payload != null) BufferPool<byte>.Return(batch.Payload);
            batch.Payload = null;
            batch.Length = 0;
            batch.RecordCount = 0;
            batch.SessionId = null;
            m_BatchPool.Push(batch);
        }

        // ---- 状态 ----

        public int QueuedBatchCount
        {
            get
            {
                lock (m_Lock) { return m_Queue.Count + (m_InFlight != null ? 1 : 0); }
            }
        }

        public int PendingRecordCount { get { return m_PendingCount; } }
        public long TotalRecords { get { return m_TotalRecords; } }
        public long TotalBatchesSent { get { return m_TotalSent; } }
        public long TotalBatchesFailed { get { return m_TotalFailed; } }
        public long TotalBatchesDropped { get { return m_TotalDropped; } }

        public override void Shutdown()
        {
            if (m_InSession) EndSession();
            ProcessCompletions();   // 已完成的批次归还缓冲
            lock (m_Lock)
            {
                while (m_Queue.Count > 0) Recycle(m_Queue.Dequeue());
                m_Completed.Clear();
            }

            m_InFlight = null;
            m_Backend = null;
            m_PendingCount = 0;
        }

        // ---- 内部 ----

        private float NextUnit()
        {
            m_Rng ^= m_Rng << 13;
            m_Rng ^= m_Rng >> 17;
            m_Rng ^= m_Rng << 5;
            return (m_Rng & 0xFFFFFF) / 16777216f;
        }

        private static int Utf8Length(string s)
        {
            return string.IsNullOrEmpty(s) ? 4 : 4 + System.Text.Encoding.UTF8.GetByteCount(s);
        }
    }
}
