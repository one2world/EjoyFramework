//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 诊断日志等级。
    /// 独立于框架内部 <see cref="LogLevel"/>：这是面向"崩溃上报 / 远端日志"管道的等级，
    /// 额外区分 <see cref="Exception"/>（带堆栈的未处理异常），便于崩溃上报后端做差异化处理。
    /// </summary>
    public enum DiagLevel
    {
        /// <summary>调试。</summary>
        Debug = 0,

        /// <summary>普通信息。</summary>
        Info,

        /// <summary>警告。</summary>
        Warning,

        /// <summary>错误。</summary>
        Error,

        /// <summary>异常（通常带堆栈，会转发给崩溃上报器）。</summary>
        Exception,
    }

    /// <summary>
    /// 一条诊断日志条目（值类型，避免热路径分配）。
    /// 通过 <c>in</c> 引用传给各 Sink，零拷贝。
    /// </summary>
    public struct DiagEntry
    {
        /// <summary>等级。</summary>
        public DiagLevel Level;

        /// <summary>日志正文。</summary>
        public string Message;

        /// <summary>堆栈（可空，Error/Exception 时通常非空）。</summary>
        public string StackTrace;

        /// <summary>产生时刻（Unix 毫秒，UTC）。</summary>
        public long UnixMillis;
    }

    /// <summary>
    /// 日志接收器（Sink）接口。
    /// 由 Unity 层（文件 Sink）或业务层（远端上报 Sink）实现，挂到 <see cref="IDiagnosticsManager"/>。
    /// 注意：<see cref="OnLog"/> 可能在<b>任意线程</b>被调用（Unity 的 logMessageReceivedThreaded
    /// 会在后台线程触发），实现必须自行保证线程安全。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>
        /// 接收一条日志（以 <c>in</c> 引用传入，避免值类型拷贝）。
        /// 实现不应抛异常；即便抛出，管理器也会捕获并隔离，不影响其他 Sink。
        /// </summary>
        void OnLog(in DiagEntry entry);

        /// <summary>
        /// 将缓冲数据落盘 / 推送。可能在任意线程被调用。
        /// </summary>
        void Flush();
    }

    /// <summary>
    /// 崩溃上报器接口。桥接真实 SDK（Crashlytics / Bugly 等，框架不内置，仅提供注入点）。
    /// <see cref="IDiagnosticsManager"/> 会把 Error/Exception 级别的日志额外转发给已注入的上报器。
    /// </summary>
    public interface ICrashReporter
    {
        /// <summary>
        /// 设置自定义键值（如用户 ID、关卡、阵营），随崩溃报告一并上报。
        /// </summary>
        void SetUserKey(string key, string value);

        /// <summary>
        /// 上报一条非致命错误 / 异常。
        /// </summary>
        void Report(string message, string stackTrace);
    }
}
