//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源管理器。
    /// 持有 IResourceLoader（由 Unity 层注入），转发所有 IO 调用；自身仅维护配置状态（路径/变体/模式/manifest 引用）。
    /// 同时维护 IAssetLoadHandle 注册表，让 LoadAsset 真正变成可观测、可取消的操作。
    /// </summary>
    internal sealed class ResourceManager : FrameworkModule, IResourceManager
    {
        private string m_ReadOnlyPath;
        private string m_ReadWritePath;
        private string m_CurrentVariant;
        private ResourceMode m_Mode;
        private IResourceLoader m_Loader;
        private AssetManifest m_Manifest;
        private bool m_LoaderInitialized;
        private int m_NextHandleId;
        private readonly Dictionary<int, AssetLoadHandle> m_LoadingHandles = new Dictionary<int, AssetLoadHandle>();
        private readonly AssetCache m_AssetCache = new AssetCache();
        private bool m_ShutDown;

        // ---- 请求调度（WS2-M1）----
        // 所有加载（旧回调式 + 句柄式）统一经 LoadRequest 进入：容量未满时同步派发（与旧行为一致），
        // 满了才入优先级堆；完成一个就从堆顶补一个。取消排队中的请求不产生任何 IO。
        private readonly BinaryHeap<LoadRequest> m_Queue = new BinaryHeap<LoadRequest>(LoadRequestComparer.Instance, 32);
        private readonly LoadAssetCallbacks m_RequestCallbacks;
        private int m_MaxConcurrentRequests = 16;
        private int m_InFlightRequests;
        private long m_NextSequence;
        private bool m_Pumping;

        public ResourceManager()
        {
            m_Mode = ResourceMode.Unspecified;
            // 一份共享回调 + userData 携带 LoadRequest：每次加载不再为回调分配闭包。
            m_RequestCallbacks = new LoadAssetCallbacks(OnRequestSuccess, OnRequestFailure, OnRequestProgress, null);
        }

        /// <summary>同时派发给 loader 的请求上限；&lt;= 0 表示不限。默认 16。调小不会撤回已派发的请求。</summary>
        public int MaxConcurrentRequests
        {
            get { return m_MaxConcurrentRequests; }
            set
            {
                m_MaxConcurrentRequests = value;
                PumpQueue();
            }
        }

        /// <summary>排队等待派发的请求数。</summary>
        public int QueuedRequestCount { get { return m_Queue.Count; } }

        /// <summary>已派发给 loader、尚未完成的请求数。</summary>
        public int InFlightRequestCount { get { return m_InFlightRequests; } }

        // Priority 20：IO keystone；所有上层 Manager (Sound/Entity/Scene/Config/Loc/DataTable) 依赖。
        public override int Priority { get { return 20; } }

        // 必需配置自检：依赖外部注入的 IResourceLoader 才能执行任何 IO。
        public override bool RequiresConfiguration { get { return true; } }
        public override bool IsModuleConfigured { get { return m_Loader != null; } }
        public override string ConfigurationHint { get { return "Call SetLoader(...) before use."; } }

        public string ReadOnlyPath { get { return m_ReadOnlyPath; } }
        public string ReadWritePath { get { return m_ReadWritePath; } }
        public ResourceMode Mode { get { return m_Mode; } }
        public bool IsInitialized { get { return m_LoaderInitialized && m_Loader != null && m_Loader.IsInitialized; } }
        public int LoadedBundleCount { get { return m_Loader != null ? m_Loader.LoadedBundleCount : 0; } }
        public int LoadedAssetCount { get { return m_Loader != null ? m_Loader.LoadedAssetCount : 0; } }
        public int LoadingTaskCount { get { return m_Loader != null ? m_Loader.LoadingTaskCount : 0; } }

        public override void Update(float elapseSeconds, float realElapseSeconds) { }

        public override void Shutdown()
        {
            // 先把预载缓存里 Pin 住的资产按 Pin 计数交还 loader，再关闭 loader，顺序不能反。
            // ClearAll 同时会把缓存封存：此后迟到的批次回调不会再把资产 Pin 回一张永远不会再被清理的表。
            m_ShutDown = true;
            m_AssetCache.ClearAll(m_Loader);

            // 排队中的请求：不再派发。句柄 → Cancelled；回调式 → NotReady 失败通知。
            while (m_Queue.Count > 0)
            {
                LoadRequest request = m_Queue.Pop();
                request.Queued = false;
                if (!request.Cancelled)
                {
                    if (request.Handle != null)
                    {
                        m_LoadingHandles.Remove(request.Handle.Id);
                        request.Handle.Request = null;
                        request.Handle.Cancel();
                    }
                    else
                    {
                        var cb = request.Callbacks.LoadAssetFailureCallback;
                        if (cb != null) { try { cb(request.AssetName, LoadResourceStatus.NotReady, "Resource manager is shutting down.", request.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAsset failure callback threw: {0}", ex); } }
                    }
                }

                ReferencePool.Release(request);
            }
            m_InFlightRequests = 0;

            if (m_Loader != null)
            {
                try { m_Loader.Shutdown(); } catch (Exception ex) { FrameworkLog.Error("ResourceLoader.Shutdown threw: {0}", ex); }
                m_Loader = null;
            }
            m_LoaderInitialized = false;
            m_Manifest = null;
        }

        public void SetReadOnlyPath(string readOnlyPath)
        {
            Framework.EnsureMainThread(nameof(SetReadOnlyPath));
            if (string.IsNullOrEmpty(readOnlyPath)) throw new FrameworkException("Read-only path is invalid.");
            m_ReadOnlyPath = readOnlyPath;
        }

        public void SetReadWritePath(string readWritePath)
        {
            Framework.EnsureMainThread(nameof(SetReadWritePath));
            if (string.IsNullOrEmpty(readWritePath)) throw new FrameworkException("Read-write path is invalid.");
            m_ReadWritePath = readWritePath;
        }

        public void SetCurrentVariant(string currentVariant)
        {
            Framework.EnsureMainThread(nameof(SetCurrentVariant));
            m_CurrentVariant = currentVariant;
        }

        public void SetMode(ResourceMode mode)
        {
            Framework.EnsureMainThread(nameof(SetMode));
            if (m_LoaderInitialized) throw new FrameworkException("Cannot change resource mode after initialization.");
            m_Mode = mode;
        }

        public void SetLoader(IResourceLoader loader)
        {
            Framework.EnsureMainThread(nameof(SetLoader));
            if (loader == null) throw new FrameworkException("Resource loader is invalid.");
            if (m_LoaderInitialized) throw new FrameworkException("Cannot replace loader after initialization.");
            m_Loader = loader;
        }

        public void InitializeAsync(AssetManifest manifest, Action onComplete, Action<string> onFailure)
        {
            Framework.EnsureMainThread(nameof(InitializeAsync));
            if (m_Loader == null)
            {
                if (onFailure != null) onFailure("Resource loader is not set. Call SetLoader before InitializeAsync.");
                return;
            }
            if (m_LoaderInitialized)
            {
                if (onComplete != null) onComplete();
                return;
            }
            m_Manifest = manifest;
            m_Loader.Initialize(m_ReadOnlyPath, m_ReadWritePath, m_CurrentVariant, manifest,
                () =>
                {
                    m_LoaderInitialized = true;
                    if (onComplete != null) onComplete();
                },
                err =>
                {
                    FrameworkLog.Error("ResourceLoader.Initialize failed: {0}", err);
                    if (onFailure != null) onFailure(err);
                });
        }

        public void LoadAsset(string assetName, LoadAssetCallbacks loadAssetCallbacks)
        {
            LoadAsset(assetName, null, 0, loadAssetCallbacks, null);
        }

        public void LoadAsset(string assetName, int priority, LoadAssetCallbacks loadAssetCallbacks)
        {
            LoadAsset(assetName, null, priority, loadAssetCallbacks, null);
        }

        public void LoadAsset(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks)
        {
            LoadAsset(assetName, assetType, 0, loadAssetCallbacks, null);
        }

        public void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks)
        {
            LoadAsset(assetName, assetType, priority, loadAssetCallbacks, null);
        }

        public void LoadAsset(string assetName, Type assetType, int priority, LoadAssetCallbacks loadAssetCallbacks, object userData)
        {
            Framework.EnsureMainThread(nameof(LoadAsset));
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");
            if (loadAssetCallbacks == null) throw new FrameworkException("Load asset callbacks is invalid.");
            if (m_Loader == null || !m_LoaderInitialized)
            {
                var fail = loadAssetCallbacks.LoadAssetFailureCallback;
                if (fail != null) fail(assetName, LoadResourceStatus.NotReady, "Resource loader is not initialized.", userData);
                return;
            }

            LoadRequest request = ReferencePool.Acquire<LoadRequest>();
            request.Init(assetName, assetType, priority, m_NextSequence++, null, loadAssetCallbacks, userData);
            Submit(request);
        }

        // ===== Handle-style API =====

        public IAssetLoadHandle LoadAssetWithHandle(string assetName, int priority = 0, object userData = null)
        {
            return LoadAssetWithHandle(assetName, null, priority, userData);
        }

        public IAssetLoadHandle LoadAssetWithHandle(string assetName, Type assetType, int priority = 0, object userData = null)
        {
            Framework.EnsureMainThread(nameof(LoadAssetWithHandle));
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");

            var handle = new AssetLoadHandle(this, ++m_NextHandleId, assetName, assetType, priority, userData);

            if (m_Loader == null || !m_LoaderInitialized)
            {
                handle.SignalFailure(LoadResourceStatus.NotReady, "Resource loader is not initialized.");
                return handle;
            }

            m_LoadingHandles[handle.Id] = handle;
            LoadRequest request = ReferencePool.Acquire<LoadRequest>();
            request.Init(assetName, assetType, priority, m_NextSequence++, handle, null, userData);
            handle.Request = request;
            Submit(request);
            return handle;
        }

        /// <summary>获取所有加载中句柄（非分配版本：写入调用方列表，列表先被清空）。</summary>
        public void GetAllLoadingHandles(List<IAssetLoadHandle> results)
        {
            if (results == null) throw new FrameworkException("Results is invalid.");
            results.Clear();
            foreach (var kv in m_LoadingHandles) results.Add(kv.Value);
        }

        public IAssetLoadHandle[] GetAllLoadingHandles()
        {
            var arr = new IAssetLoadHandle[m_LoadingHandles.Count];
            int i = 0;
            foreach (var kv in m_LoadingHandles) arr[i++] = kv.Value;
            return arr;
        }

        // ================================================================
        //  请求调度
        // ================================================================

        private void Submit(LoadRequest request)
        {
            if (CanDispatch())
            {
                Dispatch(request);
                return;
            }

            request.Queued = true;
            m_Queue.Push(request);
        }

        private bool CanDispatch()
        {
            return m_MaxConcurrentRequests <= 0 || m_InFlightRequests < m_MaxConcurrentRequests;
        }

        /// <summary>从堆顶补派。m_Pumping 挡住"同步完成的 loader → 完成回调 → 再 Pump"的递归，由外层循环接着派。</summary>
        private void PumpQueue()
        {
            if (m_Pumping) return;
            m_Pumping = true;
            try
            {
                while (m_Queue.Count > 0 && CanDispatch())
                {
                    LoadRequest request = m_Queue.Pop();
                    request.Queued = false;
                    if (request.Cancelled)
                    {
                        ReferencePool.Release(request);   // 排队中被取消：不产生 IO
                        continue;
                    }

                    Dispatch(request);
                }
            }
            finally
            {
                m_Pumping = false;
            }
        }

        private void Dispatch(LoadRequest request)
        {
            m_InFlightRequests++;
            if (request.Handle != null) request.Handle.MarkLoading();
            try
            {
                m_Loader.LoadAssetAsync(request.AssetName, request.AssetType, request.Priority, m_RequestCallbacks, request);
            }
            catch (Exception ex)
            {
                if (!request.Completed)
                {
                    CompleteFailure(request, LoadResourceStatus.AssetError, "Loader.LoadAssetAsync threw: " + ex.Message);
                }
            }
        }

        private void OnRequestSuccess(string assetName, object asset, float duration, object userData)
        {
            var request = (LoadRequest)userData;
            if (request.Completed) return;
            request.Completed = true;
            m_InFlightRequests--;

            if (request.Cancelled)
            {
                // 业务方已取消但 loader 仍把资产送达——直接卸载。
                if (asset != null) { try { m_Loader?.UnloadAsset(asset); } catch (Exception ex) { FrameworkLog.Warning("UnloadAsset on cancelled-late asset threw: {0}", ex); } }
            }
            else if (request.Handle != null)
            {
                m_LoadingHandles.Remove(request.Handle.Id);
                request.Handle.SignalSuccess(asset, duration);
            }
            else
            {
                var cb = request.Callbacks.LoadAssetSuccessCallback;
                if (cb != null) { try { cb(assetName, asset, duration, request.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAsset success callback threw: {0}", ex); } }
            }

            ReferencePool.Release(request);
            PumpQueue();
        }

        private void OnRequestFailure(string assetName, LoadResourceStatus status, string message, object userData)
        {
            var request = (LoadRequest)userData;
            if (request.Completed) return;
            CompleteFailure(request, status, message);
        }

        private void CompleteFailure(LoadRequest request, LoadResourceStatus status, string message)
        {
            request.Completed = true;
            m_InFlightRequests--;
            if (!request.Cancelled)
            {
                if (request.Handle != null)
                {
                    m_LoadingHandles.Remove(request.Handle.Id);
                    request.Handle.SignalFailure(status, message);
                }
                else
                {
                    var cb = request.Callbacks.LoadAssetFailureCallback;
                    if (cb != null) { try { cb(request.AssetName, status, message, request.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAsset failure callback threw: {0}", ex); } }
                }
            }

            ReferencePool.Release(request);
            PumpQueue();
        }

        private void OnRequestProgress(string assetName, float progress, object userData)
        {
            var request = (LoadRequest)userData;
            if (request.Completed || request.Cancelled) return;
            if (request.Handle != null)
            {
                request.Handle.SetProgress(progress);
            }
            else
            {
                var cb = request.Callbacks.LoadAssetUpdateCallback;
                if (cb != null) { try { cb(assetName, progress, request.UserData); } catch (Exception ex) { FrameworkLog.Error("LoadAsset update callback threw: {0}", ex); } }
            }
        }

        /// <summary>句柄取消：排队中 → 标记，出堆时丢弃；派发中 → 标记，结果到达时卸载。</summary>
        internal void HandleCancelled(AssetLoadHandle handle, object earlyAsset)
        {
            m_LoadingHandles.Remove(handle.Id);
            LoadRequest request = handle.Request;
            if (request != null)
            {
                request.Cancelled = true;
                handle.Request = null;
            }

            if (earlyAsset != null && m_Loader != null)
            {
                try { m_Loader.UnloadAsset(earlyAsset); } catch (Exception ex) { FrameworkLog.Warning("UnloadAsset on cancel-early asset threw: {0}", ex); }
            }
        }

        /// <summary>句柄改优先级：仍在排队则在堆内调整位置；已派发则只更新记录。</summary>
        internal void HandleReprioritized(AssetLoadHandle handle, int priority)
        {
            LoadRequest request = handle.Request;
            if (request == null) return;
            int old = request.Priority;
            request.Priority = priority;
            if (!request.Queued || old == priority) return;

            int index = m_Queue.IndexOf(request);
            if (index < 0) return;
            if (priority > old) m_Queue.DecreaseKeyAt(index);
            else m_Queue.IncreaseKeyAt(index);
        }

        public void LoadScene(string sceneAssetName, int priority,
            Action<float> onProgress, Action<string> onSuccess, Action<string, string> onFailure, object userData)
        {
            Framework.EnsureMainThread(nameof(LoadScene));
            if (string.IsNullOrEmpty(sceneAssetName)) throw new FrameworkException("Scene asset name is invalid.");
            if (m_Loader == null || !m_LoaderInitialized)
            {
                if (onFailure != null) onFailure(sceneAssetName, "Resource loader is not initialized.");
                return;
            }
            m_Loader.LoadSceneAsync(sceneAssetName, priority, onProgress, onSuccess, onFailure, userData);
        }

        public void UnloadScene(string sceneAssetName,
            Action<string> onSuccess, Action<string, string> onFailure, object userData)
        {
            Framework.EnsureMainThread(nameof(UnloadScene));
            if (string.IsNullOrEmpty(sceneAssetName)) throw new FrameworkException("Scene asset name is invalid.");
            if (m_Loader == null || !m_LoaderInitialized)
            {
                if (onFailure != null) onFailure(sceneAssetName, "Resource loader is not initialized.");
                return;
            }
            m_Loader.UnloadSceneAsync(sceneAssetName, onSuccess, onFailure, userData);
        }

        public void UnloadAsset(object asset)
        {
            Framework.EnsureMainThread(nameof(UnloadAsset));
            if (m_Loader == null) return;
            m_Loader.UnloadAsset(asset);
        }

        public void UnloadUnusedAssets(bool performGCCollect)
        {
            if (m_Loader == null) return;
            m_Loader.UnloadUnusedAssets(performGCCollect);
        }

        public bool HasAsset(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) throw new FrameworkException("Asset name is invalid.");
            if (m_Loader == null || !m_LoaderInitialized) return false;
            return m_Loader.HasAsset(assetName);
        }

        // ===== 批量预载 + 同步获取 =====

        public int CachedAssetCount
        {
            get
            {
                Framework.EnsureMainThread(nameof(CachedAssetCount));
                return m_AssetCache.Count;
            }
        }

        public AssetBatch CreateAssetBatch()
        {
            Framework.EnsureMainThread(nameof(CreateAssetBatch));
            if (m_ShutDown)
            {
                throw new FrameworkException("Cannot create an asset batch after ResourceManager has been shut down.");
            }
            return AssetBatch.Create(this, m_AssetCache);
        }

        public bool TryGetCachedAsset<T>(string assetName, out T asset) where T : class
        {
            Framework.EnsureMainThread(nameof(TryGetCachedAsset));
            return m_AssetCache.TryGet(assetName, out asset);
        }

        public T GetCachedAsset<T>(string assetName) where T : class
        {
            Framework.EnsureMainThread(nameof(GetCachedAsset));
            return m_AssetCache.Get<T>(assetName);
        }

        /// <summary>
        /// 资源加载句柄实现。状态机由 ResourceManager 推动；Completed 事件仅触发一次。
        /// </summary>
        internal sealed class AssetLoadHandle : IAssetLoadHandle
        {
            private readonly ResourceManager m_Owner;
            private readonly float m_StartTime;
            private LoadAssetStatus m_Status;
            private float m_Progress;
            private object m_Asset;
            private LoadResourceStatus m_FailureStatus;
            private string m_Error;
            private float m_Duration;
            private Action<IAssetLoadHandle> m_Completed;

            public AssetLoadHandle(ResourceManager owner, int id, string name, Type type, int priority, object userData)
            {
                m_Owner = owner;
                Id = id;
                AssetName = name;
                AssetType = type;
                Priority = priority;
                UserData = userData;
                m_Status = LoadAssetStatus.Pending;
                m_StartTime = NowSeconds();
            }

            /// <summary>对应的调度请求；完成/取消后置 null。</summary>
            internal LoadRequest Request;

            public int Id { get; }
            public string AssetName { get; }
            public Type AssetType { get; }
            public int Priority { get; private set; }

            public void SetPriority(int priority)
            {
                if (IsDone || Priority == priority) return;
                Priority = priority;
                m_Owner.HandleReprioritized(this, priority);
            }
            public object UserData { get; }
            public LoadAssetStatus Status { get { return m_Status; } }
            public float Progress { get { return m_Progress; } }
            public object Asset { get { return m_Status == LoadAssetStatus.Done ? m_Asset : null; } }
            public LoadResourceStatus FailureStatus { get { return m_FailureStatus; } }
            public string ErrorMessage { get { return m_Error; } }
            public bool IsDone { get { return m_Status == LoadAssetStatus.Done || m_Status == LoadAssetStatus.Failed || m_Status == LoadAssetStatus.Cancelled; } }
            public float Duration { get { return IsDone ? m_Duration : NowSeconds() - m_StartTime; } }

            public event Action<IAssetLoadHandle> Completed
            {
                add
                {
                    if (IsDone) { try { value?.Invoke(this); } catch (Exception ex) { FrameworkLog.Error("AssetLoadHandle.Completed listener threw: {0}", ex); } }
                    else m_Completed += value;
                }
                remove { m_Completed -= value; }
            }

            public void Cancel()
            {
                if (IsDone) return;
                object earlyAsset = m_Asset;
                m_Status = LoadAssetStatus.Cancelled;
                m_Duration = NowSeconds() - m_StartTime;
                m_Error = "Cancelled by caller.";
                m_Owner.HandleCancelled(this, earlyAsset);
                FireCompleted();
            }

            internal void MarkLoading()
            {
                if (m_Status == LoadAssetStatus.Pending) m_Status = LoadAssetStatus.Loading;
            }

            internal void SetProgress(float p)
            {
                if (IsDone) return;
                if (p < 0f) p = 0f; else if (p > 1f) p = 1f;
                m_Progress = p;
            }

            internal void SignalSuccess(object asset, float duration)
            {
                if (IsDone) return;
                Request = null;
                m_Asset = asset;
                m_Progress = 1f;
                m_Duration = duration;
                m_Status = LoadAssetStatus.Done;
                FireCompleted();
            }

            internal void SignalFailure(LoadResourceStatus status, string error)
            {
                if (IsDone) return;
                Request = null;
                m_FailureStatus = status;
                m_Error = error;
                m_Duration = NowSeconds() - m_StartTime;
                m_Status = LoadAssetStatus.Failed;
                FireCompleted();
            }

            private void FireCompleted()
            {
                var cb = m_Completed;
                m_Completed = null; // 一次性
                if (cb == null) return;
                try { cb(this); } catch (Exception ex) { FrameworkLog.Error("AssetLoadHandle.Completed threw: {0}", ex); }
            }

            private static float NowSeconds()
            {
                // 单调时钟，避免系统时钟跳变导致的负数/巨大 Duration，且子秒精度充足。
                return Utility.Timestamp.SecondsF;
            }
        }

        /// <summary>
        /// 一次加载请求（池化）。旧回调式 API 与句柄式 API 都经它排队/派发，loader 的 userData 槽位携带它本身，
        /// 业务的 userData 存在请求里——每次加载不再分配闭包。
        /// </summary>
        internal sealed class LoadRequest : IReference
        {
            public string AssetName;
            public Type AssetType;
            public int Priority;
            public long Sequence;
            public AssetLoadHandle Handle;
            public LoadAssetCallbacks Callbacks;
            public object UserData;
            public bool Queued;
            public bool Cancelled;
            public bool Completed;

            public void Init(string assetName, Type assetType, int priority, long sequence, AssetLoadHandle handle, LoadAssetCallbacks callbacks, object userData)
            {
                AssetName = assetName;
                AssetType = assetType;
                Priority = priority;
                Sequence = sequence;
                Handle = handle;
                Callbacks = callbacks;
                UserData = userData;
                Queued = false;
                Cancelled = false;
                Completed = false;
            }

            public void Clear()
            {
                AssetName = null;
                AssetType = null;
                Handle = null;
                Callbacks = null;
                UserData = null;
                Queued = false;
                Cancelled = false;
                Completed = false;
            }
        }

        /// <summary>优先级高者先；同优先级按提交序号（FIFO）。</summary>
        private sealed class LoadRequestComparer : IComparer<LoadRequest>
        {
            public static readonly LoadRequestComparer Instance = new LoadRequestComparer();

            public int Compare(LoadRequest a, LoadRequest b)
            {
                if (a.Priority != b.Priority) return b.Priority.CompareTo(a.Priority);
                return a.Sequence.CompareTo(b.Sequence);
            }
        }
    }
}
