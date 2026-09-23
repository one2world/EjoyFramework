//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// <see cref="ICrashManager"/> 实现。
    ///
    /// 锁：<see cref="m_Lock"/> 保护会话状态、去重表、新报告队列、用户键、卡死状态；面包屑环单独一把锁（日志热路径不与报告生成争用）；
    /// 文件 IO 在 <see cref="CrashStore"/> 自己的锁里且总在 <see cref="m_Lock"/> 之外进行。
    /// 主线程心跳是一个 long（double 位模式）原子写，<see cref="Update"/> 每帧只有一次原子写 + 一次 volatile 读，无锁无分配。
    /// </summary>
    internal sealed partial class CrashManager : FrameworkModule, ICrashManager
    {
        private struct Breadcrumb
        {
            public double Time;
            public DiagLevel Level;
            public string Category;
            public string Message;
        }

        private struct UploadCompletion
        {
            public CrashReport Report;
            public bool Success;
        }

        private const int MaxMessageChars = 4096;
        private const int MaxStackChars = 16384;
        private const int MaxBreadcrumbChars = 512;
        private const int MaxUploadFailuresPerSession = 3;
        private const double MarkerMinIntervalSeconds = 1.0;

        private static readonly Stopwatch s_Clock = Stopwatch.StartNew();

        private readonly object m_Lock = new object();
        private readonly object m_CrumbLock = new object();
        private readonly Dictionary<uint, CrashReport> m_ByFingerprint = new Dictionary<uint, CrashReport>();
        private readonly List<CrashReport> m_SessionReports = new List<CrashReport>();
        private readonly List<CrashReport> m_NewReports = new List<CrashReport>();
        private readonly List<CrashReport> m_NewReportsScratch = new List<CrashReport>();
        private readonly Dictionary<string, string> m_UserKeys = new Dictionary<string, string>();
        private readonly List<ICrashReportListener> m_Listeners = new List<ICrashReportListener>();
        private readonly List<string> m_PendingUploads = new List<string>();
        private readonly List<UploadCompletion> m_UploadCompletions = new List<UploadCompletion>();
        private readonly Action<CrashReport, bool> m_OnUploadComplete;

        private Breadcrumb[] m_Crumbs = new Breadcrumb[64];
        private int m_CrumbHead;
        private int m_CrumbCount;
        private volatile DiagLevel m_CrumbMinLevel = DiagLevel.Info;

        private volatile bool m_Enabled = true;
        private int m_MaxReportsPerSession = 32;
        private int m_MaxStoredReports = 20;
        private float m_HangThreshold = 5f;
        private bool m_WatchdogThreadEnabled = true;

        private CrashStore m_Store;
        private ICrashUploader m_Uploader;
        private ITelemetryManager m_Telemetry;

        // 会话（m_Lock）
        private bool m_InSession;
        private string m_SessionId;
        private string m_DeviceInfo;
        private string m_AppVersion;
        private double m_SessionStart;
        private string m_Phase;
        private SessionMarkerFlags m_Flags;
        private int m_SuspendCount;
        private int m_ReportSeq;
        private bool m_MarkerDirty;
        private double m_LastMarkerWrite;

        // 卡死（m_Lock；m_HangFlag 供主线程无锁探测）
        private long m_HeartbeatBits;
        private volatile int m_HangFlag;
        private CrashReport m_HangReport;
        private double m_HangStart;
        private int m_HangCount;

        // 看门狗线程
        private Thread m_Watchdog;
        private volatile bool m_StopWatchdog;

        // 上传（主线程）
        private CrashReport m_UploadInFlight;
        private string m_UploadInFlightPath;
        private int m_UploadFailures;
        private int m_Uploaded;

        private long m_TotalExceptions;
        private long m_TotalSuppressed;

        public CrashManager()
        {
            m_OnUploadComplete = OnUploadComplete;
        }

        /// <summary>测试注入时钟（秒）；null 用单调 Stopwatch。</summary>
        internal Func<double> ClockOverride;

        public override int Priority { get { return -100; } }   // 最后轮询、最先关闭：关闭时 EndSession 还能把汇总写进仍在会话中的遥测（-90）

        // ================================================================
        //  配置
        // ================================================================

        public bool Enabled { get { return m_Enabled; } set { m_Enabled = value; } }

        public int BreadcrumbCapacity
        {
            get { lock (m_CrumbLock) { return m_Crumbs.Length; } }
            set
            {
                int capacity = value < 1 ? 1 : value;
                lock (m_CrumbLock)
                {
                    m_Crumbs = new Breadcrumb[capacity];
                    m_CrumbHead = 0;
                    m_CrumbCount = 0;
                }
            }
        }

        public DiagLevel BreadcrumbMinLevel { get { return m_CrumbMinLevel; } set { m_CrumbMinLevel = value; } }

        public int MaxReportsPerSession
        {
            get { return m_MaxReportsPerSession; }
            set { m_MaxReportsPerSession = value < 1 ? 1 : value; }
        }

        public int MaxStoredReports
        {
            get { return m_MaxStoredReports; }
            set { m_MaxStoredReports = value < 1 ? 1 : value; }
        }

        public float HangThresholdSeconds
        {
            get { return m_HangThreshold; }
            set { m_HangThreshold = value < 0f ? 0f : value; }
        }

        public bool WatchdogThreadEnabled
        {
            get { return m_WatchdogThreadEnabled; }
            set { m_WatchdogThreadEnabled = value; }
        }

        public void Configure(string storeDirectory)
        {
            Framework.EnsureMainThread(nameof(Configure));
            if (m_InSession) throw new FrameworkException("CrashManager.Configure：须在 BeginSession 之前调用。");
            m_Store = string.IsNullOrEmpty(storeDirectory) ? null : new CrashStore(storeDirectory);
        }

        public void SetUploader(ICrashUploader uploader) { m_Uploader = uploader; }

        public void SetTelemetry(ITelemetryManager telemetry) { m_Telemetry = telemetry; }

        public void AddListener(ICrashReportListener listener)
        {
            if (listener == null) throw new FrameworkException("CrashManager.AddListener：listener 为空。");
            if (!m_Listeners.Contains(listener)) m_Listeners.Add(listener);
        }

        public bool RemoveListener(ICrashReportListener listener)
        {
            return m_Listeners.Remove(listener);
        }

        // ================================================================
        //  会话
        // ================================================================

        public bool InSession { get { lock (m_Lock) { return m_InSession; } } }

        public bool BeginSession(string sessionId, string deviceInfo, string appVersion)
        {
            Framework.EnsureMainThread(nameof(BeginSession));
            if (string.IsNullOrEmpty(sessionId)) throw new FrameworkException("CrashManager.BeginSession：sessionId 不能为空。");
            if (m_InSession) EndSession();

            double now = Now();
            lock (m_Lock)
            {
                m_SessionId = sessionId;
                m_DeviceInfo = deviceInfo ?? string.Empty;
                m_AppVersion = appVersion ?? string.Empty;
                m_SessionStart = now;
                m_Flags = SessionMarkerFlags.None;
                m_SuspendCount = 0;
                m_ByFingerprint.Clear();
                m_SessionReports.Clear();
                m_HangReport = null;
                m_HangFlag = 0;
                m_UploadFailures = 0;
                m_InSession = true;
            }

            WriteHeartbeat(now);
            bool previousAbnormal = false;
            if (m_Store != null)
            {
                previousAbnormal = DetectPreviousAbnormalExit(sessionId);
                m_Store.TrimReports(m_MaxStoredReports);
                RescanPendingUploads();
                WriteMarkerNow();
            }

            StartWatchdog();
            return previousAbnormal;
        }

        public void EndSession()
        {
            Framework.EnsureMainThread(nameof(EndSession));
            if (!InSession) return;
            StopWatchdog();
            if (m_HangFlag != 0) ResolveHang(Now());
            ProcessNewReports();
            RecordOccurrenceSummaries();
            lock (m_Lock) { m_InSession = false; }
            if (m_Store != null) m_Store.DeleteMarker();
        }

        public void SetPhase(string phase)
        {
            lock (m_Lock)
            {
                if (string.Equals(m_Phase, phase, StringComparison.Ordinal)) return;
                m_Phase = phase;
                m_MarkerDirty = true;
            }
        }

        public string Phase { get { lock (m_Lock) { return m_Phase; } } }

        public void SetUserKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new FrameworkException("CrashManager.SetUserKey：key 不能为空。");
            lock (m_Lock)
            {
                if (value == null) m_UserKeys.Remove(key);
                else m_UserKeys[key] = value;
            }
        }

        // ================================================================
        //  面包屑 / 日志
        // ================================================================

        public void AddBreadcrumb(string category, string message)
        {
            PushCrumb(DiagLevel.Info, category, message);
        }

        public void OnLog(in DiagEntry entry)
        {
            // 先报告再记面包屑：报告里的面包屑是"异常之前发生了什么"，异常本身留给之后的报告做上下文
            if (entry.Level == DiagLevel.Exception) ReportException(entry.Message, entry.StackTrace, false);
            if (entry.Level >= m_CrumbMinLevel) PushCrumb(entry.Level, "log", entry.Message);
        }

        public void Flush()
        {
            // ILogSink：报告在生成时已落盘；这里只把延迟中的会话标记刷掉（主线程外调用时跳过）
            if (Framework.IsMainThread() && m_Store != null && InSession) WriteMarkerNow();
        }

        private void PushCrumb(DiagLevel level, string category, string message)
        {
            double now = Now();
            lock (m_CrumbLock)
            {
                Breadcrumb[] ring = m_Crumbs;
                ring[m_CrumbHead].Time = now;
                ring[m_CrumbHead].Level = level;
                ring[m_CrumbHead].Category = category;
                ring[m_CrumbHead].Message = message;
                m_CrumbHead = (m_CrumbHead + 1) % ring.Length;
                if (m_CrumbCount < ring.Length) m_CrumbCount++;
            }
        }

        private string[] SnapshotCrumbs(double sessionStart)
        {
            lock (m_CrumbLock)
            {
                string[] lines = new string[m_CrumbCount];
                int start = (m_CrumbHead - m_CrumbCount + m_Crumbs.Length) % m_Crumbs.Length;
                for (int i = 0; i < m_CrumbCount; i++)
                {
                    Breadcrumb c = m_Crumbs[(start + i) % m_Crumbs.Length];
                    string text = Truncate(c.Message, MaxBreadcrumbChars);
                    lines[i] = "[" + (c.Time - sessionStart).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "] "
                               + c.Level + " " + (c.Category ?? string.Empty) + ": " + text;
                }

                return lines;
            }
        }

        // ================================================================
        //  异常
        // ================================================================

        public void ReportException(Exception exception, bool fatal)
        {
            if (exception == null) throw new FrameworkException("CrashManager.ReportException：exception 为空。");
            // 与 Unity 日志流一致用短类型名，两条路径进来的同一异常得到同一指纹
            ReportException(exception.GetType().Name + ": " + exception.Message, exception.StackTrace, fatal);
        }

        public void ReportException(string message, string stackTrace, bool fatal)
        {
            Interlocked.Increment(ref m_TotalExceptions);
            if (!m_Enabled) return;

            string msg = Truncate(message ?? string.Empty, MaxMessageChars);
            string stack = Truncate(stackTrace, MaxStackChars);
            uint fingerprint = CrashFingerprint.Compute(msg, stack);
            CrashReport report;
            bool isNew = false;
            lock (m_Lock)
            {
                CrashReport existing;
                if (m_ByFingerprint.TryGetValue(fingerprint, out existing))
                {
                    if (fatal && existing.Kind != CrashKind.Fatal)
                    {
                        // 同一事件的致命升级（未处理异常先经日志流以非致命进来，再由捕获方以 fatal 上报）：不另计次数
                        existing.Kind = CrashKind.Fatal;
                    }
                    else
                    {
                        existing.Occurrences++;
                        m_TotalSuppressed++;
                    }

                    if (!fatal) return;
                    report = existing;
                }
                else
                {
                    if (m_SessionReports.Count >= m_MaxReportsPerSession)
                    {
                        m_TotalSuppressed++;
                        return;
                    }

                    report = CreateReportLocked(fatal ? CrashKind.Fatal : CrashKind.Exception, msg, stack, fingerprint, Now());
                    m_ByFingerprint.Add(fingerprint, report);
                    m_SessionReports.Add(report);
                    isNew = true;
                }

                if (fatal) m_Flags |= SessionMarkerFlags.FatalReported;
            }

            Persist(report);
            // 落盘（StoragePath 就绪）之后才交给主线程，保证本会话就能排进上传队列
            if (isNew)
            {
                lock (m_Lock) { m_NewReports.Add(report); }
            }

            if (fatal && m_Store != null) WriteMarkerNow();
        }

        // 调用方持 m_Lock
        private CrashReport CreateReportLocked(CrashKind kind, string message, string stack, uint fingerprint, double now)
        {
            CrashReport r = new CrashReport();
            r.ReportId = NowUnixMillis().ToString("D13") + "-" + ShortSession(m_SessionId) + "-" + (++m_ReportSeq).ToString("D3");
            r.Kind = kind;
            r.SessionId = m_SessionId;
            r.AppVersion = m_AppVersion;
            r.DeviceInfo = m_DeviceInfo;
            r.UnixMillis = NowUnixMillis();
            r.SessionSeconds = m_InSession ? (float)(now - m_SessionStart) : 0f;
            r.Message = message;
            r.StackTrace = stack;
            r.Fingerprint = fingerprint;
            r.Phase = m_Phase;
            r.Breadcrumbs = SnapshotCrumbs(m_SessionStart);
            r.UserKeys = SnapshotUserKeysLocked();
            return r;
        }

        private KeyValuePair<string, string>[] SnapshotUserKeysLocked()
        {
            if (m_UserKeys.Count == 0) return Array.Empty<KeyValuePair<string, string>>();
            KeyValuePair<string, string>[] keys = new KeyValuePair<string, string>[m_UserKeys.Count];
            int i = 0;
            foreach (KeyValuePair<string, string> kv in m_UserKeys) keys[i++] = kv;
            return keys;
        }

        private void Persist(CrashReport report)
        {
            if (m_Store == null) return;
            byte[] bytes;
            lock (m_Lock) { bytes = report.ToBytes(); }   // Occurrences 等字段可能被其他线程同时修改
            if (m_Store.WriteReport(report, bytes)) report.StoragePath = m_Store.ReportPath(report.ReportId);
        }

        // ================================================================
        //  前后台 / 低内存
        // ================================================================

        public void NotifyBackground(bool background)
        {
            lock (m_Lock)
            {
                if (background) m_Flags |= SessionMarkerFlags.Background;
                else m_Flags &= ~SessionMarkerFlags.Background;
            }

            if (!background) WriteHeartbeat(Now());   // 回前台：后台时长不算卡死
            if (m_Store != null && InSession) WriteMarkerNow();
        }

        public void NotifyLowMemory()
        {
            PushCrumb(DiagLevel.Warning, "system", "low memory warning");
            lock (m_Lock) { m_Flags |= SessionMarkerFlags.LowMemory; }
            if (m_Store != null && InSession) WriteMarkerNow();
        }

        // ================================================================
        //  ANR
        // ================================================================

        public void SuspendHangDetection()
        {
            lock (m_Lock) { m_SuspendCount++; }
        }

        public void ResumeHangDetection()
        {
            bool resumed;
            lock (m_Lock)
            {
                if (m_SuspendCount == 0) throw new FrameworkException("CrashManager.ResumeHangDetection：与 Suspend 不配对。");
                m_SuspendCount--;
                resumed = m_SuspendCount == 0;
            }

            if (resumed) WriteHeartbeat(Now());
        }

        public bool IsHanging { get { return m_HangFlag != 0; } }

        public int HangCount { get { lock (m_Lock) { return m_HangCount; } } }

        public void PollWatchdog(double nowSeconds)
        {
            float threshold = m_HangThreshold;
            if (threshold <= 0f || !m_Enabled) return;
            CrashReport report;
            lock (m_Lock)
            {
                if (!m_InSession || m_HangReport != null || m_SuspendCount > 0) return;
                if ((m_Flags & SessionMarkerFlags.Background) != 0) return;
                double heartbeat = ReadHeartbeat();
                double gap = nowSeconds - heartbeat;
                if (gap < threshold) return;

                m_HangStart = heartbeat;
                m_HangCount++;
                string phase = m_Phase ?? string.Empty;
                report = CreateReportLocked(CrashKind.Hang, "Main thread unresponsive (phase: " + phase + ")", null,
                    CrashFingerprint.ComputeMessage("hang|" + phase), nowSeconds);
                report.HangSeconds = (float)gap;
                m_HangReport = report;
                m_Flags |= SessionMarkerFlags.Hanging;
                m_HangFlag = 1;
            }

            // 主线程卡着：此刻就落盘，进程若被系统杀掉，报告与"卡死中"标记都已在盘上
            Persist(report);
            if (m_Store != null) WriteMarkerNow();
        }

        private void ResolveHang(double now)
        {
            CrashReport report;
            lock (m_Lock)
            {
                report = m_HangReport;
                m_HangReport = null;
                m_HangFlag = 0;
                m_Flags &= ~SessionMarkerFlags.Hanging;
                if (report == null) return;
                float duration = (float)(now - m_HangStart);
                if (duration > report.HangSeconds) report.HangSeconds = duration;
                if (m_SessionReports.Count < m_MaxReportsPerSession) m_SessionReports.Add(report);
                else m_TotalSuppressed++;
                m_NewReports.Add(report);
            }

            Persist(report);   // 同 ReportId 覆盖，写入最终时长
            if (m_Store != null) WriteMarkerNow();
        }

        private void StartWatchdog()
        {
            if (!m_WatchdogThreadEnabled || m_HangThreshold <= 0f || m_Watchdog != null) return;
            m_StopWatchdog = false;
            int pollMs = (int)(m_HangThreshold * 1000f / 4f);
            if (pollMs < 50) pollMs = 50;
            if (pollMs > 500) pollMs = 500;
            Thread thread = new Thread(() => WatchdogLoop(pollMs));
            thread.IsBackground = true;
            thread.Name = "EjoyFramework.CrashWatchdog";
            m_Watchdog = thread;
            thread.Start();
        }

        private void StopWatchdog()
        {
            Thread thread = m_Watchdog;
            if (thread == null) return;
            m_StopWatchdog = true;
            thread.Join(2000);
            m_Watchdog = null;
        }

        private void WatchdogLoop(int pollMs)
        {
            while (!m_StopWatchdog)
            {
                Thread.Sleep(pollMs);
                if (m_StopWatchdog) break;
                try
                {
                    PollWatchdog(Now());
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("CrashManager 看门狗异常：{0}", ex);
                }
            }
        }

        // ================================================================
        //  驱动
        // ================================================================

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            double now = Now();
            WriteHeartbeat(now);
            if (m_HangFlag != 0) ResolveHang(now);

            if (m_MarkerDirty && m_Store != null && now - m_LastMarkerWrite >= MarkerMinIntervalSeconds && InSession)
            {
                WriteMarkerNow();
            }

            ProcessNewReports();
            ProcessUploadCompletions();
            TryUpload();
        }

        private void ProcessNewReports()
        {
            lock (m_Lock)
            {
                if (m_NewReports.Count == 0) return;
                m_NewReportsScratch.AddRange(m_NewReports);
                m_NewReports.Clear();
            }

            for (int i = 0; i < m_NewReportsScratch.Count; i++)
            {
                CrashReport report = m_NewReportsScratch[i];
                if (report.StoragePath != null && !m_PendingUploads.Contains(report.StoragePath) && report.StoragePath != m_UploadInFlightPath)
                {
                    m_PendingUploads.Add(report.StoragePath);
                }

                RecordTelemetry(report);
                for (int l = 0; l < m_Listeners.Count; l++)
                {
                    try { m_Listeners[l].OnCrashReport(report); }
                    catch (Exception ex) { FrameworkLog.Error("CrashManager 监听器异常：{0}", ex); }
                }
            }

            m_NewReportsScratch.Clear();
        }

        // I3 = 截至此刻的累计次数；同一报告之后次数再涨，会话结束时补记一条（后台按 (会话, 指纹) 取 I3 最大值）
        private void RecordTelemetry(CrashReport report)
        {
            ITelemetryManager telemetry = m_Telemetry;
            if (telemetry == null || !telemetry.IsSampling) return;
            int occurrences;
            lock (m_Lock)
            {
                occurrences = report.Occurrences;
                report.TelemetryOccurrences = occurrences;
            }

            TelemetryRecord r = default(TelemetryRecord);
            r.Kind = TelemetryKind.Crash;
            r.I0 = (int)report.Kind;
            r.I1 = (int)CrashFingerprint.ComputeMessage(report.Message);
            r.I2 = (int)report.Fingerprint;
            r.I3 = occurrences;
            r.F0 = report.HangSeconds;
            r.F1 = report.SessionSeconds;
            telemetry.Record(ref r);
        }

        // 会话结束时，对"已记过遥测但之后又被去重吞掉更多次"的报告补记一条（I3 = 总次数）
        private void RecordOccurrenceSummaries()
        {
            m_NewReportsScratch.Clear();
            lock (m_Lock)
            {
                for (int i = 0; i < m_SessionReports.Count; i++)
                {
                    CrashReport report = m_SessionReports[i];
                    if (report.TelemetryOccurrences > 0 && report.Occurrences > report.TelemetryOccurrences) m_NewReportsScratch.Add(report);
                }
            }

            for (int i = 0; i < m_NewReportsScratch.Count; i++) RecordTelemetry(m_NewReportsScratch[i]);
            m_NewReportsScratch.Clear();
        }

        // ================================================================
        //  状态 / 关闭
        // ================================================================

        public void GetSessionReports(List<CrashReport> results)
        {
            if (results == null) throw new FrameworkException("CrashManager.GetSessionReports：results 为空。");
            results.Clear();
            lock (m_Lock) { results.AddRange(m_SessionReports); }
        }

        public long TotalExceptions { get { return Interlocked.Read(ref m_TotalExceptions); } }

        public long TotalSuppressed { get { lock (m_Lock) { return m_TotalSuppressed; } } }

        public override void Shutdown()
        {
            // 框架关闭 = 正常退出：结束会话（删除标记）。异常退出不会走到这里。
            if (InSession && Framework.IsMainThread()) EndSession();
            StopWatchdog();
            m_Listeners.Clear();
            m_Uploader = null;
            m_Telemetry = null;
            lock (m_Lock)
            {
                m_NewReports.Clear();
                m_UploadCompletions.Clear();
                m_InSession = false;
            }
        }

        // ================================================================
        //  内部
        // ================================================================

        private double Now()
        {
            Func<double> clock = ClockOverride;
            return clock != null ? clock() : s_Clock.ElapsedTicks / (double)Stopwatch.Frequency;
        }

        private void WriteHeartbeat(double now)
        {
            Interlocked.Exchange(ref m_HeartbeatBits, BitConverter.DoubleToInt64Bits(now));
        }

        private double ReadHeartbeat()
        {
            return BitConverter.Int64BitsToDouble(Interlocked.Read(ref m_HeartbeatBits));
        }

        private static long NowUnixMillis()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string ShortSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return "none";
            string s = sessionId.Length > 8 ? sessionId.Substring(0, 8) : sessionId;
            char[] chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (!ok) chars[i] = '_';   // 进文件名，只留安全字符
            }

            return new string(chars);
        }

        private static string Truncate(string s, int max)
        {
            if (s == null || s.Length <= max) return s;
            return s.Substring(0, max);
        }
    }
}
