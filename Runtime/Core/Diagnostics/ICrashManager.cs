//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 崩溃 / ANR / 异常退出采集——"上线后到底崩在哪"的框架自有管道（与桥接第三方 SDK 的 <see cref="ICrashReporter"/> 并存）。
    ///
    /// 能力：
    ///   • <b>异常</b>：<see cref="ReportException(string,string,bool)"/>（任意线程）→ 按 <see cref="CrashFingerprint"/> 会话内去重
    ///     （同一 bug 每帧抛也只出一份报告，另计次数），每会话上限 <see cref="MaxReportsPerSession"/>；首次出现立即落盘。
    ///   • <b>面包屑</b>：最近 <see cref="BreadcrumbCapacity"/> 条日志 / 业务事件的环形缓冲，随报告附带。
    ///     本类实现 <see cref="ILogSink"/>，挂到 <see cref="IDiagnosticsManager"/> 即自动收集引擎日志与 Exception。
    ///   • <b>ANR</b>：主线程每帧心跳（<see cref="FrameworkModule.Update"/>），看门狗线程发现停顿超过 <see cref="HangThresholdSeconds"/>
    ///     立即落一份 Hang 报告（进程若随后被系统杀掉，报告已在盘上）；恢复后补记实际卡顿时长。
    ///     已知的长阻塞（同步加载）用 <see cref="SuspendHangDetection"/>/<see cref="ResumeHangDetection"/> 包住。
    ///   • <b>异常退出</b>：会话期间盘上保留一个会话标记（含当前阶段 <see cref="SetPhase"/>、前后台 / 低内存 / 卡死状态、面包屑）；
    ///     <see cref="EndSession"/> 正常结束会删除它。下次 <see cref="BeginSession"/> 发现残留标记即生成
    ///     AbnormalExit / BackgroundKill / LowMemoryKill / HangKill 报告——这是原生崩溃与 OOM 被杀唯一可靠的托管侧信号。
    ///   • <b>上报</b>：盘上待传报告经 <see cref="ICrashUploader"/> 逐个上传（单个在途，回调任意线程→主线程结算），
    ///     成功删除，连续失败本会话停止重试；盘上最多保留 <see cref="MaxStoredReports"/> 份（超出删最旧）。
    ///   • <b>遥测</b>：每份新报告写一条 <c>TelemetryKind.Crash</c> 记录（I0=kind I1=消息指纹 I2=指纹 I3=次数 F0=卡顿秒）。
    ///
    /// 线程：ReportException / AddBreadcrumb / OnLog 可在任意线程；其余在主线程。
    /// </summary>
    public interface ICrashManager : ILogSink
    {
        /// <summary>总开关。关闭后不再生成新报告（面包屑仍收集）。</summary>
        bool Enabled { get; set; }

        /// <summary>面包屑环容量。默认 64。修改会清空现有面包屑。</summary>
        int BreadcrumbCapacity { get; set; }

        /// <summary>低于此等级的日志不进面包屑。默认 Info。</summary>
        DiagLevel BreadcrumbMinLevel { get; set; }

        /// <summary>每会话最多生成的不同报告数。默认 32。</summary>
        int MaxReportsPerSession { get; set; }

        /// <summary>盘上最多保留的待上传报告数。默认 20。</summary>
        int MaxStoredReports { get; set; }

        /// <summary>主线程停顿多少秒判为卡死；0 关闭 ANR 检测。默认 5。</summary>
        float HangThresholdSeconds { get; set; }

        /// <summary>是否由管理器自带的看门狗线程轮询（默认 true）；关闭后可调用 <see cref="PollWatchdog"/> 自行驱动。</summary>
        bool WatchdogThreadEnabled { get; set; }

        /// <summary>报告与会话标记目录；null/空 = 只在内存（不落盘、不检测异常退出、不上传）。须在 BeginSession 前设置。</summary>
        void Configure(string storeDirectory);

        /// <summary>开始会话。返回上次会话是否异常结束（已生成对应报告）。</summary>
        bool BeginSession(string sessionId, string deviceInfo, string appVersion);

        /// <summary>正常结束会话：删除会话标记，停止看门狗。</summary>
        void EndSession();

        bool InSession { get; }

        /// <summary>当前游戏阶段（如 "Loading:Region_12"、"Combat:Boss_3"）。随报告与会话标记记录，相同值重复设置无开销。</summary>
        void SetPhase(string phase);

        string Phase { get; }

        /// <summary>随报告附带的键值（用户 id、服务器、画质档……）。value 为 null 表示删除。</summary>
        void SetUserKey(string key, string value);

        /// <summary>业务面包屑（任意线程）。</summary>
        void AddBreadcrumb(string category, string message);

        /// <summary>
        /// 上报异常（任意线程）。fatal=true 表示进程即将终止：同步落盘并刷新会话标记。
        /// 同指纹已有非致命报告时，fatal 上报视为同一事件的升级（改为 Fatal，不另计次数）——
        /// 未处理异常通常先经日志流以非致命进来，再由捕获方以 fatal 上报。
        /// </summary>
        void ReportException(string message, string stackTrace, bool fatal);

        /// <summary>上报异常对象（任意线程）。</summary>
        void ReportException(Exception exception, bool fatal);

        /// <summary>应用进入 / 离开后台（移动端 OnApplicationPause）。后台期间暂停 ANR 检测并立即刷新会话标记。</summary>
        void NotifyBackground(bool background);

        /// <summary>收到系统低内存警告。立即刷新会话标记。</summary>
        void NotifyLowMemory();

        /// <summary>暂停 ANR 检测（引用计数；用于已知的长阻塞如同步加载）。</summary>
        void SuspendHangDetection();

        void ResumeHangDetection();

        /// <summary>看门狗轮询一次（线程安全）。<paramref name="nowSeconds"/> 与心跳同一时钟。</summary>
        void PollWatchdog(double nowSeconds);

        /// <summary>当前是否处于已判定的卡死中。</summary>
        bool IsHanging { get; }

        void AddListener(ICrashReportListener listener);

        bool RemoveListener(ICrashReportListener listener);

        void SetUploader(ICrashUploader uploader);

        /// <summary>设置遥测出口（可空）。</summary>
        void SetTelemetry(ITelemetryManager telemetry);

        /// <summary>把本会话已生成的报告拷到 <paramref name="results"/>（含去重后的次数）。</summary>
        void GetSessionReports(List<CrashReport> results);

        /// <summary>盘上待上传报告数（含在途）。</summary>
        int PendingUploadCount { get; }

        long TotalExceptions { get; }
        long TotalSuppressed { get; }
        int HangCount { get; }
        int UploadedCount { get; }
    }
}
