//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests.TestSupport
{
    /// <summary>
    /// 测试用 IResourceManager 实现：综合 5 份历史 FakeResourceManager 的能力。
    ///
    /// 支持模式：
    ///   - 同步成功（默认）：LoadAsset / LoadAssetWithHandle 立即触发 callback。
    ///   - 同步失败：SyncSucceed=false，触发 LoadAssetFailureCallback / FireFailure。
    ///   - 异步：Async=true，请求堆积；测试代码调 FlushAllPending() 触发。
    ///   - 取消：异步模式下，handle.Cancel() 后再 Flush，模拟"晚到资产"路径（UnloadCalls++）。
    ///
    /// 自定义资产：AssetFactory 默认创建 `MockAsset { Name = assetName }`；测试可换成自定义工厂。
    ///
    /// Handle API 屏蔽：旧 UIManagerTests / UIControllerStackTests 不走 handle 路径，可设 ThrowOnHandleApi=true 锁死。
    /// </summary>
    public sealed class MockResourceManager : IResourceManager
    {
        // ===== 配置 =====
        public bool SyncSucceed { get; set; } = true;
        public string FailMessage { get; set; } = "fake load failed";
        public bool Async { get; set; }
        public Func<string, object> AssetFactory { get; set; }
        public bool ThrowOnHandleApi { get; set; }

        // ===== 观察 =====
        public int UnloadCalls;
        public string LastAssetName { get; private set; }
        public IReadOnlyList<MockAssetLoadHandle> PendingHandlesView => m_PendingHandles;

        // ===== 内部状态 =====
        private readonly List<(string Name, LoadAssetCallbacks Cb, object UserData)> m_PendingCallbacks = new List<(string, LoadAssetCallbacks, object)>();
        private readonly List<MockAssetLoadHandle> m_PendingHandles = new List<MockAssetLoadHandle>();

        public MockResourceManager()
        {
            AssetFactory = name => new MockAsset { Name = name };
        }

        // ===== IResourceManager 标准属性 =====
        public string ReadOnlyPath => null;
        public string ReadWritePath => null;
        public ResourceMode Mode => ResourceMode.EditorSimulation;
        public bool IsInitialized => true;
        public int LoadedBundleCount => 0;
        public int LoadedAssetCount => 0;
        public int LoadingTaskCount => m_PendingCallbacks.Count + m_PendingHandles.Count;

        public void SetReadOnlyPath(string p) { }
        public void SetReadWritePath(string p) { }
        public void SetCurrentVariant(string v) { }
        public void SetMode(ResourceMode m) { }
        public void SetLoader(IResourceLoader l) { }
        public void InitializeAsync(AssetManifest m, Action onComplete, Action<string> onFailure) { onComplete?.Invoke(); }

        // ===== LoadAsset 回调风格 =====
        public void LoadAsset(string n, LoadAssetCallbacks cb) { LoadAsset(n, null, 0, cb, null); }
        public void LoadAsset(string n, int p, LoadAssetCallbacks cb) { LoadAsset(n, null, p, cb, null); }
        public void LoadAsset(string n, Type t, LoadAssetCallbacks cb) { LoadAsset(n, t, 0, cb, null); }
        public void LoadAsset(string n, Type t, int p, LoadAssetCallbacks cb) { LoadAsset(n, t, p, cb, null); }
        public void LoadAsset(string n, Type t, int p, LoadAssetCallbacks cb, object ud)
        {
            LastAssetName = n;
            if (Async) { m_PendingCallbacks.Add((n, cb, ud)); return; }
            FireCallback(n, cb, ud);
        }

        // ===== LoadAssetWithHandle 风格 =====
        public IAssetLoadHandle LoadAssetWithHandle(string n, int p = 0, object ud = null)
            => LoadAssetWithHandle(n, null, p, ud);

        public IAssetLoadHandle LoadAssetWithHandle(string n, Type t, int p = 0, object ud = null)
        {
            if (ThrowOnHandleApi) throw new NotImplementedException();
            LastAssetName = n;
            var h = new MockAssetLoadHandle { AssetName = n, AssetType = t, Priority = p, UserData = ud };
            if (Async) { m_PendingHandles.Add(h); return h; }
            if (SyncSucceed) h.FireSuccess(AssetFactory(n));
            else h.FireFailure(LoadResourceStatus.NotExist, FailMessage);
            return h;
        }

        public IAssetLoadHandle[] GetAllLoadingHandles()
        {
            var arr = new IAssetLoadHandle[m_PendingHandles.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = m_PendingHandles[i];
            return arr;
        }

        // ===== Scene =====
        public void LoadScene(string n, int p, Action<float> onP, Action<string> onS, Action<string, string> onF, object ud)
        {
            if (SyncSucceed) onS?.Invoke(n);
            else onF?.Invoke(n, FailMessage);
        }

        public void UnloadScene(string n, Action<string> onS, Action<string, string> onF, object ud) { onS?.Invoke(n); }

        // ===== Unload / 查询 =====
        public void UnloadAsset(object asset) { UnloadCalls++; }
        public void UnloadUnusedAssets(bool gc) { }
        public bool HasAsset(string n) => true;

        // ===== 批量预载 =====
        // AssetBatch 需要 ResourceManager 内部的 AssetCache，Mock 无法伪造；
        // 需要覆盖批次行为的测试请直接构造真实 ResourceManager（见 AssetBatchTests）。
        // 这里仅提供一张手填的同步缓存表，供只关心 GetCachedAsset 的测试使用。
        public readonly Dictionary<string, object> CachedAssets = new Dictionary<string, object>(StringComparer.Ordinal);

        public int CachedAssetCount => CachedAssets.Count;

        public AssetBatch CreateAssetBatch()
        {
            throw new NotSupportedException("MockResourceManager does not support AssetBatch; use the real ResourceManager.");
        }

        public bool TryGetCachedAsset<T>(string assetName, out T asset) where T : class
        {
            asset = null;
            if (assetName == null || !CachedAssets.TryGetValue(assetName, out var raw)) return false;
            asset = raw as T;
            return asset != null;
        }

        public T GetCachedAsset<T>(string assetName) where T : class
        {
            if (!TryGetCachedAsset<T>(assetName, out var asset))
            {
                throw new FrameworkException($"Asset '{assetName}' is not preloaded.");
            }
            return asset;
        }

        // ===== 异步 flush =====

        /// <summary>
        /// 触发所有挂起的回调与 handle。Cancelled 状态的 handle 会模拟"晚到资产"路径并累加 UnloadCalls。
        /// </summary>
        public void FlushAllPending()
        {
            var callbacks = new List<(string, LoadAssetCallbacks, object)>(m_PendingCallbacks);
            m_PendingCallbacks.Clear();
            foreach (var (n, cb, ud) in callbacks) FireCallback(n, cb, ud);

            var handles = m_PendingHandles.ToArray();
            m_PendingHandles.Clear();
            foreach (var h in handles)
            {
                if (h.Status == LoadAssetStatus.Cancelled)
                {
                    if (SyncSucceed) UnloadCalls++;
                    continue;
                }
                if (h.Status != LoadAssetStatus.Loading) continue;
                if (SyncSucceed) h.FireSuccess(AssetFactory(h.AssetName));
                else h.FireFailure(LoadResourceStatus.NotExist, FailMessage);
            }
        }

        private void FireCallback(string n, LoadAssetCallbacks cb, object ud)
        {
            if (SyncSucceed) cb.LoadAssetSuccessCallback?.Invoke(n, AssetFactory(n), 0f, ud);
            else cb.LoadAssetFailureCallback?.Invoke(n, LoadResourceStatus.NotExist, FailMessage, ud);
        }
    }

    /// <summary>
    /// MockResourceManager 默认创建的资产占位类型。带 Name 字段便于测试断言。
    /// </summary>
    public sealed class MockAsset
    {
        public string Name;
    }
}
