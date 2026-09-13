//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Download
{
    /// <summary>
    /// 下载代理辅助器接口。每个并发下载槽对应一个本接口实例，由 Unity 层实现（封装 <c>UnityWebRequest</c> + <c>DownloadHandlerFile</c>），
    /// 负责真正的单个文件传输。
    ///
    /// 约定：
    ///   - <see cref="Download"/> 不阻塞调用线程；底层异步推进，由 <see cref="Update"/> 在<b>主线程</b>驱动轮询。
    ///   - 进度 / 完成 / 失败均通过本接口的 C# 事件回调，回调发生在 <see cref="Update"/> 内（即主线程）。
    ///   - <see cref="DownloadAgentHelperUpdate"/> 回传的是「自上次回调以来新增的字节数」（增量），而非累计值。
    ///   - 支持断点续传：<see cref="Download"/> 的 <c>fromPosition</c> 指定从文件已有偏移处续传（设置 Range 头 + 追加写）。
    ///   - 实现方负责在 <see cref="Reset"/> 中中止并释放底层请求资源；可保留半文件供同一任务续传。
    ///   - <see cref="Download"/> 的 <c>fromPosition == 0</c> 必须截断/覆盖已有目标文件，只有
    ///     <c>fromPosition &gt; 0</c> 才允许追加，避免取消后的半文件污染后续新任务。
    /// </summary>
    public interface IDownloadAgentHelper
    {
        /// <summary>
        /// 进度更新事件。第二参数为「自上次回调以来新增的字节数」（增量，非累计）。在主线程回调。
        /// </summary>
        event Action<IDownloadAgentHelper, long> DownloadAgentHelperUpdate;

        /// <summary>
        /// 下载完成事件。第二参数为本次传输累计写入的总字节数（不含续传起点之前的部分）。在主线程回调。
        /// </summary>
        event Action<IDownloadAgentHelper, long> DownloadAgentHelperComplete;

        /// <summary>
        /// 下载错误事件。第二参数为错误描述。在主线程回调。
        /// </summary>
        event Action<IDownloadAgentHelper, string> DownloadAgentHelperError;

        /// <summary>
        /// 开始下载。
        /// </summary>
        /// <param name="downloadUri">下载地址。</param>
        /// <param name="fromPosition">续传起始偏移（字节）。0 表示截断已有文件并从头下载；&gt;0 表示从已有文件末尾续传并追加。</param>
        /// <param name="savePath">本地保存路径。</param>
        void Download(string downloadUri, long fromPosition, string savePath);

        /// <summary>
        /// 轮询推进在途传输（由管理器在主线程逐帧调用）：计算增量并触发进度 / 完成 / 失败事件。
        /// </summary>
        void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 重置：中止在途请求、释放底层资源、清空内部状态，使该辅助器可被复用于下一个任务。
        /// 可保留已下载的半文件；下一次 Download 必须依据 fromPosition 决定覆盖还是追加。
        /// </summary>
        void Reset();
    }
}
