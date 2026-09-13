//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Performance
{
    /// <summary>
    /// 性能管理器。
    ///
    /// 高性能设计要点：
    ///   • Sample() 热路径：纯算术 + 环形索引，O(1)，零分配。
    ///   • 快照计算：按需懒执行（dirty flag），m_SortBuf 跨调用复用，避免 float[] 重复分配。
    ///   • 预警派发：per-type List 索引直访，无 LINQ，处理器分支可预测，handler 调用由 try/catch 隔离。
    ///   • 等级评估：三层防振荡—(1) 降级/升级各自独立冷却，(2) 升级需连续改善 LevelUpSustainSeconds，
    ///     (3) 降/升阈值之间留有迟滞区间（down < 85%/70%/50%/30%；up > 90%/80%/65%/45%）。
    ///   • Priority = 60：在多数业务模块（priority 0）之前运行，在 Event（90）之后，
    ///     保证预警通过本帧 FireNow 路径能立即派发。
    /// </summary>
    internal sealed class PerformanceManager : FrameworkModule, IPerformanceManager
    {
        private const int AlertTypeCount = 7;
        private const int MinSamplesBeforeAlert = 10;

        // 等级降级阈值（%TargetFps）：[level] = 低于此分数则从该等级降一级
        // index 0 (Lowest) 无法再降，用 0f 占位。
        private static readonly float[] s_DownPct = { 0f, 0.30f, 0.50f, 0.70f, 0.85f };

        // 等级升级阈值（%TargetFps）：[level] = 高于此分数则从该等级升一级
        // index 4 (Highest) 无法再升，用 2f（必然>1）占位。
        private static readonly float[] s_UpPct = { 0.45f, 0.65f, 0.80f, 0.90f, 2f };

        // ----------------------------------------------------------------
        //  环形缓冲（核心采样存储）
        // ----------------------------------------------------------------
        private int m_Capacity;
        private float[] m_Deltas;   // unscaled deltaTime per frame
        private long[] m_Memory;    // managed memory bytes per frame
        private int m_Head;         // 下一个写入位置
        private int m_FilledCount;  // 已写入样本数（最大 = m_Capacity）
        private int m_SpikeCount;
        private bool m_SpikeThisFrame;  // 上一次 Sample 是否为 spike（延迟到 Update 触发预警）
        private float m_LastDelta;
        private long m_LastMemory;

        // 排序缓冲：跨快照调用复用，避免每次 ComputeMetrics 都 new float[]
        private float[] m_SortBuf;

        // 快照缓存
        private PerformanceMetrics m_CachedMetrics;
        private bool m_MetricsDirty = true;

        // ----------------------------------------------------------------
        //  配置
        // ----------------------------------------------------------------
        private int m_TargetFps = 60;
        private float m_SpikeThreshold = 1f / 30f;   // 33.3ms
        private float m_FpsWarnThreshold;
        private float m_FpsCritThreshold;
        private long m_MemWarnBytes = 256L * 1024 * 1024;
        private long m_MemCritBytes = 512L * 1024 * 1024;
        private float m_AlertCooldown = 5f;

        // ----------------------------------------------------------------
        //  自适应等级
        // ----------------------------------------------------------------
        private bool m_AdaptiveEnabled;
        private PerformanceLevel m_Level = PerformanceLevel.Highest;
        private float m_LevelDownCooldown = 3f;
        private float m_LevelUpCooldown = 10f;
        private float m_LevelUpSustain = 3f;
        private float m_LastLevelChangeTime;
        private bool m_LevelUpPending;
        private float m_LevelUpPendingSince;

        // ----------------------------------------------------------------
        //  预警
        // ----------------------------------------------------------------
        private List<Action<PerformanceAlert>>[] m_Handlers;
        private float[] m_LastAlertTime;

        // ----------------------------------------------------------------
        //  时间（由 Update realElapseSeconds 累积，与 Unity 主线程同步）
        // ----------------------------------------------------------------
        private float m_RealTime;

        // ================================================================
        //  构造
        // ================================================================

        public PerformanceManager()
        {
            // 预分配 alert 结构（不含环形缓冲，等待 Initialize 调用）
            m_Handlers = new List<Action<PerformanceAlert>>[AlertTypeCount];
            m_LastAlertTime = new float[AlertTypeCount];
            for (int i = 0; i < AlertTypeCount; i++)
            {
                m_Handlers[i] = new List<Action<PerformanceAlert>>(4);
                m_LastAlertTime[i] = float.MinValue / 2f;
            }
            m_LastLevelChangeTime = float.MinValue / 2f;
            RecalcAutoThresholds();
        }

        public override int Priority { get { return 60; } }

        // ================================================================
        //  IPerformanceManager — 初始化
        // ================================================================

        public void Initialize(int sampleCapacity)
        {
            if (sampleCapacity < 8)
            {
                throw new FrameworkException("Performance module sample capacity must be >= 8.");
            }

            m_Capacity = sampleCapacity;
            m_Deltas = new float[sampleCapacity];
            m_Memory = new long[sampleCapacity];
            m_SortBuf = new float[sampleCapacity];
            Reset();
        }

        // ================================================================
        //  IPerformanceManager — 配置
        // ================================================================

        public int TargetFps
        {
            get { return m_TargetFps; }
            set
            {
                if (value <= 0) throw new FrameworkException("TargetFps must be > 0.");
                m_TargetFps = value;
                RecalcAutoThresholds();
            }
        }

        public int SampleCapacity { get { return m_Capacity; } }

        public float SpikeThresholdSeconds
        {
            get { return m_SpikeThreshold; }
            set { if (value > 0f) m_SpikeThreshold = value; }
        }

        public float FpsWarningThreshold
        {
            get { return m_FpsWarnThreshold; }
            set { if (value > 0f) m_FpsWarnThreshold = value; }
        }

        public float FpsCriticalThreshold
        {
            get { return m_FpsCritThreshold; }
            set { if (value > 0f) m_FpsCritThreshold = value; }
        }

        public long MemoryWarningBytes
        {
            get { return m_MemWarnBytes; }
            set { if (value >= 0) m_MemWarnBytes = value; }
        }

        public long MemoryCriticalBytes
        {
            get { return m_MemCritBytes; }
            set { if (value >= 0) m_MemCritBytes = value; }
        }

        public float AlertCooldownSeconds
        {
            get { return m_AlertCooldown; }
            set { if (value >= 0f) m_AlertCooldown = value; }
        }

        public bool AdaptiveEnabled
        {
            get { return m_AdaptiveEnabled; }
            set { m_AdaptiveEnabled = value; }
        }

        public float LevelDownCooldownSeconds
        {
            get { return m_LevelDownCooldown; }
            set { if (value >= 0f) m_LevelDownCooldown = value; }
        }

        public float LevelUpCooldownSeconds
        {
            get { return m_LevelUpCooldown; }
            set { if (value >= 0f) m_LevelUpCooldown = value; }
        }

        public float LevelUpSustainSeconds
        {
            get { return m_LevelUpSustain; }
            set { if (value >= 0f) m_LevelUpSustain = value; }
        }

        // ================================================================
        //  IPerformanceManager — 指标
        // ================================================================

        public PerformanceMetrics Snapshot
        {
            get
            {
                if (m_MetricsDirty)
                {
                    m_CachedMetrics = ComputeMetrics();
                    m_MetricsDirty = false;
                }
                return m_CachedMetrics;
            }
        }

        public float InstantFps { get { return m_LastDelta > 0f ? 1f / m_LastDelta : 0f; } }

        public float AverageFps { get { return Snapshot.AvgFps; } }

        public PerformanceLevel Level { get { return m_Level; } }

        public int SampleCount { get { return m_FilledCount; } }

        public int SpikeFrameCount { get { return m_SpikeCount; } }

        // ================================================================
        //  IPerformanceManager — 采样（热路径：O(1)，零分配）
        // ================================================================

        public void Sample(float deltaSeconds, long managedMemoryBytes)
        {
            if (m_Deltas == null)
            {
                // Initialize 尚未调用；使用默认容量自动初始化
                Initialize(240);
            }

            if (deltaSeconds < 0f) deltaSeconds = 0f;
            if (managedMemoryBytes < 0L) managedMemoryBytes = 0L;

            m_Deltas[m_Head] = deltaSeconds;
            m_Memory[m_Head] = managedMemoryBytes;
            m_Head = (m_Head + 1) % m_Capacity;
            if (m_FilledCount < m_Capacity) m_FilledCount++;

            m_SpikeThisFrame = deltaSeconds > m_SpikeThreshold;
            if (m_SpikeThisFrame) m_SpikeCount++;

            m_LastDelta = deltaSeconds;
            m_LastMemory = managedMemoryBytes;
            m_MetricsDirty = true;
        }

        public void Reset()
        {
            m_Head = 0;
            m_FilledCount = 0;
            m_SpikeCount = 0;
            m_SpikeThisFrame = false;
            m_LastDelta = 0f;
            m_LastMemory = 0L;
            m_MetricsDirty = true;
            m_LevelUpPending = false;
            if (m_LastAlertTime != null)
            {
                for (int i = 0; i < AlertTypeCount; i++)
                    m_LastAlertTime[i] = float.MinValue / 2f;
            }
            m_LastLevelChangeTime = float.MinValue / 2f;
        }

        public int CopyDeltaSeconds(float[] buffer)
        {
            if (buffer == null) throw new FrameworkException("buffer is null.");
            if (m_Deltas == null) return 0;
            int n = m_FilledCount;
            int start = m_FilledCount == m_Capacity ? m_Head : 0;
            int count = Math.Min(n, buffer.Length);
            for (int i = 0; i < count; i++)
                buffer[i] = m_Deltas[(start + i) % m_Capacity];
            return count;
        }

        // ================================================================
        //  IPerformanceManager — 预警订阅
        // ================================================================

        public void SubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler)
        {
            if (handler == null) throw new FrameworkException("Alert handler is null.");
            var list = m_Handlers[(int)type];
            if (!list.Contains(handler)) list.Add(handler);
        }

        public void UnsubscribeAlert(PerformanceAlertType type, Action<PerformanceAlert> handler)
        {
            if (handler == null) return;
            m_Handlers[(int)type].Remove(handler);
        }

        // ================================================================
        //  FrameworkModule 生命周期
        // ================================================================

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_RealTime += realElapseSeconds;

            if (m_FilledCount < MinSamplesBeforeAlert) return;

            // 获取缓存快照（同帧内只算一次）
            PerformanceMetrics metrics = Snapshot;
            float avgFps = metrics.AvgFps;

            // ---- Spike 预警（来自上一帧 Sample 的标记）----
            if (m_SpikeThisFrame)
            {
                m_SpikeThisFrame = false;
                FireAlert(PerformanceAlertType.FrameSpike,
                          m_LastDelta * 1000f,
                          m_SpikeThreshold * 1000f);
            }

            // ---- FPS 预警 ----
            if (avgFps < m_FpsCritThreshold)
                FireAlert(PerformanceAlertType.CriticalFps, avgFps, m_FpsCritThreshold);
            else if (avgFps < m_FpsWarnThreshold)
                FireAlert(PerformanceAlertType.LowFps, avgFps, m_FpsWarnThreshold);

            // ---- 内存预警 ----
            if (m_LastMemory > m_MemCritBytes)
                FireAlert(PerformanceAlertType.CriticalMemory,
                          m_LastMemory / (1024f * 1024f),
                          m_MemCritBytes / (1024f * 1024f));
            else if (m_LastMemory > m_MemWarnBytes)
                FireAlert(PerformanceAlertType.HighMemory,
                          m_LastMemory / (1024f * 1024f),
                          m_MemWarnBytes / (1024f * 1024f));

            // ---- 自适应等级评估 ----
            if (m_AdaptiveEnabled)
                EvaluateLevel(avgFps);
        }

        public override void Shutdown()
        {
            for (int i = 0; i < AlertTypeCount; i++)
                m_Handlers[i].Clear();
            Reset();
        }

        // ================================================================
        //  快照计算（懒执行，O(N log N) but N ≤ 600，且每帧至多一次）
        // ================================================================

        private PerformanceMetrics ComputeMetrics()
        {
            var result = new PerformanceMetrics
            {
                Capacity            = m_Capacity,
                SampleCount         = m_FilledCount,
                SpikeFrameCount     = m_SpikeCount,
                SpikeThresholdSeconds = m_SpikeThreshold,
            };
            if (m_FilledCount == 0) return result;

            // 确保排序缓冲足够大（Initialize 后已预分配，此处兜底 lazy alloc）
            if (m_SortBuf == null || m_SortBuf.Length < m_FilledCount)
                m_SortBuf = new float[m_Capacity];

            float minDt = float.MaxValue, maxDt = 0f, sumDt = 0f;
            long  minMem = long.MaxValue, maxMem = 0L, sumMem = 0L;
            int start = m_FilledCount == m_Capacity ? m_Head : 0;

            for (int i = 0; i < m_FilledCount; i++)
            {
                int idx  = (start + i) % m_Capacity;
                float dt = m_Deltas[idx];
                long mem = m_Memory[idx];
                m_SortBuf[i] = dt;
                if (dt  < minDt)  minDt  = dt;
                if (dt  > maxDt)  maxDt  = dt;
                sumDt += dt;
                if (mem < minMem) minMem = mem;
                if (mem > maxMem) maxMem = mem;
                sumMem += mem;
            }

            // 仅对有效段排序（m_SortBuf 可能比 m_FilledCount 更大）
            Array.Sort(m_SortBuf, 0, m_FilledCount);

            result.MinDeltaTime      = minDt;
            result.AvgDeltaTime      = sumDt / m_FilledCount;
            result.MaxDeltaTime      = maxDt;
            result.P95DeltaTime      = Percentile(m_SortBuf, m_FilledCount, 0.95f);
            result.P99DeltaTime      = Percentile(m_SortBuf, m_FilledCount, 0.99f);
            result.MinManagedMemory  = minMem;
            result.AvgManagedMemory  = sumMem / m_FilledCount;
            result.MaxManagedMemory  = maxMem;
            return result;
        }

        // NIST R-3 ceil-based rank 百分位（与 PerformanceCollector 保持算法一致）
        private static float Percentile(float[] sortedAsc, int n, float pct)
        {
            if (n <= 0) return 0f;
            int rank = (int)Math.Ceiling(pct * (n - 1));
            if (rank < 0) rank = 0;
            if (rank >= n) rank = n - 1;
            return sortedAsc[rank];
        }

        // ================================================================
        //  等级评估（三重防振荡：迟滞阈值 + 降/升独立冷却 + 升级持续时间要求）
        // ================================================================

        private void EvaluateLevel(float avgFps)
        {
            int li = (int)m_Level;
            float targetFps = m_TargetFps;

            bool canChangeDown = m_RealTime - m_LastLevelChangeTime >= m_LevelDownCooldown;
            bool canChangeUp   = m_RealTime - m_LastLevelChangeTime >= m_LevelUpCooldown;

            // ---- 降级优先（快速响应卡顿）----
            if (li > 0 && avgFps < s_DownPct[li] * targetFps)
            {
                if (canChangeDown)
                {
                    m_LevelUpPending = false;
                    ChangeLevel((PerformanceLevel)(li - 1),
                                PerformanceAlertType.LevelDegraded, avgFps);
                }
                return;
            }

            // ---- 升级：需满足「持续改善」要求 ----
            if (li < (int)PerformanceLevel.Highest && avgFps > s_UpPct[li] * targetFps)
            {
                if (!m_LevelUpPending)
                {
                    m_LevelUpPending      = true;
                    m_LevelUpPendingSince = m_RealTime;
                }
                else if (canChangeUp
                         && m_RealTime - m_LevelUpPendingSince >= m_LevelUpSustain)
                {
                    m_LevelUpPending = false;
                    ChangeLevel((PerformanceLevel)(li + 1),
                                PerformanceAlertType.LevelImproved, avgFps);
                }
            }
            else
            {
                // 不在升级区间，重置累计计时
                m_LevelUpPending = false;
            }
        }

        private void ChangeLevel(PerformanceLevel newLevel, PerformanceAlertType alertType, float avgFps)
        {
            m_Level = newLevel;
            m_LastLevelChangeTime = m_RealTime;

            // 等级变化不受通用 AlertCooldown 约束（已由冷却字段控制）
            var alert = new PerformanceAlert
            {
                Type      = alertType,
                Value     = avgFps,
                Threshold = 0f,
                Level     = newLevel,
                RealTime  = m_RealTime,
            };
            Dispatch(alertType, alert);
        }

        // ================================================================
        //  预警辅助
        // ================================================================

        private void FireAlert(PerformanceAlertType type, float value, float threshold)
        {
            int ti = (int)type;
            if (m_RealTime - m_LastAlertTime[ti] < m_AlertCooldown) return;
            m_LastAlertTime[ti] = m_RealTime;

            var alert = new PerformanceAlert
            {
                Type      = type,
                Value     = value,
                Threshold = threshold,
                Level     = m_Level,
                RealTime  = m_RealTime,
            };
            Dispatch(type, alert);
        }

        private void Dispatch(PerformanceAlertType type, PerformanceAlert alert)
        {
            var list = m_Handlers[(int)type];
            if (list.Count == 0) return;
            // 先快照再回调：handler 常在回调内反订阅（自身或同类一次性告警）。若直接遍历活动列表，
            // 移除会令后续 handler 被跳过，且缓存的 count 失效后 list[i] 越界。预警受冷却节流、频次低，
            // 这里的快照分配可接受。
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                try
                {
                    snapshot[i](alert);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("PerformanceManager '{0}' handler threw: {1}", type, ex);
                }
            }
        }

        private void RecalcAutoThresholds()
        {
            m_FpsWarnThreshold = m_TargetFps * 0.70f;
            m_FpsCritThreshold = m_TargetFps * 0.50f;
        }
    }
}
