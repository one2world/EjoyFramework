//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>
    /// 文件日志 Sink（Unity 层）。
    ///
    /// 把诊断日志写入 <c>Application.persistentDataPath/logs/app.log</c>，按大小上限滚动：
    /// 写满后 app.log → app.1.log（保留 N=2 份历史），再开新 app.log。
    /// 每条格式：<c>[unixmillis][LEVEL] message</c>，若有堆栈则换行追加堆栈。
    ///
    /// 线程安全：<see cref="OnLog"/> 可能从后台线程被调用（Unity logMessageReceivedThreaded），
    /// 故所有对 <see cref="m_Writer"/> 的写入 / Flush / 滚动都在 <see cref="m_Lock"/> 下串行化。
    /// 写入失败（磁盘满 / 权限）被吞掉并禁用后续写入，绝不让日志 Sink 成为崩溃源。
    /// </summary>
    public sealed class FileLogSink : ILogSink
    {
        /// <summary>默认单文件大小上限（字节）。</summary>
        public const long DefaultMaxBytes = 1L * 1024 * 1024; // 1 MB

        /// <summary>默认保留的滚动历史份数（app.1.log ... app.N.log）。</summary>
        public const int DefaultMaxRollFiles = 2;

        private const string DirectoryName = "logs";
        private const string BaseName = "app";
        private const string Ext = ".log";

        private readonly object m_Lock = new object();
        private readonly string m_Directory;
        private readonly string m_BasePath;     // <dir>/app.log
        private readonly long m_MaxBytes;
        private readonly int m_MaxRollFiles;
        private readonly StringBuilder m_LineBuilder = new StringBuilder(256);

        private StreamWriter m_Writer;
        private long m_CurrentBytes;
        private bool m_Disabled;                // 一旦发生不可恢复的 IO 错误，禁用后续写入

        /// <summary>
        /// 构造文件日志 Sink。
        /// </summary>
        /// <param name="rootOverride">日志根目录覆盖（测试用）。空则用 persistentDataPath/logs。</param>
        /// <param name="maxBytes">单文件大小上限（&lt;=0 用默认）。</param>
        /// <param name="maxRollFiles">滚动历史份数（&lt;=0 用默认）。</param>
        public FileLogSink(string rootOverride = null, long maxBytes = 0, int maxRollFiles = 0)
        {
            m_Directory = string.IsNullOrEmpty(rootOverride)
                ? Path.Combine(Application.persistentDataPath, DirectoryName)
                : rootOverride;
            m_BasePath = Path.Combine(m_Directory, BaseName + Ext);
            m_MaxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
            m_MaxRollFiles = maxRollFiles > 0 ? maxRollFiles : DefaultMaxRollFiles;

            try
            {
                if (!Directory.Exists(m_Directory)) Directory.CreateDirectory(m_Directory);
                OpenWriterLocked();
            }
            catch (Exception ex)
            {
                m_Disabled = true;
                // 直接用 FrameworkLog（不经 Diagnostics），避免反喂诊断管道造成递归。
                FrameworkLog.Error("FileLogSink init failed, disabled: {0}", ex);
            }
        }

        public void OnLog(in DiagEntry entry)
        {
            // 在锁外构造行文本会与共享的 m_LineBuilder 冲突，故整体串行化。
            lock (m_Lock)
            {
                if (m_Disabled || m_Writer == null) return;

                string line = BuildLineLocked(in entry);
                try
                {
                    if (m_CurrentBytes + EstimateBytes(line) > m_MaxBytes)
                    {
                        RollLocked();
                    }

                    m_Writer.Write(line);
                    m_CurrentBytes += EstimateBytes(line);
                }
                catch (Exception ex)
                {
                    m_Disabled = true;
                    SafeCloseLocked();
                    FrameworkLog.Error("FileLogSink write failed, disabled: {0}", ex);
                }
            }
        }

        public void Flush()
        {
            lock (m_Lock)
            {
                if (m_Disabled || m_Writer == null) return;
                try { m_Writer.Flush(); }
                catch (Exception ex)
                {
                    m_Disabled = true;
                    SafeCloseLocked();
                    FrameworkLog.Error("FileLogSink flush failed, disabled: {0}", ex);
                }
            }
        }

        /// <summary>
        /// 关闭并释放底层文件句柄（最终落盘）。可重复调用。
        /// </summary>
        public void Close()
        {
            lock (m_Lock)
            {
                SafeCloseLocked();
            }
        }

        // ================================================================
        //  内部实现（均要求持有 m_Lock）
        // ================================================================

        private string BuildLineLocked(in DiagEntry entry)
        {
            m_LineBuilder.Clear();
            m_LineBuilder.Append('[').Append(entry.UnixMillis).Append("][")
                         .Append(entry.Level.ToString().ToUpperInvariant()).Append("] ")
                         .Append(entry.Message ?? string.Empty);
            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                m_LineBuilder.Append('\n').Append(entry.StackTrace);
            }
            m_LineBuilder.Append('\n');
            return m_LineBuilder.ToString();
        }

        private static long EstimateBytes(string s)
        {
            // 估算 UTF-8 字节数即可（用于滚动阈值判断，无需精确）。
            return s == null ? 0 : Encoding.UTF8.GetByteCount(s);
        }

        private void OpenWriterLocked()
        {
            // 追加模式打开，保留崩溃前已写入的内容。
            var fs = new FileStream(m_BasePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            m_Writer = new StreamWriter(fs, new UTF8Encoding(false));
            m_Writer.AutoFlush = false;
            m_CurrentBytes = fs.Length;
        }

        // 滚动：关闭当前 writer → app.(N-1).log→app.N.log ... app.log→app.1.log → 重开新 app.log。
        private void RollLocked()
        {
            SafeCloseLocked();

            // 删除最老一份。
            string oldest = RollPath(m_MaxRollFiles);
            TryDelete(oldest);

            // app.(i-1).log → app.i.log（从大到小，避免覆盖）。
            for (int i = m_MaxRollFiles; i >= 2; i--)
            {
                string from = RollPath(i - 1);
                string to = RollPath(i);
                TryMove(from, to);
            }

            // app.log → app.1.log
            TryMove(m_BasePath, RollPath(1));

            OpenWriterLocked();
        }

        private string RollPath(int index)
        {
            return Path.Combine(m_Directory, BaseName + "." + index + Ext);
        }

        private void SafeCloseLocked()
        {
            if (m_Writer == null) return;
            try { m_Writer.Flush(); } catch { /* 吞掉，下面继续释放 */ }
            try { m_Writer.Dispose(); } catch { /* 吞掉 */ }
            m_Writer = null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { FrameworkLog.Warning("FileLogSink delete '{0}' threw: {1}", path, ex); }
        }

        private static void TryMove(string from, string to)
        {
            try
            {
                if (!File.Exists(from)) return;
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
            catch (Exception ex) { FrameworkLog.Warning("FileLogSink move '{0}'->'{1}' threw: {2}", from, to, ex); }
        }
    }
}
