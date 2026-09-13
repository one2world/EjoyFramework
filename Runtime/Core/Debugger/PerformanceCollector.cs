//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Debugger
{
    /// <summary>
    /// 性能采样器（核心层，无 Unity 依赖）。
    /// 维护一个固定容量的环形缓冲区，记录每帧 deltaTime 与 managedMemory（业务可扩展更多维度），
    /// 提供 min / avg / max / p95 / p99 等聚合统计；适合 Debugger 折线图与 spike 报警。
    ///
    /// 使用方式（由 Unity 层 MonoBehaviour 的 Update 驱动）：
    /// <code>
    /// collector.Sample(Time.unscaledDeltaTime, GC.GetTotalMemory(false));
    /// var s = collector.Snapshot;
    /// if (s.MaxDeltaTime > 0.0333f) Debug.LogWarning("frame spike");
    /// </code>
    /// </summary>
    public sealed class PerformanceCollector
    {
        private const int DefaultCapacity = 240;   // ~4s @ 60fps

        private readonly int m_Capacity;
        private readonly float[] m_DeltaSeconds;
        private readonly long[] m_ManagedMemory;
        private int m_Head;          // 下一个写入位置
        private int m_FilledCount;   // 已写入样本数（最大 = m_Capacity）
        private int m_SpikeFrameCount;
        private float m_SpikeThresholdSeconds = 1f / 30f;  // 30fps = ~33.3ms 算 spike

        // Snapshot cache: ComputeSnapshot allocates a temp float[] + Array.Sort, so recomputing it on
        // every OnGUI repaint (multiple per frame) is wasteful. Cache the result and only recompute
        // when new samples have arrived since the last computation. A reusable sort buffer avoids the
        // per-call float[] allocation.
        private PerformanceSnapshot m_CachedSnapshot;
        private bool m_SnapshotDirty = true;
        private float[] m_SortBuffer;

        /// <summary>
        /// 创建采样器，capacity = 滑动窗口最大样本数（默认 240，~4 秒 @60fps）。
        /// </summary>
        public PerformanceCollector(int capacity = DefaultCapacity)
        {
            if (capacity < 8) throw new FrameworkException("PerformanceCollector capacity must be >= 8.");
            m_Capacity = capacity;
            m_DeltaSeconds = new float[capacity];
            m_ManagedMemory = new long[capacity];
        }

        /// <summary>滑动窗口最大样本数。</summary>
        public int Capacity { get { return m_Capacity; } }

        /// <summary>当前已记录的样本数（达到 Capacity 后稳定）。</summary>
        public int FilledCount { get { return m_FilledCount; } }

        /// <summary>累计的 "spike" 帧数（deltaTime &gt; SpikeThresholdSeconds）。</summary>
        public int SpikeFrameCount { get { return m_SpikeFrameCount; } }

        /// <summary>判定 spike 的阈值（秒）。默认 1/30 = ~33.3ms（&lt;30fps）。</summary>
        public float SpikeThresholdSeconds
        {
            get { return m_SpikeThresholdSeconds; }
            set { if (value > 0f) m_SpikeThresholdSeconds = value; }
        }

        /// <summary>采集一帧样本。</summary>
        public void Sample(float deltaSeconds, long managedMemoryBytes)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;
            if (managedMemoryBytes < 0) managedMemoryBytes = 0;

            m_DeltaSeconds[m_Head] = deltaSeconds;
            m_ManagedMemory[m_Head] = managedMemoryBytes;
            m_Head = (m_Head + 1) % m_Capacity;
            if (m_FilledCount < m_Capacity) m_FilledCount++;

            if (deltaSeconds > m_SpikeThresholdSeconds) m_SpikeFrameCount++;
            m_SnapshotDirty = true;   // invalidate cache; recompute lazily on next Snapshot read
        }

        /// <summary>重置：清空所有样本与 spike 计数。</summary>
        public void Reset()
        {
            m_Head = 0;
            m_FilledCount = 0;
            m_SpikeFrameCount = 0;
            m_SnapshotDirty = true;
        }

        /// <summary>
        /// 取统计快照（拷贝；调用方可安全 read）。
        /// 结果按采样 tick 缓存：仅当自上次计算后有新样本（或 Reset）时才重新计算，
        /// 避免每次 OnGUI 重绘都触发一次 float[] 分配 + Array.Sort。
        /// </summary>
        public PerformanceSnapshot Snapshot
        {
            get
            {
                if (m_SnapshotDirty)
                {
                    m_CachedSnapshot = ComputeSnapshot();
                    m_SnapshotDirty = false;
                }
                return m_CachedSnapshot;
            }
        }

        /// <summary>把窗口里的 deltaTime 拷贝到 buffer（最旧 → 最新顺序）。</summary>
        /// <returns>写入的元素数（= FilledCount）。</returns>
        public int CopyDeltaSeconds(float[] buffer)
        {
            if (buffer == null) throw new FrameworkException("buffer is null.");
            int n = m_FilledCount;
            int start = m_FilledCount == m_Capacity ? m_Head : 0;
            for (int i = 0; i < n && i < buffer.Length; i++)
            {
                buffer[i] = m_DeltaSeconds[(start + i) % m_Capacity];
            }
            return Math.Min(n, buffer.Length);
        }

        private PerformanceSnapshot ComputeSnapshot()
        {
            var s = new PerformanceSnapshot
            {
                Capacity = m_Capacity,
                SampleCount = m_FilledCount,
                SpikeFrameCount = m_SpikeFrameCount,
                SpikeThresholdSeconds = m_SpikeThresholdSeconds,
            };
            if (m_FilledCount == 0) return s;

            // deltaTime 统计
            float minDt = float.MaxValue, maxDt = 0f, sumDt = 0f;
            long minMem = long.MaxValue, maxMem = 0L, sumMem = 0L;
            // 拷贝到复用排序缓冲以计算 p95 / p99（需排序，避免破坏原 buffer）。
            // 缓冲一次性分配为 Capacity 大小，跨调用复用，避免每次快照都 new float[]。
            if (m_SortBuffer == null || m_SortBuffer.Length < m_Capacity) m_SortBuffer = new float[m_Capacity];
            float[] dts = m_SortBuffer;
            int idx = 0;
            int start = m_FilledCount == m_Capacity ? m_Head : 0;
            for (int i = 0; i < m_FilledCount; i++)
            {
                float dt = m_DeltaSeconds[(start + i) % m_Capacity];
                long mem = m_ManagedMemory[(start + i) % m_Capacity];
                dts[idx++] = dt;
                if (dt < minDt) minDt = dt;
                if (dt > maxDt) maxDt = dt;
                sumDt += dt;
                if (mem < minMem) minMem = mem;
                if (mem > maxMem) maxMem = mem;
                sumMem += mem;
            }
            // Sort only the filled range — the buffer may be larger than m_FilledCount (reused/Capacity-sized).
            Array.Sort(dts, 0, m_FilledCount);
            s.MinDeltaTime = minDt;
            s.AvgDeltaTime = sumDt / m_FilledCount;
            s.MaxDeltaTime = maxDt;
            s.P95DeltaTime = Percentile(dts, m_FilledCount, 0.95f);
            s.P99DeltaTime = Percentile(dts, m_FilledCount, 0.99f);
            s.MinManagedMemory = minMem;
            s.AvgManagedMemory = sumMem / m_FilledCount;
            s.MaxManagedMemory = maxMem;
            return s;
        }

        private static float Percentile(float[] sortedAsc, int n, float pct)
        {
            // pct ∈ (0,1)；NIST R-3 ceil-based rank（inclusive 语义：spike 在 1% 异常时 p99 能包含）
            // n = sortedAsc 中有效（已排序）元素数；buffer 可能比 n 大（复用），故显式传入长度。
            if (n <= 0) return 0f;
            int rank = (int)Math.Ceiling(pct * (n - 1));
            if (rank < 0) rank = 0;
            if (rank >= n) rank = n - 1;
            return sortedAsc[rank];
        }
    }

    /// <summary>采样统计快照。</summary>
    public struct PerformanceSnapshot
    {
        public int Capacity;
        public int SampleCount;
        public int SpikeFrameCount;
        public float SpikeThresholdSeconds;

        public float MinDeltaTime;
        public float AvgDeltaTime;
        public float MaxDeltaTime;
        public float P95DeltaTime;
        public float P99DeltaTime;

        public long MinManagedMemory;
        public long AvgManagedMemory;
        public long MaxManagedMemory;

        /// <summary>1 / AvgDeltaTime；deltaTime 为 0 时返回 0。</summary>
        public float AvgFps { get { return AvgDeltaTime > 0f ? 1f / AvgDeltaTime : 0f; } }
    }
}
