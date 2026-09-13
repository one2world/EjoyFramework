//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Download;
using UnityEngine.Networking;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 <see cref="UnityWebRequest"/> + <see cref="DownloadHandlerFile"/> 的 <see cref="IDownloadAgentHelper"/> 实现。
    ///
    /// 不自己起协程：由 <see cref="DownloadManager"/> 在主线程逐帧调 <see cref="Update"/> 轮询在途请求，
    /// 因此所有事件回调都发生在主线程。续传通过 <c>DownloadHandlerFile.append = true</c> 配合
    /// <c>Range: bytes=&lt;fromPosition&gt;-</c> 请求头实现。
    ///
    /// 命名空间提示：本文件位于 EjoyFramework.Core.Unity，与 core 的 EjoyFramework.Core.Download 平级；
    /// UnityEngine 不存在名为 Download 的类型，无遮蔽风险。UnityWebRequest 等类型通过显式 using 引入。
    /// </summary>
    public sealed class UnityWebRequestDownloadAgentHelper : IDownloadAgentHelper
    {
        private UnityWebRequest m_UnityWebRequest;
        private UnityWebRequestAsyncOperation m_Operation;
        private bool m_Disposed;
        // 上一次回调时已下载的字节数，用于计算增量。
        private ulong m_LastReportedBytes;

        /// <inheritdoc />
        public event Action<IDownloadAgentHelper, long> DownloadAgentHelperUpdate;

        /// <inheritdoc />
        public event Action<IDownloadAgentHelper, long> DownloadAgentHelperComplete;

        /// <inheritdoc />
        public event Action<IDownloadAgentHelper, string> DownloadAgentHelperError;

        /// <inheritdoc />
        public void Download(string downloadUri, long fromPosition, string savePath)
        {
            Reset();

            try
            {
                m_UnityWebRequest = new UnityWebRequest(downloadUri, UnityWebRequest.kHttpVerbGET);
                // 新任务必须截断可能残留的半文件；仅续传请求才追加。
                DownloadHandlerFile handler = new DownloadHandlerFile(
                    savePath,
                    ShouldAppendToExistingFile(fromPosition))
                {
                    removeFileOnAbort = false,
                };
                m_UnityWebRequest.downloadHandler = handler;

                if (fromPosition > 0L)
                {
                    // 请求服务端从 fromPosition 处继续返回内容（区间请求）。
                    m_UnityWebRequest.SetRequestHeader("Range", "bytes=" + fromPosition + "-");
                }

                m_Operation = m_UnityWebRequest.SendWebRequest();
                m_LastReportedBytes = 0UL;
                m_Disposed = false;
            }
            catch (Exception ex)
            {
                DisposeRequest();
                DownloadAgentHelperError?.Invoke(this, "Failed to start UnityWebRequest download: " + ex.Message);
            }
        }

        /// <inheritdoc />
        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_UnityWebRequest == null || m_Operation == null)
            {
                return;
            }

            // 1) 报告增量进度。
            ulong now = m_UnityWebRequest.downloadedBytes;
            if (now > m_LastReportedBytes)
            {
                long delta = (long)(now - m_LastReportedBytes);
                m_LastReportedBytes = now;
                DownloadAgentHelperUpdate?.Invoke(this, delta);
            }

            // 2) 完成判定。
            if (!m_Operation.isDone)
            {
                return;
            }

            UnityWebRequest.Result result = m_UnityWebRequest.result;
            if (result == UnityWebRequest.Result.Success)
            {
                long total = (long)m_UnityWebRequest.downloadedBytes;
                DisposeRequest();
                DownloadAgentHelperComplete?.Invoke(this, total);
            }
            else
            {
                string error = m_UnityWebRequest.error ?? ("UnityWebRequest result: " + result);
                DisposeRequest();
                DownloadAgentHelperError?.Invoke(this, error);
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            DisposeRequest();
            m_LastReportedBytes = 0UL;
        }

        private static bool ShouldAppendToExistingFile(long fromPosition)
        {
            return fromPosition > 0L;
        }

        // 中止并释放底层请求资源；幂等。
        private void DisposeRequest()
        {
            if (m_UnityWebRequest != null && !m_Disposed)
            {
                try
                {
                    m_UnityWebRequest.Abort();
                }
                catch (Exception ex)
                {
                    FrameworkLog.Warning("UnityWebRequest.Abort threw: {0}", ex.Message);
                }

                try
                {
                    m_UnityWebRequest.Dispose();
                }
                catch (Exception ex)
                {
                    FrameworkLog.Warning("UnityWebRequest.Dispose threw: {0}", ex.Message);
                }
            }

            m_UnityWebRequest = null;
            m_Operation = null;
            m_Disposed = true;
        }
    }
}
