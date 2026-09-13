//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Download
{
    /// <summary>
    /// 下载管理器接口。
    /// 负责：维护一组下载代理（每个代理封装一个 <see cref="IDownloadAgentHelper"/>）；把排队的下载任务按
    /// <see cref="MaxConcurrentDownloads"/> 并发上限分配给空闲代理；在失败时按 <see cref="RetryCount"/> 自动续传重试。
    ///
    /// 业务层一般通过 Unity 层的 DownloadComponent 间接使用本接口。
    /// </summary>
    public interface IDownloadManager
    {
        /// <summary>
        /// 最大并发下载数（默认 3）。超过该上限的任务保持排队，待有代理空闲后再分配。
        /// 注意：实际并发还受已注入代理总数（<see cref="TotalAgentCount"/>）约束，取两者较小值。
        /// </summary>
        int MaxConcurrentDownloads { get; set; }

        /// <summary>
        /// 单个任务的最大重试次数（默认 2）。失败后续传重试，重试耗尽才触发 <see cref="DownloadFailure"/>。
        /// </summary>
        int RetryCount { get; set; }

        /// <summary>
        /// 注入一个下载代理辅助器（每个并发槽调用一次，通常由 Unity 组件在 Awake 中注入）。
        /// </summary>
        void AddAgentHelper(IDownloadAgentHelper helper);

        /// <summary>
        /// 新增一个下载任务（入队）；返回单调递增的序列编号用于后续移除 / 关联回调。
        /// 入队后会立即尝试把它（及其它等待任务）分配给空闲代理。
        /// </summary>
        int AddDownload(string downloadUri, string savePath, object userData = null);

        /// <summary>
        /// 按序列编号移除一个下载任务：等待中的直接出队；正在下载的中止其代理并释放该槽，随后拉起下一个等待任务。
        /// 移除成功返回 true，未找到返回 false。
        /// </summary>
        bool RemoveDownload(int serialId);

        /// <summary>
        /// 移除全部下载任务：清空等待队列并中止所有在途传输。
        /// </summary>
        void RemoveAllDownloads();

        /// <summary>已注入的代理总数。</summary>
        int TotalAgentCount { get; }

        /// <summary>当前空闲（可接新任务）的代理数。</summary>
        int FreeAgentCount { get; }

        /// <summary>当前正在下载的代理数。</summary>
        int WorkingAgentCount { get; }

        /// <summary>当前排队等待的任务数。</summary>
        int WaitingTaskCount { get; }

        /// <summary>下载开始事件。</summary>
        event Action<DownloadStartEventArgs> DownloadStart;

        /// <summary>下载进度更新事件。</summary>
        event Action<DownloadUpdateEventArgs> DownloadUpdate;

        /// <summary>下载成功事件。</summary>
        event Action<DownloadSuccessEventArgs> DownloadSuccess;

        /// <summary>下载失败事件（重试耗尽后触发）。</summary>
        event Action<DownloadFailureEventArgs> DownloadFailure;
    }
}
