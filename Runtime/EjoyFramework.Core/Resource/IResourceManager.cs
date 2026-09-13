//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源管理器接口。
    /// 业务层始终通过此接口访问资源；Unity 层通过 SetLoader 注入具体实现（EditorSimulation / AssetBundle / Resources）。
    /// </summary>
    public interface IResourceManager
    {
        /// <summary>
        /// 获取资源只读路径（StreamingAssets，运行时只读）。
        /// </summary>
        string ReadOnlyPath { get; }

        /// <summary>
        /// 获取资源读写路径（persistentDataPath，热更后的资源落地处）。
        /// </summary>
        string ReadWritePath { get; }

        /// <summary>
        /// 当前加载模式。
        /// </summary>
        ResourceMode Mode { get; }

        /// <summary>
        /// Loader 是否已初始化（Manifest 加载完成且 Loader.IsInitialized）。
        /// </summary>
        bool IsInitialized { get; }

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
        /// 设置资源只读路径。
        /// </summary>
        void SetReadOnlyPath(string readOnlyPath);

        /// <summary>
        /// 设置资源读写路径。
        /// </summary>
        void SetReadWritePath(string readWritePath);

        /// <summary>
        /// 设置当前变体。
        /// </summary>
        void SetCurrentVariant(string currentVariant);

        /// <summary>
        /// 设置加载模式（必须在 Initialize 之前调用）。
        /// </summary>
        void SetMode(ResourceMode mode);

        /// <summary>
        /// 注入具体 Loader 实现。
        /// </summary>
        void SetLoader(IResourceLoader loader);

        /// <summary>
        /// 异步初始化：加载 Manifest（AssetBundle 模式）+ 让 Loader 完成自身初始化。
        /// EditorSimulation 模式可传 null manifest，由 Loader 在内部构造。
        /// </summary>
        void InitializeAsync(AssetManifest manifest,
            Action onComplete,
            Action<string> onFailure);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        void LoadAsset(string assetName, LoadAssetCallbacks loadAssetCallbacks);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        void LoadAsset(string assetName, int priority, LoadAssetCallbacks loadAssetCallbacks);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        void LoadAsset(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks);

        /// <summary>
        /// 异步加载资源。
        /// </summary>
        void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks);

        /// <summary>
        /// 异步加载资源（带 userData）。
        /// </summary>
        void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData);

        /// <summary>
        /// 异步加载资源，返回 handle。调用方可读取 Status/Progress/Asset/Cancel/Completed。
        /// 旧的 LoadAsset(name, callbacks) 是其薄封装，内部走同一条路。
        /// </summary>
        IAssetLoadHandle LoadAssetWithHandle(string assetName, int priority = 0, object userData = null);

        /// <summary>
        /// 异步加载资源（指定类型），返回 handle。
        /// </summary>
        IAssetLoadHandle LoadAssetWithHandle(string assetName, Type assetType, int priority = 0, object userData = null);

        /// <summary>
        /// 当前所有在途加载句柄的快照（调试用，请勿修改）。
        /// </summary>
        IAssetLoadHandle[] GetAllLoadingHandles();

        /// <summary>
        /// 异步加载场景资源（供 SceneManager 调用，业务方一般通过 SceneComponent.LoadScene 间接调用）。
        /// </summary>
        void LoadScene(string sceneAssetName, int priority,
            Action<float> onProgress,
            Action<string> onSuccess,
            Action<string, string> onFailure,
            object userData);

        /// <summary>
        /// 异步卸载场景资源。
        /// </summary>
        void UnloadScene(string sceneAssetName,
            Action<string> onSuccess,
            Action<string, string> onFailure,
            object userData);

        /// <summary>
        /// 卸载资源（递减引用计数）。
        /// </summary>
        void UnloadAsset(object asset);

        /// <summary>
        /// 卸载未被引用的资源。
        /// </summary>
        void UnloadUnusedAssets(bool performGCCollect);

        /// <summary>
        /// 检查资源是否存在。
        /// </summary>
        bool HasAsset(string assetName);

        // ===== 批量预载 + 同步获取 =====

        /// <summary>
        /// 当前被批次 Pin 住的已预载资产数量（统计/调试用）。
        /// </summary>
        int CachedAssetCount { get; }

        /// <summary>
        /// 从引用池租用一个资源批次。用法：CreateAssetBatch().Add(a).Add(b).Start()。
        /// 批次用完必须调用 Release() 归还，否则预载资产不会被卸载。
        /// Shutdown 之后调用会抛 FrameworkException。
        /// </summary>
        AssetBatch CreateAssetBatch();

        /// <summary>
        /// 同步取已预载（被某个 AssetBatch Pin 住）的资产。未预载或类型不符返回 false。
        /// </summary>
        bool TryGetCachedAsset<T>(string assetName, out T asset) where T : class;

        /// <summary>
        /// 同步取已预载资产。未预载或类型不符抛 FrameworkException。
        /// 仅用于调用方能确信资产已由批次预载完成的路径。
        /// </summary>
        T GetCachedAsset<T>(string assetName) where T : class;
    }

    /// <summary>
    /// 加载资源成功回调函数。
    /// </summary>
    public delegate void LoadAssetSuccessCallback(string assetName, object asset, float duration, object userData);

    /// <summary>
    /// 加载资源失败回调函数。
    /// </summary>
    public delegate void LoadAssetFailureCallback(string assetName, LoadResourceStatus status, string errorMessage, object userData);

    /// <summary>
    /// 加载资源更新回调函数。
    /// </summary>
    public delegate void LoadAssetUpdateCallback(string assetName, float progress, object userData);

    /// <summary>
    /// 加载资源加载依赖资源回调函数。
    /// </summary>
    public delegate void LoadAssetDependencyAssetCallback(string assetName, string dependencyAssetName, int loadedCount, int totalCount, object userData);

    /// <summary>
    /// 加载资源回调函数集。
    /// </summary>
    public sealed class LoadAssetCallbacks
    {
        private readonly LoadAssetSuccessCallback m_LoadAssetSuccessCallback;
        private readonly LoadAssetFailureCallback m_LoadAssetFailureCallback;
        private readonly LoadAssetUpdateCallback m_LoadAssetUpdateCallback;
        private readonly LoadAssetDependencyAssetCallback m_LoadAssetDependencyAssetCallback;

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback)
            : this(loadAssetSuccessCallback, null, null, null)
        {
        }

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback)
            : this(loadAssetSuccessCallback, loadAssetFailureCallback, null, null)
        {
        }

        public LoadAssetCallbacks(LoadAssetSuccessCallback loadAssetSuccessCallback, LoadAssetFailureCallback loadAssetFailureCallback, LoadAssetUpdateCallback loadAssetUpdateCallback, LoadAssetDependencyAssetCallback loadAssetDependencyAssetCallback)
        {
            if (loadAssetSuccessCallback == null)
            {
                throw new FrameworkException("Load asset success callback is invalid.");
            }

            m_LoadAssetSuccessCallback = loadAssetSuccessCallback;
            m_LoadAssetFailureCallback = loadAssetFailureCallback;
            m_LoadAssetUpdateCallback = loadAssetUpdateCallback;
            m_LoadAssetDependencyAssetCallback = loadAssetDependencyAssetCallback;
        }

        public LoadAssetSuccessCallback LoadAssetSuccessCallback { get { return m_LoadAssetSuccessCallback; } }
        public LoadAssetFailureCallback LoadAssetFailureCallback { get { return m_LoadAssetFailureCallback; } }
        public LoadAssetUpdateCallback LoadAssetUpdateCallback { get { return m_LoadAssetUpdateCallback; } }
        public LoadAssetDependencyAssetCallback LoadAssetDependencyAssetCallback { get { return m_LoadAssetDependencyAssetCallback; } }
    }

    /// <summary>
    /// 加载资源状态。
    /// </summary>
    public enum LoadResourceStatus : byte
    {
        Success = 0,
        NotExist,
        NotReady,
        AssetError,
        DependencyError,
        TypeError,
    }
}
