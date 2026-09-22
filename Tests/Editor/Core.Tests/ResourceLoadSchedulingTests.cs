//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests
{
    /// <summary>WS2-M1：ResourceManager 请求调度——并发上限、优先级出队、排队取消零 IO、重排优先级、关闭语义、零分配。</summary>
    public sealed class ResourceLoadSchedulingTests
    {
        /// <summary>手动完成的假 loader：记录派发顺序，测试决定何时完成哪一个。</summary>
        private sealed class ManualLoader : IResourceLoader
        {
            public readonly List<string> Dispatched = new List<string>();
            private readonly List<(string name, LoadAssetCallbacks cb, object ud)> m_Pending = new List<(string, LoadAssetCallbacks, object)>();
            public bool Initialized;

            public bool IsInitialized { get { return Initialized; } }
            public bool RequiresManifest { get { return false; } }
            public int LoadedBundleCount { get { return 0; } }
            public int LoadedAssetCount { get { return 0; } }
            public int LoadingTaskCount { get { return m_Pending.Count; } }
            public int UnloadCalls;

            public void Initialize(string ro, string rw, string variant, AssetManifest m, Action onComplete, Action<string> onFailure)
            {
                Initialized = true;
                onComplete?.Invoke();
            }

            public bool HasAsset(string assetName) { return true; }

            public void LoadAssetAsync(string assetName, Type assetType, int priority, LoadAssetCallbacks cb, object userData)
            {
                Dispatched.Add(assetName);
                m_Pending.Add((assetName, cb, userData));
            }

            public void CompleteNext()
            {
                var item = m_Pending[0];
                m_Pending.RemoveAt(0);
                item.cb.LoadAssetSuccessCallback?.Invoke(item.name, new object(), 0f, item.ud);
            }

            public void FailNext(LoadResourceStatus status)
            {
                var item = m_Pending[0];
                m_Pending.RemoveAt(0);
                item.cb.LoadAssetFailureCallback?.Invoke(item.name, status, "failed", item.ud);
            }

            public void ProgressNext(float p)
            {
                var item = m_Pending[0];
                item.cb.LoadAssetUpdateCallback?.Invoke(item.name, p, item.ud);
            }

            public void LoadSceneAsync(string n, int p, Action<float> onP, Action<string> onS, Action<string, string> onF, object ud) { onS?.Invoke(n); }
            public void UnloadSceneAsync(string n, Action<string> onS, Action<string, string> onF, object ud) { onS?.Invoke(n); }
            public void UnloadAsset(object asset) { UnloadCalls++; }
            public void UnloadUnusedAssets(bool gc) { }
            public void Shutdown() { Initialized = false; }
        }

        private ResourceManager m_Manager;
        private ManualLoader m_Loader;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Manager = new ResourceManager();
            m_Loader = new ManualLoader();
            m_Manager.SetReadOnlyPath("/ro");
            m_Manager.SetReadWritePath("/rw");
            m_Manager.SetMode(ResourceMode.AssetBundle);
            m_Manager.SetLoader(m_Loader);
            m_Manager.InitializeAsync(null, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            m_Manager.Shutdown();
        }

        [Test]
        public void UnderCapacity_DispatchesSynchronously()
        {
            m_Manager.MaxConcurrentRequests = 2;
            IAssetLoadHandle h = m_Manager.LoadAssetWithHandle("a");
            Assert.AreEqual(LoadAssetStatus.Loading, h.Status);
            Assert.AreEqual(1, m_Loader.Dispatched.Count, "容量未满应立即派发，行为与旧版一致。");
            Assert.AreEqual(1, m_Manager.InFlightRequestCount);
            Assert.AreEqual(0, m_Manager.QueuedRequestCount);
        }

        [Test]
        public void OverCapacity_QueuesByPriority_ThenFifo()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("first");          // 立即派发
            m_Manager.LoadAssetWithHandle("low-a", 0);
            m_Manager.LoadAssetWithHandle("high", 10);
            m_Manager.LoadAssetWithHandle("low-b", 0);
            Assert.AreEqual(3, m_Manager.QueuedRequestCount);

            m_Loader.CompleteNext();   // first 完成 → 派 high
            m_Loader.CompleteNext();   // high 完成 → 派 low-a
            m_Loader.CompleteNext();   // low-a 完成 → 派 low-b
            CollectionAssert.AreEqual(new[] { "first", "high", "low-a", "low-b" }, m_Loader.Dispatched);
            Assert.AreEqual(0, m_Manager.QueuedRequestCount);
        }

        [Test]
        public void CancelWhileQueued_NeverReachesLoader()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("first");
            IAssetLoadHandle queued = m_Manager.LoadAssetWithHandle("queued");
            queued.Cancel();
            Assert.AreEqual(LoadAssetStatus.Cancelled, queued.Status);

            m_Loader.CompleteNext();
            CollectionAssert.AreEqual(new[] { "first" }, m_Loader.Dispatched, "排队中取消的请求不得产生 IO。");
            Assert.AreEqual(0, m_Manager.QueuedRequestCount);
            Assert.AreEqual(0, m_Manager.InFlightRequestCount);
        }

        [Test]
        public void CancelInFlight_LateResultIsUnloaded_AndSlotFreed()
        {
            m_Manager.MaxConcurrentRequests = 1;
            IAssetLoadHandle h = m_Manager.LoadAssetWithHandle("inflight");
            m_Manager.LoadAssetWithHandle("next");
            h.Cancel();
            Assert.AreEqual(1, m_Manager.InFlightRequestCount, "取消不撤回已派发的 IO。");

            m_Loader.CompleteNext();   // 迟到结果
            Assert.AreEqual(1, m_Loader.UnloadCalls, "取消后到达的资产应被卸载。");
            CollectionAssert.AreEqual(new[] { "inflight", "next" }, m_Loader.Dispatched, "槽位释放后应补派下一个。");
        }

        [Test]
        public void SetPriorityWhileQueued_ReordersQueue()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("first");
            IAssetLoadHandle a = m_Manager.LoadAssetWithHandle("a", 0);
            IAssetLoadHandle b = m_Manager.LoadAssetWithHandle("b", 0);
            b.SetPriority(100);
            Assert.AreEqual(100, b.Priority);

            m_Loader.CompleteNext();
            Assert.AreEqual("b", m_Loader.Dispatched[1], "提升优先级后应先于同级更早提交的请求出队。");
            m_Loader.CompleteNext();
            Assert.AreEqual("a", m_Loader.Dispatched[2]);
        }

        [Test]
        public void LegacyCallbackApi_GoesThroughSameQueue()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("first");
            string done = null;
            m_Manager.LoadAsset("legacy", null, 5, new LoadAssetCallbacks((n, asset, d, ud) => done = n), "ud");
            Assert.AreEqual(1, m_Manager.QueuedRequestCount);

            m_Loader.CompleteNext();
            m_Loader.CompleteNext();
            Assert.AreEqual("legacy", done);
        }

        [Test]
        public void Failure_ReleasesSlot_AndReportsHandle()
        {
            m_Manager.MaxConcurrentRequests = 1;
            IAssetLoadHandle h = m_Manager.LoadAssetWithHandle("bad");
            m_Manager.LoadAssetWithHandle("next");
            m_Loader.FailNext(LoadResourceStatus.NotExist);

            Assert.AreEqual(LoadAssetStatus.Failed, h.Status);
            Assert.AreEqual(LoadResourceStatus.NotExist, h.FailureStatus);
            Assert.AreEqual(2, m_Loader.Dispatched.Count);
        }

        [Test]
        public void Progress_IsForwardedToHandle()
        {
            IAssetLoadHandle h = m_Manager.LoadAssetWithHandle("p");
            m_Loader.ProgressNext(0.5f);
            Assert.AreEqual(0.5f, h.Progress, 1e-6f);
        }

        [Test]
        public void SynchronousLoader_DrainsQueueWithoutRecursionBlowup()
        {
            // 同步完成的 loader：完成回调里 Pump 会被外层循环接管，深队列不应递归爆栈。
            var sync = new ResourceManagerTestsSyncLoader();
            var rm = new ResourceManager();
            rm.SetReadOnlyPath("/ro"); rm.SetReadWritePath("/rw"); rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(sync);
            rm.InitializeAsync(null, null, null);
            rm.MaxConcurrentRequests = 1;
            int completed = 0;
            sync.Hold = true;
            for (int i = 0; i < 5000; i++)
            {
                rm.LoadAssetWithHandle("x" + i).Completed += _ => completed++;
            }

            Assert.AreEqual(4999, rm.QueuedRequestCount);
            sync.Hold = false;
            sync.CompleteHeld();   // 第一个完成 → 后续 4999 个同步连锁完成
            Assert.AreEqual(5000, completed);
            Assert.AreEqual(0, rm.QueuedRequestCount);
            rm.Shutdown();
        }

        [Test]
        public void Shutdown_CancelsQueuedHandles_AndFailsQueuedCallbacks()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("first");
            IAssetLoadHandle queued = m_Manager.LoadAssetWithHandle("queued");
            LoadResourceStatus status = LoadResourceStatus.Success;
            m_Manager.LoadAsset("legacy", new LoadAssetCallbacks((n, a, d, ud) => { }, (n, s, m, ud) => status = s));

            m_Manager.Shutdown();
            Assert.AreEqual(LoadAssetStatus.Cancelled, queued.Status);
            Assert.AreEqual(LoadResourceStatus.NotReady, status);
            Assert.AreEqual(0, m_Manager.QueuedRequestCount);
        }

        [Test]
        public void GetAllLoadingHandles_NonAlloc_ListsInFlightAndQueued()
        {
            m_Manager.MaxConcurrentRequests = 1;
            m_Manager.LoadAssetWithHandle("a");
            m_Manager.LoadAssetWithHandle("b");
            var list = new List<IAssetLoadHandle>();
            m_Manager.GetAllLoadingHandles(list);
            Assert.AreEqual(2, list.Count);
        }

        [Test]
        public void LegacyLoad_SteadyState_DoesNotAllocateInManager()
        {
            // 同步完成 loader + 无句柄的旧回调式 API：管理器侧（请求池化、共享回调）应零分配；
            // 假 loader 自己 new object() 作为资产不可避免，用 Hold 模式让它不产生资产。
            var sync = new ResourceManagerTestsSyncLoader { NullAsset = true };
            var rm = new ResourceManager();
            rm.SetReadOnlyPath("/ro"); rm.SetReadWritePath("/rw"); rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(sync);
            rm.InitializeAsync(null, null, null);
            var cb = new LoadAssetCallbacks((n, a, d, ud) => { });
            TestDelegate body = () =>
            {
                for (int i = 0; i < 16; i++) rm.LoadAsset("asset", cb);
            };
            body();
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
            rm.Shutdown();
        }

        /// <summary>同步完成的 loader（可 Hold 住第一个再放行）。</summary>
        private sealed class ResourceManagerTestsSyncLoader : IResourceLoader
        {
            public bool Hold;
            public bool NullAsset;
            private (string name, LoadAssetCallbacks cb, object ud) m_Held;
            private bool m_HasHeld;

            public bool IsInitialized { get { return true; } }
            public bool RequiresManifest { get { return false; } }
            public int LoadedBundleCount { get { return 0; } }
            public int LoadedAssetCount { get { return 0; } }
            public int LoadingTaskCount { get { return 0; } }
            public void Initialize(string ro, string rw, string variant, AssetManifest m, Action onComplete, Action<string> onFailure) { onComplete?.Invoke(); }
            public bool HasAsset(string assetName) { return true; }

            public void LoadAssetAsync(string assetName, Type assetType, int priority, LoadAssetCallbacks cb, object userData)
            {
                if (Hold && !m_HasHeld) { m_Held = (assetName, cb, userData); m_HasHeld = true; return; }
                cb.LoadAssetSuccessCallback?.Invoke(assetName, NullAsset ? null : new object(), 0f, userData);
            }

            public void CompleteHeld()
            {
                if (!m_HasHeld) return;
                m_HasHeld = false;
                m_Held.cb.LoadAssetSuccessCallback?.Invoke(m_Held.name, new object(), 0f, m_Held.ud);
            }

            public void LoadSceneAsync(string n, int p, Action<float> onP, Action<string> onS, Action<string, string> onF, object ud) { onS?.Invoke(n); }
            public void UnloadSceneAsync(string n, Action<string> onS, Action<string, string> onF, object ud) { onS?.Invoke(n); }
            public void UnloadAsset(object asset) { }
            public void UnloadUnusedAssets(bool gc) { }
            public void Shutdown() { }
        }
    }
}
