//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Performance;
using EjoyFramework.Core.Telemetry;
using UnityEngine;
using UnityEngine.Profiling;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 遥测组件：把 <see cref="IPerformanceManager"/> 的帧时间窗口、Profiler 内存、热/电状态按周期写成 <see cref="TelemetryRecord"/>，
    /// 并提供加载耗时记录入口。会话在 Awake 自动开始（可关），设备信息自动拼装。
    ///
    /// 数据：
    ///   • FrameWindow（每 <see cref="m_FrameWindowSeconds"/> 秒）：avg/p95/p99/max 毫秒、帧数、spike 数、当前性能分级；
    ///   • Memory（每 <see cref="m_MemoryIntervalSeconds"/> 秒）：托管 / 原生总量 / 图形驱动 / 常驻 bundle / 系统保留，MB；
    ///   • Thermal（每 <see cref="m_ThermalIntervalSeconds"/> 秒）：电量、充电状态（热状态平台 API 未暴露时为 -1）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Telemetry")]
    public sealed class TelemetryComponent : GameFrameworkComponent
    {
        [SerializeField]
        [Tooltip("Awake 时自动开始会话。")]
        private bool m_AutoStartSession = true;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("会话采样率。")]
        private float m_SampleRate = 1f;

        [SerializeField]
        [Tooltip("帧时间窗口记录间隔（秒）。")]
        private float m_FrameWindowSeconds = 10f;

        [SerializeField]
        [Tooltip("内存快照间隔（秒）。")]
        private float m_MemoryIntervalSeconds = 30f;

        [SerializeField]
        [Tooltip("热/电状态间隔（秒）；0 = 不采。")]
        private float m_ThermalIntervalSeconds = 60f;

        [SerializeField]
        [Tooltip("未由业务 SetBackend 时，默认落盘到 persistentDataPath/Telemetry（FileTelemetryBackend）。")]
        private bool m_DefaultFileBackend = true;

        [SerializeField]
        [Tooltip("默认文件后端最多保留的批次文件数。")]
        private int m_MaxTelemetryFiles = 200;

        private ITelemetryManager m_Telemetry;
        private IPerformanceManager m_Performance;
        private float m_SinceFrameWindow;
        private float m_SinceMemory;
        private float m_SinceThermal;
        private int m_WindowFrames;
        private int m_WindowSpikesAtStart;

        protected override void Awake()
        {
            base.Awake();
            m_Telemetry = Framework.GetModule<ITelemetryManager>();
            if (m_Telemetry == null)
            {
                Log.Fatal("Telemetry manager is invalid.");
                return;
            }

            m_Telemetry.SampleRate = m_SampleRate;
            if (m_DefaultFileBackend)
            {
                FileTelemetryBackend file = new FileTelemetryBackend(System.IO.Path.Combine(Application.persistentDataPath, "Telemetry"));
                file.MaxFiles = m_MaxTelemetryFiles;
                m_Telemetry.SetBackend(file);
            }

            m_Performance = Framework.HasModule<IPerformanceManager>() ? Framework.GetModule<IPerformanceManager>() : null;
            if (m_AutoStartSession) StartSession();
        }

        /// <summary>底层管理器。</summary>
        public ITelemetryManager Telemetry { get { return m_Telemetry; } }

        /// <summary>开始会话（自动生成 sessionId 与设备信息）。</summary>
        public bool StartSession()
        {
            if (m_Telemetry == null) return false;
            string sessionId = Guid.NewGuid().ToString("N");
            string device = SystemInfo.deviceModel + "|" + SystemInfo.operatingSystem + "|" + SystemInfo.graphicsDeviceName + "|"
                            + SystemInfo.systemMemorySize + "MB|" + SystemInfo.processorType + "x" + SystemInfo.processorCount
                            + "|" + Screen.width + "x" + Screen.height;
            bool sampled = m_Telemetry.StartSession(sessionId, device, Application.version);
            m_SinceFrameWindow = 0f;
            m_SinceMemory = 0f;
            m_SinceThermal = 0f;
            m_WindowFrames = 0;
            m_WindowSpikesAtStart = m_Performance != null ? m_Performance.SpikeFrameCount : 0;
            return sampled;
        }

        /// <summary>记录一次加载耗时（资源 / 场景 / 区块），kind 与 item 用稳定哈希便于聚合。</summary>
        public void RecordLoadTiming(int loadKindHash, int itemHash, float seconds, bool success)
        {
            if (m_Telemetry == null || !m_Telemetry.IsSampling) return;
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.LoadTiming;
            r.I0 = loadKindHash;
            r.I1 = itemHash;
            r.F0 = seconds;
            r.I2 = success ? 1 : 0;
            m_Telemetry.Record(ref r);
        }

        /// <summary>业务自定义记录（Kind ≥ TelemetryKind.Custom）。</summary>
        public void RecordCustom(ref TelemetryRecord record)
        {
            if (record.Kind < TelemetryKind.Custom) throw new FrameworkException("TelemetryComponent.RecordCustom：自定义 Kind 必须 ≥ TelemetryKind.Custom。");
            if (m_Telemetry == null) return;
            m_Telemetry.Record(ref record);
        }

        private void Update()
        {
            if (m_Telemetry == null || !m_Telemetry.IsSampling) return;
            float dt = Time.unscaledDeltaTime;
            m_WindowFrames++;
            m_SinceFrameWindow += dt;
            m_SinceMemory += dt;
            m_SinceThermal += dt;

            if (m_FrameWindowSeconds > 0f && m_SinceFrameWindow >= m_FrameWindowSeconds)
            {
                RecordFrameWindow();
                m_SinceFrameWindow = 0f;
                m_WindowFrames = 0;
            }

            if (m_MemoryIntervalSeconds > 0f && m_SinceMemory >= m_MemoryIntervalSeconds)
            {
                RecordMemory();
                m_SinceMemory = 0f;
            }

            if (m_ThermalIntervalSeconds > 0f && m_SinceThermal >= m_ThermalIntervalSeconds)
            {
                RecordThermal();
                m_SinceThermal = 0f;
            }
        }

        private void RecordFrameWindow()
        {
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.FrameWindow;
            if (m_Performance != null)
            {
                PerformanceMetrics m = m_Performance.Snapshot;
                r.F0 = m.AvgDeltaTime * 1000f;
                r.F1 = m.P95DeltaTime * 1000f;
                r.F2 = m.P99DeltaTime * 1000f;
                r.F3 = m.MaxDeltaTime * 1000f;
                r.I1 = m_Performance.SpikeFrameCount - m_WindowSpikesAtStart;
                r.I2 = (int)m_Performance.Level;
                m_WindowSpikesAtStart = m_Performance.SpikeFrameCount;
            }
            else
            {
                r.F0 = m_WindowFrames > 0 ? m_FrameWindowSeconds / m_WindowFrames * 1000f : 0f;
            }

            r.I0 = m_WindowFrames;
            m_Telemetry.Record(ref r);
        }

        private void RecordMemory()
        {
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.Memory;
            r.I0 = (int)(Profiler.GetMonoUsedSizeLong() >> 20);
            r.I1 = (int)(Profiler.GetTotalAllocatedMemoryLong() >> 20);
            r.I2 = (int)(Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20);
            ResourceComponent rc = ComponentRegistry.GetComponent<ResourceComponent>();
            r.I3 = rc != null ? (int)(rc.ResidentBytes >> 20) : -1;
            r.I4 = (int)(Profiler.GetTotalReservedMemoryLong() >> 20);
            m_Telemetry.Record(ref r);
        }

        private void RecordThermal()
        {
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.Thermal;
            r.I0 = -1;   // 热状态：Unity 未提供跨平台 API，留给平台插件写入
            r.F0 = SystemInfo.batteryLevel;
            r.I1 = (int)SystemInfo.batteryStatus;
            m_Telemetry.Record(ref r);
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_Telemetry != null) m_Telemetry.EndSession();
            }
            finally
            {
                base.OnDestroy();
            }
        }
    }
}
