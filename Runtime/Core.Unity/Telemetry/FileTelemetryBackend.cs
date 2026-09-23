//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using EjoyFramework.Core.Telemetry;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 文件后端：把批次落到 <c>persistentDataPath/Telemetry/&lt;sessionId&gt;-&lt;batchId&gt;.ejtm</c>，供出包前的本机分析或
    /// 由业务的上传管线（Http 模块 / 第三方 SDK）稍后扫描目录上传。写盘在线程池，完成回调来自线程池线程
    /// （TelemetryManager 会转回主线程处理）。目录内文件数超过 <see cref="MaxFiles"/> 时丢最旧。
    /// </summary>
    public sealed class FileTelemetryBackend : ITelemetryBackend
    {
        private readonly string m_Directory;
        private int m_MaxFiles = 200;

        public FileTelemetryBackend(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new FrameworkException("FileTelemetryBackend：directory 不能为空。");
            m_Directory = directory;
        }

        /// <summary>目录内最多保留的批次文件数。</summary>
        public int MaxFiles
        {
            get { return m_MaxFiles; }
            set { m_MaxFiles = value < 1 ? 1 : value; }
        }

        public string Directory
        {
            get { return m_Directory; }
        }

        public void Send(TelemetryBatch batch, Action<TelemetryBatch, bool> onComplete)
        {
            // Payload 在回调前不会被归还，线程池里可以安全读取
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    System.IO.Directory.CreateDirectory(m_Directory);
                    string path = Path.Combine(m_Directory, batch.SessionId + "-" + batch.BatchId.ToString("D6") + ".ejtm");
                    using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        fs.Write(batch.Payload, 0, batch.Length);
                    }

                    TrimDirectory();
                    ok = true;
                }
                catch (Exception ex)
                {
                    FrameworkLog.Warning("FileTelemetryBackend：写批次失败：{0}", ex.Message);
                }

                onComplete(batch, ok);
            });
        }

        private void TrimDirectory()
        {
            string[] files = System.IO.Directory.GetFiles(m_Directory, "*.ejtm");
            if (files.Length <= m_MaxFiles) return;
            Array.Sort(files, (a, b) => File.GetCreationTimeUtc(a).CompareTo(File.GetCreationTimeUtc(b)));   // 最旧在前
            for (int i = 0; i < files.Length - m_MaxFiles; i++)
            {
                try { File.Delete(files[i]); } catch (Exception) { }
            }
        }
    }
}
