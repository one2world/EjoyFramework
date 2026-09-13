//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using EjoyFramework.Core.Performance;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 性能管理组件（Unity 层）。
    ///
    /// 职责：
    ///   1. 每帧调用 <see cref="IPerformanceManager.Sample"/>（热路径，最轻量）。
    ///   2. 托管内存查询（<c>GC.GetTotalMemory</c>）按 <see cref="m_MemorySampleInterval"/> 节流，
    ///      帧间复用缓存值，避免每帧触发 GC 统计。
    ///   3. 提供 Inspector 一站式配置：帧率目标、缓冲容量、阈值、自适应画质开关。
    ///   4. 可选：自适应等级变化时自动调用 <c>QualitySettings.SetQualityLevel</c>。
    ///
    /// 挂载方式：添加到场景中的 EjoyFramework.Core.prefab（与其他 *Component 同根）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Performance")]
    public sealed class PerformanceComponent : GameFrameworkComponent
    {
        // ----------------------------------------------------------------
        //  Inspector 配置
        // ----------------------------------------------------------------

        [Header("采样")]
        [Tooltip("环形缓冲容量（帧数）。建议 120–600；默认 240 ≈ 4s @ 60fps。")]
        [SerializeField] private int m_SampleCapacity = 240;

        [Tooltip("目标帧率（用于自动计算 FPS 阈值与性能等级边界）。")]
        [SerializeField] private int m_TargetFps = 60;

        [Tooltip("单帧 spike 判定阈值（毫秒）。默认 33.3ms（<30fps 算 spike）。")]
        [SerializeField] private float m_SpikeThresholdMs = 33.3f;

        [Tooltip("GC 内存采样间隔（帧）。GC.GetTotalMemory 较重，每 N 帧查一次；中间帧复用缓存值。")]
        [SerializeField] private int m_MemorySampleInterval = 60;

        [Header("预警阈值")]
        [Tooltip("FPS 警告阈值。0 = 自动（TargetFps × 0.70）。")]
        [SerializeField] private float m_FpsWarningThreshold = 0f;

        [Tooltip("FPS 严重阈值。0 = 自动（TargetFps × 0.50）。")]
        [SerializeField] private float m_FpsCriticalThreshold = 0f;

        [Tooltip("托管内存警告阈值（MB）。")]
        [SerializeField] private int m_MemoryWarningMb = 256;

        [Tooltip("托管内存严重阈值（MB）。")]
        [SerializeField] private int m_MemoryCriticalMb = 512;

        [Tooltip("同类预警冷却时间（秒），避免每帧重复触发。")]
        [SerializeField] private float m_AlertCooldownSeconds = 5f;

        [Header("自适应画质")]
        [Tooltip("启用性能等级评估（Lowest→Highest 五档）。")]
        [SerializeField] private bool m_AdaptiveEnabled = false;

        [Tooltip("启用后等级变化时自动调用 QualitySettings.SetQualityLevel。\n" +
                 "要求项目 Quality Settings 有 5 个档位（对应 PerformanceLevel 0–4）。")]
        [SerializeField] private bool m_ApplyUnityQualitySettings = false;

        [Tooltip("等级下降（画质降级）冷却（秒）。")]
        [SerializeField] private float m_LevelDownCooldown = 3f;

        [Tooltip("等级上升（画质升级）冷却（秒）。")]
        [SerializeField] private float m_LevelUpCooldown = 10f;

        [Tooltip("等级上升需持续改善的时长（秒），防止瞬时峰值触发升级。")]
        [SerializeField] private float m_LevelUpSustain = 3f;

        // ----------------------------------------------------------------
        //  内部状态
        // ----------------------------------------------------------------
        private IPerformanceManager m_PerformanceManager;
        private int m_FrameCount;
        private long m_CachedMemory;

        // ================================================================
        //  MonoBehaviour 生命周期
        // ================================================================

        protected override void Awake()
        {
            base.Awake();

            m_PerformanceManager = Framework.GetModule<IPerformanceManager>();
            if (m_PerformanceManager == null)
            {
                Log.Fatal("Performance manager is invalid.");
                return;
            }

            // 在第一次 Sample 之前完成初始化
            m_PerformanceManager.Initialize(m_SampleCapacity);

            // 应用 Inspector 配置
            m_PerformanceManager.TargetFps              = m_TargetFps;
            m_PerformanceManager.SpikeThresholdSeconds  = m_SpikeThresholdMs * 0.001f;
            m_PerformanceManager.MemoryWarningBytes      = (long)m_MemoryWarningMb * 1024 * 1024;
            m_PerformanceManager.MemoryCriticalBytes     = (long)m_MemoryCriticalMb * 1024 * 1024;
            m_PerformanceManager.AlertCooldownSeconds    = m_AlertCooldownSeconds;
            m_PerformanceManager.AdaptiveEnabled         = m_AdaptiveEnabled;
            m_PerformanceManager.LevelDownCooldownSeconds = m_LevelDownCooldown;
            m_PerformanceManager.LevelUpCooldownSeconds   = m_LevelUpCooldown;
            m_PerformanceManager.LevelUpSustainSeconds    = m_LevelUpSustain;

            // 手动设置阈值（0 表示保持模块自动值）
            if (m_FpsWarningThreshold  > 0f) m_PerformanceManager.FpsWarningThreshold  = m_FpsWarningThreshold;
            if (m_FpsCriticalThreshold > 0f) m_PerformanceManager.FpsCriticalThreshold = m_FpsCriticalThreshold;

            // 自适应画质：订阅等级变化预警
            if (m_AdaptiveEnabled && m_ApplyUnityQualitySettings)
            {
                m_PerformanceManager.SubscribeAlert(PerformanceAlertType.LevelDegraded, OnLevelChanged);
                m_PerformanceManager.SubscribeAlert(PerformanceAlertType.LevelImproved, OnLevelChanged);
            }
        }

        protected override void OnDestroy()
        {
            if (m_PerformanceManager != null)
            {
                m_PerformanceManager.UnsubscribeAlert(PerformanceAlertType.LevelDegraded, OnLevelChanged);
                m_PerformanceManager.UnsubscribeAlert(PerformanceAlertType.LevelImproved, OnLevelChanged);
            }
            base.OnDestroy();
        }

        // PerformanceComponent 默认执行顺序（0），晚于 BaseComponent（-10000）。
        // PerformanceManager.Update 在 BaseComponent.Update → Framework.Update 中已执行（分析上一帧）；
        // 此处 Sample 采集当前帧，供下一帧分析。
        private void Update()
        {
            if (m_PerformanceManager == null) return;

            // GC 内存节流：每 m_MemorySampleInterval 帧才真正查询；中间帧复用缓存值。
            // 钳到 >=1，防止 Inspector 误填 0/负数时取模触发 DivideByZeroException。
            m_FrameCount++;
            int sampleInterval = Mathf.Max(1, m_MemorySampleInterval);
            if (m_FrameCount % sampleInterval == 0 || m_CachedMemory == 0L)
                m_CachedMemory = GC.GetTotalMemory(false);

            m_PerformanceManager.Sample(Time.unscaledDeltaTime, m_CachedMemory);
        }

        // ================================================================
        //  公开 API（对业务透明转发）
        // ================================================================

        /// <summary>最新统计快照（懒缓存）。</summary>
        public PerformanceMetrics Snapshot
        {
            get { return m_PerformanceManager != null ? m_PerformanceManager.Snapshot : default; }
        }

        /// <summary>瞬时 FPS（最近一帧）。</summary>
        public float InstantFps
        {
            get { return m_PerformanceManager != null ? m_PerformanceManager.InstantFps : 0f; }
        }

        /// <summary>滑动窗口平均 FPS。</summary>
        public float AverageFps
        {
            get { return m_PerformanceManager != null ? m_PerformanceManager.AverageFps : 0f; }
        }

        /// <summary>当前性能等级。</summary>
        public PerformanceLevel Level
        {
            get { return m_PerformanceManager != null ? m_PerformanceManager.Level : PerformanceLevel.Highest; }
        }

        /// <summary>订阅性能预警。handler 在主线程 Framework.Update 中调用。</summary>
        public void SubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler)
        {
            m_PerformanceManager?.SubscribeAlert(type, handler);
        }

        /// <summary>取消订阅性能预警。</summary>
        public void UnsubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler)
        {
            m_PerformanceManager?.UnsubscribeAlert(type, handler);
        }

        /// <summary>运行时切换自适应画质开关。</summary>
        public void SetAdaptiveEnabled(bool enabled)
        {
            m_AdaptiveEnabled = enabled;
            if (m_PerformanceManager != null)
                m_PerformanceManager.AdaptiveEnabled = enabled;
        }

        /// <summary>重置所有采样数据（不影响配置参数）。</summary>
        public void ResetSamples()
        {
            m_FrameCount   = 0;
            m_CachedMemory = 0L;
            m_PerformanceManager?.Reset();
        }

        // ================================================================
        //  自适应画质回调
        // ================================================================

        private void OnLevelChanged(PerformanceAlert alert)
        {
            // QualitySettings 索引与 PerformanceLevel 枚举值（0–4）直接对应
            QualitySettings.SetQualityLevel((int)alert.Level, applyExpensiveChanges: true);
        }
    }
}
