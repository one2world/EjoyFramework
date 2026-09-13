//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源加载器接口。由 Unity 层（或测试 mock）注入具体实现。
    /// ResourceManager 仅做：路径/变体配置、Loader 转发、Manifest 持有、HasAsset 查询。
    /// 所有 IO/异步/AssetDatabase/AssetBundle 调用都封装在 Loader 实现里，保持 Framework 层引擎无关。
    ///
    /// 实现要求：
    ///   - 所有方法可重入；同一 assetName 并发请求自行合并或排队
    ///   - 回调必须在主线程派发（Unity 层 Loader 用协程/UnityWebRequest 保证）
    ///   - 失败必须显式回调，绝不抛出穿透 Manager
    ///   - LoadScene 是单独通道：场景资源不能用 LoadAsset 拉取
    /// </summary>
    public interface IResourceLoader
    {
        /// <summary>
        /// 初始化 Loader（加载 manifest、准备目录、初始化内部状态）。
        /// 异步：完成后通过 onComplete 回调。
        /// </summary>
        void Initialize(string readOnlyPath, string readWritePath, string currentVariant,
            AssetManifest manifest,
            Action onComplete,
            Action<string> onFailure);

        /// <summary>
        /// 该 Loader 是否需要 AssetManifest 才能初始化。
        /// 由 Loader 自己声明，调用方据此决定是否要从磁盘加载 manifest，
        /// 避免上层组件二次判断 ResourceMode（避免 runtime 携带 Editor 特异性逻辑）。
        /// </summary>
        bool RequiresManifest { get; }

        /// <summary>
        /// 已初始化且可加载资源。
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 资源是否存在（基于 manifest）。
        /// </summary>
        bool HasAsset(string assetName);

        /// <summary>
        /// 异步加载资源。Loader 内部负责 bundle 依赖解析 / 并发合并 / 引用计数 / 主线程回调。
        /// </summary>
        void LoadAssetAsync(string assetName, Type assetType, int priority,
            LoadAssetCallbacks callbacks, object userData);

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        void LoadSceneAsync(string sceneAssetName, int priority,
            Action<float> onProgress,
            Action<string> onSuccess,
            Action<string, string> onFailure,
            object userData);

        /// <summary>
        /// 异步卸载场景。
        /// </summary>
        void UnloadSceneAsync(string sceneAssetName,
            Action<string> onSuccess,
            Action<string, string> onFailure,
            object userData);

        /// <summary>
        /// 显式卸载已加载资源（递减引用计数；归零时真正释放）。
        /// null 视为整体清理信号。
        /// </summary>
        void UnloadAsset(object asset);

        /// <summary>
        /// 卸载所有引用计数为 0 的 bundle/asset。
        /// </summary>
        void UnloadUnusedAssets(bool performGCCollect);

        /// <summary>
        /// 当前持有的 bundle 数量（统计/调试用）。
        /// </summary>
        int LoadedBundleCount { get; }

        /// <summary>
        /// 当前持有的 asset 数量（统计/调试用）。
        /// </summary>
        int LoadedAssetCount { get; }

        /// <summary>
        /// 当前在途的加载任务数。
        /// </summary>
        int LoadingTaskCount { get; }

        /// <summary>
        /// 关闭 Loader，释放所有资源。
        /// </summary>
        void Shutdown();
    }
}
