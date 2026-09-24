//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Benchmarking;
using EjoyFramework.Core.Diagnostics;
using EjoyFramework.Core.Quality;
using EjoyFramework.Core.Telemetry;
using UnityEngine;
using UnityEngine.Profiling;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 真机性能采集：<see cref="StartCapture"/> 后逐帧记录帧耗时与工作耗时（FrameTimingManager）、每 0.5s 记录内存峰值，
    /// <see cref="StopCapture"/>（或到时长自动停）时写报告到 <c>persistentDataPath/PerfCaptures/&lt;label&gt;-&lt;时间&gt;.tsv</c>
    /// 并写一条 <c>TelemetryKind.PerfCapture</c> 遥测。
    ///
    /// 典型用法：基准场景 / 跑图脚本 / 自动化回归在固定镜头路径上 Start → 走完 → Stop，同机前后版本对比报告；
    /// 线上可在特定玩法段（进城、Boss 战）采集摘要随遥测回收。采集期间默认暂停自动画质（保证可复现）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Perf Capture")]
    public sealed class PerfCaptureComponent : GameFrameworkComponent
    {
        [Tooltip("采集期间暂停自动画质，保证档位 / 分辨率不变、结果可复现。")]
        [SerializeField] private bool m_SuspendAutoQuality = true;

        [Tooltip("采集期间在崩溃采集中标记阶段（Capture:<label>）。")]
        [SerializeField] private bool m_SetCrashPhase = true;

        [Tooltip("报告目录（相对 persistentDataPath）。")]
        [SerializeField] private string m_Folder = "PerfCaptures";

        private readonly PerfCapture m_Capture = new PerfCapture();
        private FrameWorkTimeSampler m_WorkTime;
        private float m_Duration;
        private float m_SinceMemory;
        private bool m_SuspendedQuality;
        private string m_PreviousPhase;
        private string m_LastReportPath;

        protected override void Awake()
        {
            base.Awake();
            m_WorkTime = new FrameWorkTimeSampler();
        }

        public bool IsCapturing { get { return m_Capture.IsRunning; } }

        /// <summary>底层采集器（读实时摘要 / 标记）。</summary>
        public PerfCapture Capture { get { return m_Capture; } }

        /// <summary>报告目录；null = persistentDataPath/<see cref="m_Folder"/>。自动化 / 测试可指向工程内目录。</summary>
        public string ReportDirectory { get; set; }

        /// <summary>最近一次写出的报告路径。</summary>
        public string LastReportPath { get { return m_LastReportPath; } }

        /// <summary>开始采集。<paramref name="durationSeconds"/> &gt; 0 时到时自动停止。</summary>
        public void StartCapture(string label, float durationSeconds = 0f)
        {
            if (m_Capture.IsRunning) throw new FrameworkException("PerfCapture：已在采集中（" + m_Capture.Label + "），先 StopCapture。");
            m_Capture.Begin(label);
            m_Duration = durationSeconds;
            m_SinceMemory = float.MaxValue;   // 首帧即采一次内存

            if (m_SuspendAutoQuality && Framework.HasModule<IQualityManager>())
            {
                Framework.GetModule<IQualityManager>().SuspendAuto();
                m_SuspendedQuality = true;
            }

            if (m_SetCrashPhase && Framework.HasModule<ICrashManager>())
            {
                ICrashManager crash = Framework.GetModule<ICrashManager>();
                m_PreviousPhase = crash.Phase;
                crash.SetPhase("Capture:" + label);
            }
        }

        /// <summary>打标记（非每帧）。</summary>
        public void Mark(string name)
        {
            m_Capture.Mark(name);
        }

        /// <summary>停止采集，写报告与遥测，返回摘要。</summary>
        public PerfCaptureSummary StopCapture()
        {
            if (!m_Capture.IsRunning) throw new FrameworkException("PerfCapture：当前没有在采集。");
            PerfCaptureSummary summary = m_Capture.End();
            RestoreSideEffects();
            m_LastReportPath = WriteReport();
            RecordTelemetry(summary);
            Log.Info("PerfCapture '{0}'：{1} 帧，avg {2:F2}ms p95 {3:F2}ms p99 {4:F2}ms，报告 {5}",
                m_Capture.Label, summary.Frames, summary.AvgFrameMs, summary.P95FrameMs, summary.P99FrameMs, m_LastReportPath ?? "(写入失败)");
            return summary;
        }

        private void Update()
        {
            if (!m_Capture.IsRunning) return;
            float dt = Time.unscaledDeltaTime;
            float work;
            if (!m_WorkTime.TrySample(out work)) work = 0f;
            m_Capture.AddFrame(dt * 1000f, work);

            m_SinceMemory += dt;
            if (m_SinceMemory >= 0.5f)
            {
                m_SinceMemory = 0f;
                m_Capture.AddMemorySample((int)(Profiler.GetMonoUsedSizeLong() >> 20),
                    (int)(Profiler.GetTotalAllocatedMemoryLong() >> 20),
                    (int)(Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20));
            }

            if (m_Duration > 0f && m_Capture.ElapsedSeconds >= m_Duration) StopCapture();
        }

        private void RestoreSideEffects()
        {
            if (m_SuspendedQuality)
            {
                m_SuspendedQuality = false;
                if (Framework.HasModule<IQualityManager>()) Framework.GetModule<IQualityManager>().ResumeAuto();
            }

            if (m_SetCrashPhase && Framework.HasModule<ICrashManager>())
            {
                Framework.GetModule<ICrashManager>().SetPhase(m_PreviousPhase);
                m_PreviousPhase = null;
            }
        }

        private string WriteReport()
        {
            try
            {
                string dir = !string.IsNullOrEmpty(ReportDirectory) ? ReportDirectory : Path.Combine(Application.persistentDataPath, m_Folder);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SafeFileName(m_Capture.Label) + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".tsv");
                File.WriteAllText(path, m_Capture.ToText(BuildEnvironment()), new System.Text.UTF8Encoding(false));
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("PerfCapture：写报告失败：{0}", ex.Message);
                return null;
            }
        }

        /// <summary>采集环境（设备 / 版本 / 画质），也供自动化脚本写 BenchmarkReport 用。</summary>
        public static List<KeyValuePair<string, string>> BuildEnvironment()
        {
            List<KeyValuePair<string, string>> env = new List<KeyValuePair<string, string>>();
            env.Add(new KeyValuePair<string, string>("session", AppSession.Id));
            env.Add(new KeyValuePair<string, string>("device", AppSession.DeviceSummary));
            env.Add(new KeyValuePair<string, string>("unity", Application.unityVersion));
            env.Add(new KeyValuePair<string, string>("app", Application.version));
            env.Add(new KeyValuePair<string, string>("platform", Application.platform.ToString()));
            env.Add(new KeyValuePair<string, string>("utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));
            if (Framework.HasModule<IQualityManager>())
            {
                IQualityManager q = Framework.GetModule<IQualityManager>();
                env.Add(new KeyValuePair<string, string>("quality_level", q.Level.ToString()));
                env.Add(new KeyValuePair<string, string>("device_tier", q.Tier.Tier.ToString()));
                env.Add(new KeyValuePair<string, string>("render_scale", q.RenderScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
            }

            return env;
        }

        private void RecordTelemetry(PerfCaptureSummary s)
        {
            if (!Framework.HasModule<ITelemetryManager>()) return;
            ITelemetryManager telemetry = Framework.GetModule<ITelemetryManager>();
            if (!telemetry.IsSampling) return;
            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.PerfCapture;
            r.I0 = s.Frames;
            r.I1 = s.Hitches50;
            r.I2 = s.Hitches100;
            r.I3 = s.PeakTotalMB;
            r.I4 = (int)CrashFingerprint.ComputeMessage(m_Capture.Label);
            r.F0 = s.AvgFrameMs;
            r.F1 = s.P95FrameMs;
            r.F2 = s.P99FrameMs;
            r.F3 = s.MaxFrameMs;
            r.F4 = s.AvgWorkMs;
            r.F5 = (float)s.DurationSeconds;
            telemetry.Record(ref r);
        }

        private static string SafeFileName(string label)
        {
            char[] chars = label.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) chars[i] = '_';
            }

            return new string(chars);
        }

        protected override void OnDestroy()
        {
            try
            {
                if (m_Capture.IsRunning)
                {
                    m_Capture.End();
                    RestoreSideEffects();
                }
            }
            finally
            {
                base.OnDestroy();
            }
        }
    }
}
