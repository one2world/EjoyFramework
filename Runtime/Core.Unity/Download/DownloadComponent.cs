//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Download;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 下载组件。
    /// Awake 解析 <see cref="IDownloadManager"/>，按序列化的 <see cref="m_AgentCount"/> 创建并注入相同数量的
    /// <see cref="UnityWebRequestDownloadAgentHelper"/>（每个对应一个并发槽），随后把 AddDownload / RemoveDownload
    /// 以及 4 个下载事件转发 / 暴露给业务。
    ///
    /// 设计：不提供 GameEntry 静态访问器；业务通过 GetComponent 或自有装配获取本组件（与 HttpComponent 一致）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Download")]
    public sealed class DownloadComponent : GameFrameworkComponent
    {
        [Tooltip("并发下载代理数量（每个代理一个并发槽）。")]
        [SerializeField]
        private int m_AgentCount = 3;

        [Tooltip("单任务最大重试次数（失败后续传重试）。")]
        [SerializeField]
        private int m_RetryCount = 2;

        private IDownloadManager m_DownloadManager;

        protected override void Awake()
        {
            base.Awake();
            m_DownloadManager = Framework.GetModule<IDownloadManager>();
            if (m_DownloadManager == null)
            {
                Log.Fatal("Download manager is invalid.");
                return;
            }

            int count = m_AgentCount < 1 ? 1 : m_AgentCount;
            m_DownloadManager.MaxConcurrentDownloads = count;
            m_DownloadManager.RetryCount = m_RetryCount;

            for (int i = 0; i < count; i++)
            {
                m_DownloadManager.AddAgentHelper(new UnityWebRequestDownloadAgentHelper());
            }
        }

        /// <summary>已注入的代理总数。</summary>
        public int TotalAgentCount
        {
            get { return m_DownloadManager != null ? m_DownloadManager.TotalAgentCount : 0; }
        }

        /// <summary>当前空闲代理数。</summary>
        public int FreeAgentCount
        {
            get { return m_DownloadManager != null ? m_DownloadManager.FreeAgentCount : 0; }
        }

        /// <summary>当前正在下载的代理数。</summary>
        public int WorkingAgentCount
        {
            get { return m_DownloadManager != null ? m_DownloadManager.WorkingAgentCount : 0; }
        }

        /// <summary>当前排队等待的任务数。</summary>
        public int WaitingTaskCount
        {
            get { return m_DownloadManager != null ? m_DownloadManager.WaitingTaskCount : 0; }
        }

        /// <summary>下载开始事件。</summary>
        public event Action<DownloadStartEventArgs> DownloadStart
        {
            add { if (m_DownloadManager != null) m_DownloadManager.DownloadStart += value; }
            remove { if (m_DownloadManager != null) m_DownloadManager.DownloadStart -= value; }
        }

        /// <summary>下载进度更新事件。</summary>
        public event Action<DownloadUpdateEventArgs> DownloadUpdate
        {
            add { if (m_DownloadManager != null) m_DownloadManager.DownloadUpdate += value; }
            remove { if (m_DownloadManager != null) m_DownloadManager.DownloadUpdate -= value; }
        }

        /// <summary>下载成功事件。</summary>
        public event Action<DownloadSuccessEventArgs> DownloadSuccess
        {
            add { if (m_DownloadManager != null) m_DownloadManager.DownloadSuccess += value; }
            remove { if (m_DownloadManager != null) m_DownloadManager.DownloadSuccess -= value; }
        }

        /// <summary>下载失败事件（重试耗尽后触发）。</summary>
        public event Action<DownloadFailureEventArgs> DownloadFailure
        {
            add { if (m_DownloadManager != null) m_DownloadManager.DownloadFailure += value; }
            remove { if (m_DownloadManager != null) m_DownloadManager.DownloadFailure -= value; }
        }

        /// <summary>
        /// 新增一个下载任务，返回序列编号。
        /// </summary>
        public int AddDownload(string downloadUri, string savePath, object userData = null)
        {
            if (m_DownloadManager == null) { Log.Error("Download manager not ready."); return 0; }
            return m_DownloadManager.AddDownload(downloadUri, savePath, userData);
        }

        /// <summary>
        /// 按序列编号移除一个下载任务。
        /// </summary>
        public bool RemoveDownload(int serialId)
        {
            if (m_DownloadManager == null) { Log.Error("Download manager not ready."); return false; }
            return m_DownloadManager.RemoveDownload(serialId);
        }

        /// <summary>
        /// 移除全部下载任务。
        /// </summary>
        public void RemoveAllDownloads()
        {
            if (m_DownloadManager == null) { Log.Error("Download manager not ready."); return; }
            m_DownloadManager.RemoveAllDownloads();
        }
    }
}
