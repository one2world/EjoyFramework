//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能统计快照（值类型，调用方可安全持有/拷贝）。
    /// 由 <see cref="IPerformanceManager.Snapshot"/> 返回；结果按采样 tick 缓存，
    /// 有新帧到来后首次访问时重算，避免多次 OnGUI 重绘重复触发 float[] 分配与 Array.Sort。
    /// </summary>
    public struct PerformanceMetrics
    {
        /// <summary>环形缓冲容量（样本帧数）。</summary>
        public int Capacity;
        /// <summary>已采集的有效样本数（达到 Capacity 后稳定）。</summary>
        public int SampleCount;
        /// <summary>累计的 spike 帧数（deltaTime > SpikeThresholdSeconds）。</summary>
        public int SpikeFrameCount;
        /// <summary>当前 spike 判定阈值（秒）。</summary>
        public float SpikeThresholdSeconds;

        // ---- 帧时间（单位：秒）----
        public float MinDeltaTime;
        public float AvgDeltaTime;
        public float MaxDeltaTime;
        /// <summary>95th 百分位帧时间（NIST R-3 ceil-rank）。</summary>
        public float P95DeltaTime;
        /// <summary>99th 百分位帧时间。</summary>
        public float P99DeltaTime;

        // ---- 托管内存（单位：字节）----
        public long MinManagedMemory;
        public long AvgManagedMemory;
        public long MaxManagedMemory;

        // ---- 衍生值 ----

        /// <summary>1 / AvgDeltaTime；deltaTime 为 0 时返回 0。</summary>
        public float AvgFps { get { return AvgDeltaTime > 0f ? 1f / AvgDeltaTime : 0f; } }

        /// <summary>基于最优帧时间得到的峰值 FPS（1 / MinDeltaTime）。</summary>
        public float MaxFps { get { return MinDeltaTime > 0f ? 1f / MinDeltaTime : 0f; } }

        /// <summary>基于最差帧时间得到的谷值 FPS（1 / MaxDeltaTime）。</summary>
        public float MinFps { get { return MaxDeltaTime > 0f ? 1f / MaxDeltaTime : 0f; } }
    }
}
