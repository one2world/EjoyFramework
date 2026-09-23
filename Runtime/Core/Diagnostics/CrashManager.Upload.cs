//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>CrashManager：盘上报告的逐个上传（单个在途、跨线程完成回报、失败重排队）。</summary>
    internal sealed partial class CrashManager
    {
        // ================================================================
        //  上传
        // ================================================================

        public int PendingUploadCount { get { return m_PendingUploads.Count + (m_UploadInFlight != null ? 1 : 0); } }

        public int UploadedCount { get { return m_Uploaded; } }

        private void RescanPendingUploads()
        {
            m_Store.ListReports(m_PendingUploads);
            if (m_UploadInFlightPath != null) m_PendingUploads.Remove(m_UploadInFlightPath);
        }

        private void TryUpload()
        {
            if (m_Uploader == null || m_Store == null || m_UploadInFlight != null) return;
            if (m_UploadFailures >= MaxUploadFailuresPerSession) return;

            while (m_PendingUploads.Count > 0)
            {
                string path = m_PendingUploads[0];
                m_PendingUploads.RemoveAt(0);
                byte[] bytes = m_Store.Read(path);
                if (bytes == null) continue;   // 已被删除 / 读失败
                CrashReport report;
                if (!CrashReport.TryParse(bytes, out report))
                {
                    FrameworkLog.Warning("CrashManager：丢弃损坏的报告文件 {0}", path);
                    m_Store.Delete(path);
                    continue;
                }

                report.StoragePath = path;
                m_UploadInFlight = report;
                m_UploadInFlightPath = path;
                try
                {
                    m_Uploader.Upload(report, bytes, m_OnUploadComplete);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("CrashManager：上传器抛出异常：{0}", ex);
                    OnUploadComplete(report, false);
                }

                return;
            }
        }

        private void OnUploadComplete(CrashReport report, bool success)
        {
            lock (m_Lock)
            {
                UploadCompletion c;
                c.Report = report;
                c.Success = success;
                m_UploadCompletions.Add(c);
            }
        }

        private void ProcessUploadCompletions()
        {
            UploadCompletion completion;
            lock (m_Lock)
            {
                if (m_UploadCompletions.Count == 0) return;
                completion = m_UploadCompletions[0];
                m_UploadCompletions.RemoveAt(0);
            }

            if (completion.Report != m_UploadInFlight) return;
            string path = m_UploadInFlightPath;
            m_UploadInFlight = null;
            m_UploadInFlightPath = null;
            if (completion.Success)
            {
                m_Uploaded++;
                m_UploadFailures = 0;
                m_Store.Delete(path);
            }
            else
            {
                m_UploadFailures++;
                m_PendingUploads.Add(path);   // 排到队尾，本会话连续失败达上限后停止，下个会话重试
            }
        }
    }
}
