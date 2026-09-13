//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Download
{
    /// <summary>
    /// 下载开始事件参数。某个等待任务被分配到空闲代理、真正开始传输时触发。
    /// </summary>
    public sealed class DownloadStartEventArgs
    {
        /// <summary>下载任务序列编号。</summary>
        public int SerialId;

        /// <summary>下载地址。</summary>
        public string DownloadUri;

        /// <summary>本地保存路径。</summary>
        public string SavePath;

        /// <summary>业务自定义数据（由 <c>AddDownload</c> 透传）。</summary>
        public object UserData;
    }

    /// <summary>
    /// 下载进度更新事件参数。每次代理辅助器回传增量字节时触发。
    /// </summary>
    public sealed class DownloadUpdateEventArgs
    {
        /// <summary>下载任务序列编号。</summary>
        public int SerialId;

        /// <summary>下载地址。</summary>
        public string DownloadUri;

        /// <summary>本地保存路径。</summary>
        public string SavePath;

        /// <summary>业务自定义数据。</summary>
        public object UserData;

        /// <summary>当前已累计下载的字节数（含续传起点之前的部分）。</summary>
        public long CurrentLength;
    }

    /// <summary>
    /// 下载成功事件参数。
    /// </summary>
    public sealed class DownloadSuccessEventArgs
    {
        /// <summary>下载任务序列编号。</summary>
        public int SerialId;

        /// <summary>下载地址。</summary>
        public string DownloadUri;

        /// <summary>本地保存路径。</summary>
        public string SavePath;

        /// <summary>业务自定义数据。</summary>
        public object UserData;
    }

    /// <summary>
    /// 下载失败事件参数。仅在重试耗尽后触发。
    /// </summary>
    public sealed class DownloadFailureEventArgs
    {
        /// <summary>下载任务序列编号。</summary>
        public int SerialId;

        /// <summary>下载地址。</summary>
        public string DownloadUri;

        /// <summary>本地保存路径。</summary>
        public string SavePath;

        /// <summary>业务自定义数据。</summary>
        public object UserData;

        /// <summary>错误描述。</summary>
        public string ErrorMessage;
    }
}
