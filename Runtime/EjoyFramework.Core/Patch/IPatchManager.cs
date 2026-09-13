//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Core.Patch
{
    /// <summary>
    /// 资源热更管理器：拉远程 manifest → 计算差量 → 下载 → 校验 → 应用。
    ///
    /// 流程：
    /// <code>
    /// patch.SetDownloadHelper(unityDownloadHelper);
    /// patch.SetReadWritePath(Application.persistentDataPath + "/patches");
    /// patch.CheckUpdateAsync(localManifest, remoteManifestUrl,
    ///     onResult: result => {
    ///       if (result.HasUpdate) patch.DownloadAsync(result.PendingBundles, onProgress, onComplete, onError);
    ///     });
    /// </code>
    /// </summary>
    public interface IPatchManager
    {
        /// <summary>注入下载 helper（Unity 层 UnityWebRequest 实现）。</summary>
        void SetDownloadHelper(IDownloadHelper helper);

        /// <summary>写入路径（patch 落地位置）。通常 Application.persistentDataPath。</summary>
        void SetReadWritePath(string path);

        /// <summary>
        /// 同步比较 local 与 remote manifest，返回差量。
        /// remote manifest 由 Unity 层下载并 JsonUtility 解析后传入。
        /// </summary>
        PatchCheckResult CheckUpdate(AssetManifest local, AssetManifest remote);

        /// <summary>
        /// 下载差量 bundles。
        /// </summary>
        void DownloadAsync(string baseUrl, PatchBundleInfo[] bundles,
            Action<PatchProgress> onProgress,
            Action onComplete,
            Action<string> onError);

        /// <summary>当前进度（业务侧 polling 备用；推荐用 onProgress 回调）。</summary>
        PatchProgress CurrentProgress { get; }
    }

    /// <summary>下载后端 helper（Unity 层 UnityWebRequest / native HTTP 实现）。</summary>
    public interface IDownloadHelper
    {
        /// <summary>
        /// 下载 url 到 filePath。
        /// 回调：成功 → onComplete(bytesDownloaded); 失败 → onError(reason);
        /// 业务期间可触发 onProgress(0..1)。
        /// </summary>
        void DownloadAsync(string url, string filePath,
            Action<float> onProgress,
            Action<long> onComplete,
            Action<string> onError);
    }

    /// <summary>CheckUpdate 结果。</summary>
    public sealed class PatchCheckResult
    {
        public bool HasUpdate;
        public int RemoteVersion;
        public int LocalVersion;
        public PatchBundleInfo[] PendingBundles = new PatchBundleInfo[0];
        public long TotalSizeBytes;
    }

    /// <summary>差量 bundle 信息。</summary>
    public sealed class PatchBundleInfo
    {
        public string BundleName;
        public string RelativePath;
        public string Md5;
        public long SizeBytes;
    }

    /// <summary>下载进度快照。</summary>
    public struct PatchProgress
    {
        public int CompletedBundleCount;
        public int TotalBundleCount;
        public long DownloadedBytes;
        public long TotalBytes;
        public float CurrentBundleProgress;
        public string CurrentBundleName;
    }
}
