//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>会话标记状态位。</summary>
    [Flags]
    internal enum SessionMarkerFlags : byte
    {
        None = 0,
        Background = 1,
        LowMemory = 2,
        Hanging = 4,
        FatalReported = 8,
    }

    /// <summary>会话标记内容：会话存活期间保留在盘上，正常结束删除；残留即上次异常退出。</summary>
    internal struct SessionMarker
    {
        public string SessionId;
        public string AppVersion;
        public string DeviceInfo;
        public long WrittenUnixMillis;
        public float SessionSeconds;
        public string Phase;
        public SessionMarkerFlags Flags;
        public string[] Breadcrumbs;
    }

    internal enum MarkerReadResult
    {
        None,
        Ok,
        Corrupt,
    }

    /// <summary>
    /// 崩溃报告与会话标记的文件存储。所有写入先写 .tmp 再替换，进程在写到一半时被杀也不会留下半个文件。
    /// 可被任意线程调用（内部一把文件锁串行化）。报告文件名以 13 位 Unix 毫秒开头，字典序即时间序。
    /// </summary>
    internal sealed class CrashStore
    {
        public const string ReportExtension = ".ejcr";
        private const string MarkerFileName = "session.ejsm";
        private const uint MarkerMagic = 0x4D534A45;   // 'EJSM'
        private const int MarkerVersion = 1;

        private readonly string m_Directory;
        private readonly string m_MarkerPath;
        private readonly object m_FileLock = new object();

        public CrashStore(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new FrameworkException("CrashStore：directory 不能为空。");
            m_Directory = directory;
            m_MarkerPath = Path.Combine(directory, MarkerFileName);
        }

        public string Directory { get { return m_Directory; } }

        public string ReportPath(string reportId)
        {
            return Path.Combine(m_Directory, reportId + ReportExtension);
        }

        /// <summary>写报告（同 id 覆盖）。返回是否成功。</summary>
        public bool WriteReport(CrashReport report, byte[] bytes)
        {
            return WriteAtomic(ReportPath(report.ReportId), bytes);
        }

        /// <summary>按时间序列出盘上报告路径。</summary>
        public void ListReports(List<string> results)
        {
            results.Clear();
            lock (m_FileLock)
            {
                if (!System.IO.Directory.Exists(m_Directory)) return;
                string[] files = System.IO.Directory.GetFiles(m_Directory, "*" + ReportExtension);
                for (int i = 0; i < files.Length; i++)
                {
                    if (files[i].EndsWith(ReportExtension, StringComparison.OrdinalIgnoreCase)) results.Add(files[i]);
                }
            }

            results.Sort(StringComparer.Ordinal);
        }

        /// <summary>超过 max 份时删最旧的，返回删除数。</summary>
        public int TrimReports(int max)
        {
            List<string> files = new List<string>();
            ListReports(files);
            int removed = 0;
            for (int i = 0; i < files.Count - max; i++)
            {
                if (Delete(files[i])) removed++;
            }

            return removed;
        }

        public byte[] Read(string path)
        {
            lock (m_FileLock)
            {
                try
                {
                    return File.Exists(path) ? File.ReadAllBytes(path) : null;
                }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
        }

        public bool Delete(string path)
        {
            lock (m_FileLock)
            {
                try
                {
                    if (!File.Exists(path)) return false;
                    File.Delete(path);
                    return true;
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        // ---- 会话标记 ----

        public bool WriteMarker(ref SessionMarker marker)
        {
            byte[] bytes;
            using (MemoryStream ms = new MemoryStream(1024))
            using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(MarkerMagic);
                w.Write(MarkerVersion);
                w.Write(marker.SessionId ?? string.Empty);
                w.Write(marker.AppVersion ?? string.Empty);
                w.Write(marker.DeviceInfo ?? string.Empty);
                w.Write(marker.WrittenUnixMillis);
                w.Write(marker.SessionSeconds);
                w.Write(marker.Phase ?? string.Empty);
                w.Write((byte)marker.Flags);
                string[] crumbs = marker.Breadcrumbs ?? Array.Empty<string>();
                w.Write(crumbs.Length);
                for (int i = 0; i < crumbs.Length; i++) w.Write(crumbs[i] ?? string.Empty);
                w.Flush();
                bytes = ms.ToArray();
            }

            return WriteAtomic(m_MarkerPath, bytes);
        }

        public MarkerReadResult TryReadMarker(out SessionMarker marker)
        {
            marker = default(SessionMarker);
            byte[] bytes = Read(m_MarkerPath);
            if (bytes == null) bytes = Read(m_MarkerPath + ".tmp");   // 非原子回退路径上 Delete 与 Move 之间被杀
            if (bytes == null) return MarkerReadResult.None;
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes, false))
                using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (r.ReadUInt32() != MarkerMagic || r.ReadInt32() != MarkerVersion) return MarkerReadResult.Corrupt;
                    marker.SessionId = r.ReadString();
                    marker.AppVersion = r.ReadString();
                    marker.DeviceInfo = r.ReadString();
                    marker.WrittenUnixMillis = r.ReadInt64();
                    marker.SessionSeconds = r.ReadSingle();
                    marker.Phase = r.ReadString();
                    marker.Flags = (SessionMarkerFlags)r.ReadByte();
                    int count = r.ReadInt32();
                    if (count < 0 || count > bytes.Length) return MarkerReadResult.Corrupt;
                    marker.Breadcrumbs = new string[count];
                    for (int i = 0; i < count; i++) marker.Breadcrumbs[i] = r.ReadString();
                    return ms.Position == ms.Length ? MarkerReadResult.Ok : MarkerReadResult.Corrupt;
                }
            }
            catch (EndOfStreamException) { return MarkerReadResult.Corrupt; }
            catch (IOException) { return MarkerReadResult.Corrupt; }
            catch (ArgumentException) { return MarkerReadResult.Corrupt; }
            catch (FormatException) { return MarkerReadResult.Corrupt; }
        }

        public bool DeleteMarker()
        {
            Delete(m_MarkerPath + ".tmp");
            return Delete(m_MarkerPath);
        }

        public bool MarkerExists()
        {
            lock (m_FileLock) { return File.Exists(m_MarkerPath); }
        }

        private bool WriteAtomic(string path, byte[] bytes)
        {
            lock (m_FileLock)
            {
                string tmp = path + ".tmp";
                try
                {
                    System.IO.Directory.CreateDirectory(m_Directory);
                    using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(bytes, 0, bytes.Length);
                        fs.Flush(true);
                    }

                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tmp, path, null);   // 原子替换（Windows / POSIX rename）
                            return true;
                        }
                        catch (PlatformNotSupportedException)
                        {
                            File.Delete(path);
                        }
                    }

                    File.Move(tmp, path);
                    return true;
                }
                catch (IOException ex)
                {
                    FrameworkLog.Warning("CrashStore：写 {0} 失败：{1}", path, ex.Message);
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    FrameworkLog.Warning("CrashStore：写 {0} 失败：{1}", path, ex.Message);
                    return false;
                }
            }
        }
    }
}
