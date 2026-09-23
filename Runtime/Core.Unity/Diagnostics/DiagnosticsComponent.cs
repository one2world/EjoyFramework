//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 诊断组件（Unity 层）。
    ///
    /// 职责：
    ///   1. 解析核心 <see cref="IDiagnosticsManager"/>。
    ///   2. 可选挂载内置 <see cref="FileLogSink"/>（Inspector 开关 <see cref="m_EnableFileSink"/>）。
    ///   3. 订阅 Unity 的 <c>Application.logMessageReceivedThreaded</c>，把引擎 log/exception 流
    ///      映射为 <see cref="DiagLevel"/> 后喂给管理器。
    ///   4. 订阅 <c>AppDomain.CurrentDomain.UnhandledException</c>，把未捕获异常转发崩溃上报。
    ///
    /// 线程：<c>logMessageReceivedThreaded</c> 会在<b>任意线程</b>触发，故下游
    /// DiagnosticsManager 路由 + FileLogSink 写入均已自带 lock 串行化。
    ///
    /// 命名空间防遮蔽：本文件位于 EjoyFramework.Core.* 命名空间体系下，对 UnityEngine 中
    /// 简单名可能被 EjoyFramework.Core 子命名空间遮蔽的类型（Application、Debug、LogType 等）
    /// 一律全限定，避免解析到 EjoyFramework.Core.* 同名段。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Diagnostics")]
    public sealed class DiagnosticsComponent : GameFrameworkComponent
    {
        [Header("文件日志 Sink")]
        [Tooltip("启用内置文件日志 Sink（写到 persistentDataPath/logs/app.log，按大小滚动）。")]
        [SerializeField] private bool m_EnableFileSink = true;

        [Tooltip("单个日志文件大小上限（MB）。0 = 使用默认（1MB）。")]
        [SerializeField] private int m_FileMaxSizeMb = 0;

        [Tooltip("滚动历史份数（app.1.log ...）。0 = 使用默认（2）。")]
        [SerializeField] private int m_FileMaxRollFiles = 0;

        [Header("捕获")]
        [Tooltip("订阅 Unity 日志/异常流（logMessageReceivedThreaded）。")]
        [SerializeField] private bool m_CaptureUnityLog = true;

        [Tooltip("订阅 AppDomain 未捕获异常，转发崩溃上报。")]
        [SerializeField] private bool m_CaptureUnhandled = true;

        [Header("崩溃 / ANR / 异常退出（ICrashManager）")]
        [Tooltip("启用框架崩溃采集：异常去重报告、ANR 看门狗、上次会话异常退出检测，报告落盘 persistentDataPath/Crash。")]
        [SerializeField] private bool m_EnableCrashCapture = true;

        [Tooltip("主线程停顿多少秒判为卡死（ANR）；0 = 关闭。")]
        [SerializeField] private float m_HangThresholdSeconds = 5f;

        [Tooltip("编辑器里也做 ANR 检测（断点 / 暂停会被误判，默认关）。")]
        [SerializeField] private bool m_HangDetectionInEditor = false;

        [Tooltip("盘上最多保留的待上传崩溃报告数。")]
        [SerializeField] private int m_MaxStoredCrashReports = 20;

        private IDiagnosticsManager m_Manager;
        private FileLogSink m_FileSink;
        private ICrashManager m_Crash;
        private bool m_SubscribedLowMemory;
        private bool m_SubscribedUnityLog;
        private bool m_SubscribedUnhandled;

        // ================================================================
        //  MonoBehaviour 生命周期
        // ================================================================

        protected override void Awake()
        {
            base.Awake();

            m_Manager = Framework.GetModule<IDiagnosticsManager>();
            if (m_Manager == null)
            {
                Log.Fatal("Diagnostics manager is invalid.");
                return;
            }

            if (m_EnableFileSink)
            {
                long maxBytes = m_FileMaxSizeMb > 0 ? (long)m_FileMaxSizeMb * 1024 * 1024 : 0;
                m_FileSink = new FileLogSink(rootOverride: null, maxBytes: maxBytes, maxRollFiles: m_FileMaxRollFiles);
                m_Manager.AddSink(m_FileSink);
            }

            if (m_EnableCrashCapture) SetupCrashCapture();

            if (m_CaptureUnityLog)
            {
                UnityEngine.Application.logMessageReceivedThreaded += OnUnityLog;
                m_SubscribedUnityLog = true;
            }

            if (m_CaptureUnhandled)
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                m_SubscribedUnhandled = true;
            }
        }

        private void SetupCrashCapture()
        {
            m_Crash = Framework.GetModule<ICrashManager>();
            bool hangAllowed = !UnityEngine.Application.isEditor || m_HangDetectionInEditor;
            m_Crash.HangThresholdSeconds = hangAllowed ? m_HangThresholdSeconds : 0f;
            m_Crash.MaxStoredReports = m_MaxStoredCrashReports;
            m_Crash.Configure(System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Crash"));
            m_Manager.AddSink(m_Crash);
            m_Crash.BeginSession(AppSession.Id, AppSession.DeviceSummary, UnityEngine.Application.version);
            UnityEngine.Application.lowMemory += OnLowMemory;
            m_SubscribedLowMemory = true;
        }

        private void Start()
        {
            // 所有组件 Awake 之后再接遥测：TelemetryComponent 可能晚于本组件 Awake
            if (m_Crash != null && Framework.HasModule<EjoyFramework.Core.Telemetry.ITelemetryManager>())
            {
                m_Crash.SetTelemetry(Framework.GetModule<EjoyFramework.Core.Telemetry.ITelemetryManager>());
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (m_Crash != null) m_Crash.NotifyBackground(paused);
        }

        private void OnApplicationQuit()
        {
            // 正常退出：结束崩溃会话（删除会话标记），下次启动不会误判为异常退出
            if (m_Crash != null && m_Crash.InSession) m_Crash.EndSession();
        }

        private void OnLowMemory()
        {
            if (m_Crash != null) m_Crash.NotifyLowMemory();
        }

        protected override void OnDestroy()
        {
            // base.OnDestroy()（注销自身）置于 finally：FlushAll/Close 是关闭期最易抛 IO 异常之处，
            // 即便抛出也必须确定性注销，否则 ComponentRegistry.s_ByType 残留已销毁实例。异常仍向上传播。
            try
            {
                if (m_SubscribedUnityLog)
                {
                    UnityEngine.Application.logMessageReceivedThreaded -= OnUnityLog;
                    m_SubscribedUnityLog = false;
                }
                if (m_SubscribedUnhandled)
                {
                    AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                    m_SubscribedUnhandled = false;
                }
                if (m_SubscribedLowMemory)
                {
                    UnityEngine.Application.lowMemory -= OnLowMemory;
                    m_SubscribedLowMemory = false;
                }

                if (m_Crash != null)
                {
                    if (m_Crash.InSession) m_Crash.EndSession();
                    if (m_Manager != null) m_Manager.RemoveSink(m_Crash);
                    m_Crash = null;
                }

                if (m_Manager != null)
                {
                    m_Manager.FlushAll();
                    if (m_FileSink != null) m_Manager.RemoveSink(m_FileSink);
                }

                if (m_FileSink != null)
                {
                    m_FileSink.Close();
                    m_FileSink = null;
                }
            }
            finally
            {
                base.OnDestroy();
            }
        }

        // ================================================================
        //  捕获回调（可能在任意线程）
        // ================================================================

        // Unity 引擎日志流：可能在后台线程触发。下游已线程安全。
        private void OnUnityLog(string condition, string stackTrace, UnityEngine.LogType type)
        {
            IDiagnosticsManager manager = m_Manager;
            if (manager == null) return;
            manager.Log(MapLevel(type), condition, stackTrace);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            IDiagnosticsManager manager = m_Manager;
            if (manager == null) return;
            Exception ex = args.ExceptionObject as Exception;
            if (ex != null)
            {
                // 先经诊断管道（崩溃采集作为 Sink 以非致命收到），再以 fatal 升级同一份报告并同步落盘
                manager.ReportException(ex, args.IsTerminating ? "UnhandledException(terminating)" : "UnhandledException");
                ICrashManager crash = m_Crash;
                if (crash != null && args.IsTerminating) crash.ReportException(ex, true);
            }
            else
            {
                manager.Log(DiagLevel.Exception, "UnhandledException: " + (args.ExceptionObject?.ToString() ?? "null"), null);
            }
        }

        private static DiagLevel MapLevel(UnityEngine.LogType type)
        {
            switch (type)
            {
                case UnityEngine.LogType.Log:       return DiagLevel.Info;
                case UnityEngine.LogType.Warning:   return DiagLevel.Warning;
                case UnityEngine.LogType.Assert:    return DiagLevel.Error;
                case UnityEngine.LogType.Error:     return DiagLevel.Error;
                case UnityEngine.LogType.Exception: return DiagLevel.Exception;
                default:                            return DiagLevel.Info;
            }
        }

        // ================================================================
        //  公开 API（对业务透明转发）
        // ================================================================

        /// <summary>仅分发不低于此等级的日志。</summary>
        public DiagLevel MinLevel
        {
            get { return m_Manager != null ? m_Manager.MinLevel : DiagLevel.Debug; }
            set { if (m_Manager != null) m_Manager.MinLevel = value; }
        }

        /// <summary>添加一个日志 Sink（如业务注入的远端上报 Sink）。</summary>
        public void AddSink(ILogSink sink)
        {
            m_Manager?.AddSink(sink);
        }

        /// <summary>移除一个日志 Sink。</summary>
        public bool RemoveSink(ILogSink sink)
        {
            return m_Manager != null && m_Manager.RemoveSink(sink);
        }

        /// <summary>设置崩溃上报器（桥接 Crashlytics/Bugly 等真实 SDK）。</summary>
        public void SetCrashReporter(ICrashReporter reporter)
        {
            m_Manager?.SetCrashReporter(reporter);
        }

        /// <summary>设置随崩溃报告上报的自定义键值。</summary>
        public void SetUserKey(string key, string value)
        {
            m_Manager?.SetUserKey(key, value);
        }

        /// <summary>崩溃采集管理器（未启用时为 null）：SetPhase / SetUserKey / AddBreadcrumb / SetUploader / AddListener。</summary>
        public ICrashManager Crash
        {
            get { return m_Crash; }
        }

        /// <summary>让所有 Sink 立即落盘 / 推送。</summary>
        public void FlushAll()
        {
            m_Manager?.FlushAll();
        }
    }
}
