//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 诊断管理器实现（崩溃上报 + 日志 Sink / 远端日志）。
    ///
    /// 职责：把一条诊断日志分发给所有已注册的 <see cref="ILogSink"/>（&gt;= MinLevel），
    /// 并把 Error/Exception 额外转发给注入的 <see cref="ICrashReporter"/>。
    ///
    /// 线程安全：<see cref="Log"/> 可能从后台线程被调用（Unity logMessageReceivedThreaded），
    /// 故 Sink 集合的增删与"分发前取快照"全部在 <see cref="m_Lock"/> 下完成。分发本身在快照数组上
    /// 进行（锁外），允许 Sink 回调里再注册新 Sink 而不破坏当前分发，也避免在持锁期间执行可能较慢的
    /// Sink IO。崩溃上报器引用为 volatile 字段，读写无需进锁。
    ///
    /// 防递归：本类只在<b>自身内部错误</b>（Sink/Reporter 抛异常）时用 <c>FrameworkLog</c> 记录，
    /// 且经 <see cref="m_InternalLogGuard"/>（ThreadStatic 重入标志）守护——绝不把诊断管道自己产生的
    /// 错误再喂回诊断管道，从而杜绝"日志→Sink抛异常→记日志→……"的无限递归。
    ///
    /// 核心层引擎无关：仅使用 System.* ，不引用 UnityEngine。
    /// </summary>
    internal sealed class DiagnosticsManager : FrameworkModule, IDiagnosticsManager
    {
        private readonly object m_Lock = new object();
        private readonly List<ILogSink> m_Sinks = new List<ILogSink>();

        // 分发快照：仅在 Sink 集合变更时重建，热路径（无变更）零分配复用。
        private ILogSink[] m_Snapshot = Array.Empty<ILogSink>();
        private bool m_SnapshotDirty = true;

        // volatile：崩溃上报器引用可被任意线程读取（分发线程）/ 主线程写入。
        private volatile ICrashReporter m_CrashReporter;

        // volatile：MinLevel 可被任意线程读取。
        private volatile DiagLevel m_MinLevel = DiagLevel.Debug;

        // 防递归：标记"当前线程正处在记录内部错误的过程中"，避免内部错误再次触发分发链。
        [ThreadStatic] private static bool m_InternalLogGuard;

        // Priority 0：基础服务模块，无依赖。
        public override int Priority { get { return 0; } }

        public DiagLevel MinLevel
        {
            get { return m_MinLevel; }
            set { m_MinLevel = value; }
        }

        public void AddSink(ILogSink sink)
        {
            if (sink == null)
            {
                InternalWarning("Diagnostics.AddSink: sink is null.");
                return;
            }

            lock (m_Lock)
            {
                m_Sinks.Add(sink);
                m_SnapshotDirty = true;
            }
        }

        public bool RemoveSink(ILogSink sink)
        {
            if (sink == null) return false;

            lock (m_Lock)
            {
                bool removed = m_Sinks.Remove(sink);
                if (removed) m_SnapshotDirty = true;
                return removed;
            }
        }

        public void SetCrashReporter(ICrashReporter reporter)
        {
            m_CrashReporter = reporter;
        }

        public void Log(DiagLevel level, string message, string stackTrace = null)
        {
            // 等级过滤（注意：volatile 读一次）。
            if (level < m_MinLevel) return;

            DiagEntry entry = new DiagEntry
            {
                Level = level,
                Message = message ?? string.Empty,
                StackTrace = stackTrace,
                UnixMillis = NowUnixMillis(),
            };

            Dispatch(in entry);

            // Error / Exception 额外转发给崩溃上报器。
            if (level >= DiagLevel.Error)
            {
                ForwardToCrashReporter(entry.Message, entry.StackTrace);
            }
        }

        public void ReportException(Exception ex, string context = null)
        {
            if (ex == null)
            {
                InternalWarning("Diagnostics.ReportException: exception is null.");
                return;
            }

            string message = string.IsNullOrEmpty(context)
                ? ex.GetType().Name + ": " + ex.Message
                : context + " | " + ex.GetType().Name + ": " + ex.Message;

            // ex.StackTrace 在未抛出的异常上可能为空——直接用，下游 Sink 自行容错。
            Log(DiagLevel.Exception, message, ex.StackTrace);
        }

        public void FlushAll()
        {
            ILogSink[] snapshot = GetSnapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ILogSink sink = snapshot[i];
                if (sink == null) continue;
                try { sink.Flush(); }
                catch (Exception ex) { InternalError("Diagnostics sink Flush threw", ex); }
            }
        }

        public void SetUserKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                InternalWarning("Diagnostics.SetUserKey: key is invalid.");
                return;
            }

            ICrashReporter reporter = m_CrashReporter;
            if (reporter == null) return;

            try { reporter.SetUserKey(key, value); }
            catch (Exception ex) { InternalError("Diagnostics crash reporter SetUserKey threw", ex); }
        }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            // 关闭前确保缓冲落盘。
            FlushAll();

            lock (m_Lock)
            {
                m_Sinks.Clear();
                m_Snapshot = Array.Empty<ILogSink>();
                m_SnapshotDirty = false;
            }
            m_CrashReporter = null;
        }

        // ================================================================
        //  内部实现
        // ================================================================

        // 当前 Unix 毫秒（UTC）。long 基钟，跨平台一致，不依赖 UnityEngine.Time。
        private static long NowUnixMillis()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // 取一份 Sink 快照，分发在快照上进行（锁外），允许 Sink 回调里再注册 Sink。
        private ILogSink[] GetSnapshot()
        {
            lock (m_Lock)
            {
                if (m_SnapshotDirty)
                {
                    m_Snapshot = m_Sinks.Count == 0 ? Array.Empty<ILogSink>() : m_Sinks.ToArray();
                    m_SnapshotDirty = false;
                }
                return m_Snapshot;
            }
        }

        private void Dispatch(in DiagEntry entry)
        {
            ILogSink[] snapshot = GetSnapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ILogSink sink = snapshot[i];
                if (sink == null) continue;
                try { sink.OnLog(in entry); }
                catch (Exception ex) { InternalError("Diagnostics sink OnLog threw", ex); }
            }
        }

        private void ForwardToCrashReporter(string message, string stackTrace)
        {
            ICrashReporter reporter = m_CrashReporter;
            if (reporter == null) return;

            try { reporter.Report(message, stackTrace); }
            catch (Exception ex) { InternalError("Diagnostics crash reporter Report threw", ex); }
        }

        // 内部错误记录：用 FrameworkLog，但经线程级重入标志守护，绝不把诊断管道自身的错误
        // 再次喂回 Diagnostics（本类不订阅 FrameworkLog，递归本不会发生；此守护是对未来
        // "FrameworkLog -> Diagnostics 桥接"的额外防线，且避免一次内部失败连环放大）。
        private static void InternalError(string what, Exception ex)
        {
            if (m_InternalLogGuard) return;
            m_InternalLogGuard = true;
            try { FrameworkLog.Error("{0}: {1}", what, ex); }
            catch { /* 最后兜底：彻底吞掉，诊断路径绝不成为崩溃源 */ }
            finally { m_InternalLogGuard = false; }
        }

        private static void InternalWarning(string what)
        {
            if (m_InternalLogGuard) return;
            m_InternalLogGuard = true;
            try { FrameworkLog.Warning(what); }
            catch { /* 兜底吞掉 */ }
            finally { m_InternalLogGuard = false; }
        }
    }
}
