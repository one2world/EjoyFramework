//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能管理器接口。
    ///
    /// 职责：
    ///   1. <b>检测</b>：每帧记录帧时长与托管内存，维护环形缓冲滑动窗口。
    ///   2. <b>分析</b>：提供实时 FPS、滑动窗口均值 / P95 / P99 等快照，结果懒缓存。
    ///   3. <b>预警</b>：按可配置阈值触发 <see cref="PerformanceAlert"/>，带冷却去抖。
    ///   4. <b>自适应</b>：评估 <see cref="PerformanceLevel"/>，供上层动态降/升画质；
    ///      降级快（短冷却），升级慢（需持续改善 + 长冷却），防振荡。
    ///
    /// 高性能约束：
    ///   • <see cref="Sample"/> 热路径：O(1)，零 GC 分配。
    ///   • 快照计算：懒缓存，有新采样才重算；复用排序缓冲，避免 float[] 重复分配。
    ///   • 预警派发：基于冷却时间去抖，从 Framework.Update 主线程调用。
    ///   • 等级评估：滑动窗口均值 FPS + 迟滞阈值 + 持续时间，多重防抖。
    ///
    /// 典型用法：
    /// <code>
    /// // 业务代码
    /// GameEntry.Performance.SubscribeAlert(PerformanceAlertType.LowFps, OnLowFps);
    ///
    /// void OnLowFps(PerformanceAlert alert)
    /// {
    ///     Debug.LogWarning($"FPS {alert.Value:F1} 低于阈值 {alert.Threshold:F1}");
    /// }
    /// </code>
    /// </summary>
    public interface IPerformanceManager
    {
        // ================================================================
        //  初始化
        // ================================================================

        /// <summary>
        /// 初始化（或重置）采样缓冲。
        /// 必须在第一次 <see cref="Sample"/> 之前调用；之后调用会清空所有历史数据。
        /// 由 <c>PerformanceComponent.Awake</c> 自动调用，业务无需手动调用。
        /// </summary>
        /// <param name="sampleCapacity">环形缓冲容量（帧数）。建议 120–600，默认 240 ≈ 4s @60fps。</param>
        void Initialize(int sampleCapacity);

        // ================================================================
        //  配置
        // ================================================================

        /// <summary>目标帧率（用于自动计算相对 FPS 阈值与等级边界）。默认 60。</summary>
        int TargetFps { get; set; }

        /// <summary>环形缓冲容量（只读；由 <see cref="Initialize"/> 决定）。</summary>
        int SampleCapacity { get; }

        /// <summary>单帧 spike 判定阈值（秒）。默认 1/30 ≈ 33.3ms。</summary>
        float SpikeThresholdSeconds { get; set; }

        /// <summary>FPS 警告阈值；平均 FPS 低于此值触发 <see cref="PerformanceAlertType.LowFps"/>。默认 TargetFps × 0.70。</summary>
        float FpsWarningThreshold { get; set; }

        /// <summary>FPS 严重阈值；平均 FPS 低于此值触发 <see cref="PerformanceAlertType.CriticalFps"/>。默认 TargetFps × 0.50。</summary>
        float FpsCriticalThreshold { get; set; }

        /// <summary>内存警告阈值（字节）。默认 256 MB。</summary>
        long MemoryWarningBytes { get; set; }

        /// <summary>内存严重阈值（字节）。默认 512 MB。</summary>
        long MemoryCriticalBytes { get; set; }

        /// <summary>同类预警的冷却时间（秒），避免每帧触发同一预警。默认 5s。</summary>
        float AlertCooldownSeconds { get; set; }

        // ================================================================
        //  自适应等级配置
        // ================================================================

        /// <summary>是否启用性能等级评估。启用后每帧在 Update 中评估 <see cref="Level"/>。</summary>
        bool AdaptiveEnabled { get; set; }

        /// <summary>
        /// 等级下降（画质降级）的冷却时间（秒）。
        /// 短于上升冷却，保证卡顿时迅速响应。默认 3s。
        /// </summary>
        float LevelDownCooldownSeconds { get; set; }

        /// <summary>
        /// 等级上升（画质升级）的冷却时间（秒）。
        /// 应大于下降冷却，避免频繁升级再被打回。默认 10s。
        /// </summary>
        float LevelUpCooldownSeconds { get; set; }

        /// <summary>
        /// 性能改善需持续多少秒才允许升级（防止短暂帧率峰值触发升级）。默认 3s。
        /// </summary>
        float LevelUpSustainSeconds { get; set; }

        // ================================================================
        //  指标查询
        // ================================================================

        /// <summary>
        /// 最新统计快照（懒缓存）。
        /// 仅当自上次计算后有新采样时重算，多次读取同一帧的代价为一次字段访问。
        /// </summary>
        PerformanceMetrics Snapshot { get; }

        /// <summary>最近一次 Sample 的瞬时 FPS（1 / lastDelta）；无采样时为 0。</summary>
        float InstantFps { get; }

        /// <summary>滑动窗口平均 FPS（等价于 Snapshot.AvgFps）。</summary>
        float AverageFps { get; }

        /// <summary>当前性能等级（仅在 <see cref="AdaptiveEnabled"/> 为 true 时更新）。</summary>
        PerformanceLevel Level { get; }

        /// <summary>已采集的有效样本数（达到 SampleCapacity 后稳定）。</summary>
        int SampleCount { get; }

        /// <summary>累计的 spike 帧数（自上次 <see cref="Reset"/> 起）。</summary>
        int SpikeFrameCount { get; }

        // ================================================================
        //  采样
        // ================================================================

        /// <summary>
        /// 记录一帧性能数据。由 <c>PerformanceComponent.Update</c> 每帧调用。
        /// 热路径：O(1)，零 GC 分配。
        /// </summary>
        /// <param name="deltaSeconds">本帧真实耗时（Time.unscaledDeltaTime）。</param>
        /// <param name="managedMemoryBytes">当前托管堆大小（字节）。建议每 N 帧更新一次，中间帧传入缓存值。</param>
        void Sample(float deltaSeconds, long managedMemoryBytes);

        /// <summary>重置所有样本、spike 计数及预警时间戳。不影响配置参数。</summary>
        void Reset();

        /// <summary>
        /// 把窗口内 deltaTime 拷贝到 buffer（最旧 → 最新顺序）。
        /// </summary>
        /// <returns>写入的元素数（= min(SampleCount, buffer.Length)）。</returns>
        int CopyDeltaSeconds(float[] buffer);

        // ================================================================
        //  预警订阅
        // ================================================================

        /// <summary>
        /// 订阅指定类型的性能预警。
        /// handler 在主线程（<c>Framework.Update</c> 上下文）中调用，可安全访问 Unity API。
        /// </summary>
        void SubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler);

        /// <summary>取消订阅性能预警。handler 不存在时静默忽略。</summary>
        void UnsubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler);
    }
}
