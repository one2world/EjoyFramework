//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 诊断管理器接口（崩溃上报 + 日志 Sink / 远端日志）。
    ///
    /// 设计：本模块独立于框架内部的 <c>FrameworkLog</c>，专门承接 Unity 的 log/exception 流，
    /// 将其分发到一组可插拔的 <see cref="ILogSink"/>（内置文件 Sink + 业务注入的远端 Sink），
    /// 并把 Error/Exception 额外转发给注入的 <see cref="ICrashReporter"/>（桥接 Crashlytics/Bugly 等真实 SDK）。
    ///
    /// 线程：<see cref="Log"/> / <see cref="ReportException"/> 可能从后台线程被调用
    /// （Unity 的 logMessageReceivedThreaded、AppDomain.UnhandledException），实现需线程安全。
    /// </summary>
    public interface IDiagnosticsManager
    {
        /// <summary>
        /// 仅分发不低于此等级的日志到各 Sink 与崩溃上报器。默认 <see cref="DiagLevel.Debug"/>。
        /// </summary>
        DiagLevel MinLevel { get; set; }

        /// <summary>
        /// 添加一个日志 Sink。允许在分发过程中（Sink 回调里）注册新 Sink，不会破坏当前分发。
        /// </summary>
        void AddSink(ILogSink sink);

        /// <summary>
        /// 移除一个日志 Sink。返回是否确实移除了。
        /// </summary>
        bool RemoveSink(ILogSink sink);

        /// <summary>
        /// 设置崩溃上报器。传 null 表示清除（不再转发 Error/Exception）。
        /// </summary>
        void SetCrashReporter(ICrashReporter reporter);

        /// <summary>
        /// 记录一条诊断日志：分发给所有 Sink（&gt;= <see cref="MinLevel"/>）；
        /// 若等级为 Error/Exception，还会转发给崩溃上报器。
        /// </summary>
        /// <param name="level">等级。</param>
        /// <param name="message">正文。</param>
        /// <param name="stackTrace">堆栈（可空）。</param>
        void Log(DiagLevel level, string message, string stackTrace = null);

        /// <summary>
        /// 以 <see cref="DiagLevel.Exception"/> 记录一个异常（含其堆栈），并转发给崩溃上报器。
        /// </summary>
        /// <param name="ex">异常对象。</param>
        /// <param name="context">附加上下文（可空），会拼到消息前。</param>
        void ReportException(System.Exception ex, string context = null);

        /// <summary>
        /// 让所有 Sink 立即落盘 / 推送。
        /// </summary>
        void FlushAll();

        /// <summary>
        /// 设置随崩溃报告上报的自定义键值（转发给崩溃上报器）。
        /// </summary>
        void SetUserKey(string key, string value);
    }
}
