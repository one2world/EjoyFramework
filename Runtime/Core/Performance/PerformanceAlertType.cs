//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能预警类型。
    /// </summary>
    public enum PerformanceAlertType
    {
        /// <summary>滑动窗口平均帧率低于 FpsWarningThreshold。</summary>
        LowFps         = 0,
        /// <summary>滑动窗口平均帧率低于 FpsCriticalThreshold。</summary>
        CriticalFps    = 1,
        /// <summary>托管内存超出 MemoryWarningBytes。</summary>
        HighMemory     = 2,
        /// <summary>托管内存超出 MemoryCriticalBytes。</summary>
        CriticalMemory = 3,
        /// <summary>单帧耗时超过 SpikeThresholdSeconds。</summary>
        FrameSpike     = 4,
        /// <summary>性能等级上升（帧率持续改善达到阈值）。</summary>
        LevelImproved  = 5,
        /// <summary>性能等级下降（帧率跌破降级阈值）。</summary>
        LevelDegraded  = 6,
    }
}
