//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能预警事件数据。
    /// 值类型（struct），handler 调用时栈上传递，零 GC 分配。
    /// </summary>
    public struct PerformanceAlert
    {
        /// <summary>预警类型。</summary>
        public PerformanceAlertType Type;

        /// <summary>
        /// 触发预警时的实测值：
        /// FPS 类预警 → 平均 FPS；
        /// 内存类预警 → 当前托管内存（MB）；
        /// FrameSpike → 该帧耗时（ms）；
        /// Level 类预警 → 触发时的平均 FPS。
        /// </summary>
        public float Value;

        /// <summary>
        /// 触发预警的阈值（与 Value 同单位）。
        /// Level 类预警此字段为 0（等级变化不基于单一阈值）。
        /// </summary>
        public float Threshold;

        /// <summary>预警发生时刻的当前性能等级。</summary>
        public PerformanceLevel Level;

        /// <summary>预警发生时的累计真实经过时间（秒，自模块第一次 Update 起累计）。</summary>
        public float RealTime;
    }
}
