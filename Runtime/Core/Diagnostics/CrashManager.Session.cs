//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Diagnostics
{
    /// <summary>CrashManager：上次会话异常退出检测与会话标记写入。</summary>
    internal sealed partial class CrashManager
    {
        // ================================================================
        //  异常退出检测 / 会话标记
        // ================================================================

        private bool DetectPreviousAbnormalExit(string sessionId)
        {
            SessionMarker marker;
            MarkerReadResult result = m_Store.TryReadMarker(out marker);
            if (result == MarkerReadResult.None) return false;
            if (result == MarkerReadResult.Ok && marker.SessionId == sessionId) return false;

            if (result == MarkerReadResult.Ok && (marker.Flags & SessionMarkerFlags.FatalReported) != 0)
            {
                return true;   // 致命异常已单独落盘，不再重复生成异常退出报告
            }

            CrashKind kind;
            if (result == MarkerReadResult.Corrupt) kind = CrashKind.AbnormalExit;
            else if ((marker.Flags & SessionMarkerFlags.Hanging) != 0) kind = CrashKind.HangKill;
            else if ((marker.Flags & SessionMarkerFlags.Background) != 0) kind = CrashKind.BackgroundKill;
            else if ((marker.Flags & SessionMarkerFlags.LowMemory) != 0) kind = CrashKind.LowMemoryKill;
            else kind = CrashKind.AbnormalExit;

            CrashReport report = new CrashReport();
            lock (m_Lock) { report.ReportId = NowUnixMillis().ToString("D13") + "-" + ShortSession(sessionId) + "-" + (++m_ReportSeq).ToString("D3"); }
            report.Kind = kind;
            report.SessionId = marker.SessionId ?? string.Empty;
            report.AppVersion = marker.AppVersion;
            report.DeviceInfo = marker.DeviceInfo;
            report.UnixMillis = marker.WrittenUnixMillis;
            report.SessionSeconds = marker.SessionSeconds;
            report.Phase = marker.Phase;
            report.Message = result == MarkerReadResult.Corrupt
                ? "Previous session did not shut down cleanly (session marker unreadable)"
                : "Previous session did not shut down cleanly";
            report.Fingerprint = CrashFingerprint.ComputeMessage(kind + "|" + (marker.Phase ?? string.Empty));
            report.Breadcrumbs = marker.Breadcrumbs ?? Array.Empty<string>();
            Persist(report);
            lock (m_Lock) { m_NewReports.Add(report); }
            return true;
        }

        private void WriteMarkerNow()
        {
            if (m_Store == null) return;
            SessionMarker marker;
            double now = Now();
            lock (m_Lock)
            {
                if (!m_InSession) return;
                marker.SessionId = m_SessionId;
                marker.AppVersion = m_AppVersion;
                marker.DeviceInfo = m_DeviceInfo;
                marker.WrittenUnixMillis = NowUnixMillis();
                marker.SessionSeconds = (float)(now - m_SessionStart);
                marker.Phase = m_Phase;
                marker.Flags = m_Flags;
                marker.Breadcrumbs = SnapshotCrumbs(m_SessionStart);
                m_MarkerDirty = false;
                m_LastMarkerWrite = now;
            }

            m_Store.WriteMarker(ref marker);
        }
    }
}
