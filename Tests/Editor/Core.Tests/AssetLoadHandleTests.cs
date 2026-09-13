//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Resource;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 验证 IAssetLoadHandle 的状态机：Pending→Loading→(Done/Failed/Cancelled)，Completed 一次性触发，Cancel 后到达的资产被回收。
    /// </summary>
    public class AssetLoadHandleTests
    {
        private sealed class ManualLoader : IResourceLoader
        {
            public bool IsInitialized { get; private set; } = true;
            public bool RequiresManifest => false;
            public int LoadedBundleCount => 0;
            public int LoadedAssetCount => 0;
            public int LoadingTaskCount => 0;
            public int UnloadCalls;

            private LoadAssetCallbacks m_PendingCallbacks;
            private string m_PendingName;
            private object m_PendingUserData;

            public void Initialize(string ro, string rw, string v, AssetManifest m, Action onComplete, Action<string> onFailure) { onComplete?.Invoke(); }
            public bool HasAsset(string n) => true;

            public void LoadAssetAsync(string assetName, Type assetType, int priority, LoadAssetCallbacks callbacks, object userData)
            {
                m_PendingCallbacks = callbacks;
                m_PendingName = assetName;
                m_PendingUserData = userData;
            }
            public void LoadSceneAsync(string n, int p, Action<float> oP, Action<string> oS, Action<string, string> oF, object ud) { }
            public void UnloadSceneAsync(string n, Action<string> oS, Action<string, string> oF, object ud) { }
            public void UnloadAsset(object asset) { UnloadCalls++; }
            public void UnloadUnusedAssets(bool gc) { }
            public void Shutdown() { }

            public void FlushSuccess(object asset)
            {
                m_PendingCallbacks.LoadAssetSuccessCallback?.Invoke(m_PendingName, asset, 0.1f, m_PendingUserData);
                m_PendingCallbacks = null;
            }
            public void FlushProgress(float p)
            {
                m_PendingCallbacks.LoadAssetUpdateCallback?.Invoke(m_PendingName, p, m_PendingUserData);
            }
            public void FlushFailure(LoadResourceStatus s, string err)
            {
                m_PendingCallbacks.LoadAssetFailureCallback?.Invoke(m_PendingName, s, err, m_PendingUserData);
                m_PendingCallbacks = null;
            }
        }

        private static (IResourceManager, ManualLoader) NewManager()
        {
            var t = typeof(IResourceManager).Assembly.GetType("EjoyFramework.Core.Resource.ResourceManager");
            var rm = (IResourceManager)Activator.CreateInstance(t, true);
            var loader = new ManualLoader();
            rm.SetMode(ResourceMode.EditorSimulation);
            rm.SetLoader(loader);
            rm.InitializeAsync(null, null, null);
            return (rm, loader);
        }

        [Test]
        public void LoadAssetWithHandle_InitialState_IsLoading()
        {
            var (rm, _) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/main");
            Assert.AreEqual(LoadAssetStatus.Loading, h.Status);
            Assert.IsFalse(h.IsDone);
            Assert.AreEqual(0f, h.Progress);
            Assert.IsNull(h.Asset);
        }

        [Test]
        public void Handle_Success_FiresCompletedAndExposesAsset()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/main");
            IAssetLoadHandle captured = null;
            h.Completed += handle => captured = handle;

            var fake = new object();
            loader.FlushSuccess(fake);

            Assert.AreSame(h, captured);
            Assert.AreEqual(LoadAssetStatus.Done, h.Status);
            Assert.AreEqual(fake, h.Asset);
            Assert.AreEqual(1f, h.Progress);
            Assert.IsTrue(h.IsDone);
        }

        [Test]
        public void Handle_Progress_PropagatesFromLoader()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/main");

            loader.FlushProgress(0.3f);
            Assert.AreEqual(0.3f, h.Progress, 0.0001f);
            loader.FlushProgress(0.8f);
            Assert.AreEqual(0.8f, h.Progress, 0.0001f);
        }

        [Test]
        public void Handle_Failure_FiresCompletedWithError()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/missing");
            IAssetLoadHandle captured = null;
            h.Completed += handle => captured = handle;

            loader.FlushFailure(LoadResourceStatus.NotExist, "no such asset");

            Assert.AreSame(h, captured);
            Assert.AreEqual(LoadAssetStatus.Failed, h.Status);
            Assert.AreEqual(LoadResourceStatus.NotExist, h.FailureStatus);
            Assert.AreEqual("no such asset", h.ErrorMessage);
        }

        [Test]
        public void Handle_Cancel_BeforeLoaderCallback_DiscardsArrivingAsset()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("slow/asset");
            bool completed = false;
            h.Completed += handle => completed = handle.Status == LoadAssetStatus.Cancelled;

            h.Cancel();
            Assert.AreEqual(LoadAssetStatus.Cancelled, h.Status);
            Assert.IsTrue(completed);

            // Loader 最终送达成功——应该被 UnloadAsset 回收
            var fakeAsset = new object();
            loader.FlushSuccess(fakeAsset);

            Assert.AreEqual(1, loader.UnloadCalls, "Cancelled handle's late asset should be unloaded");
            Assert.IsNull(h.Asset, "Asset getter should remain null after Cancel");
        }

        [Test]
        public void Handle_Cancel_AfterDone_IsNoOp()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/x");
            loader.FlushSuccess(new object());
            Assert.AreEqual(LoadAssetStatus.Done, h.Status);

            h.Cancel(); // no-op
            Assert.AreEqual(LoadAssetStatus.Done, h.Status);
        }

        [Test]
        public void Completed_RegisteredAfterDone_FiresSynchronously()
        {
            var (rm, loader) = NewManager();
            var h = rm.LoadAssetWithHandle("ui/x");
            loader.FlushSuccess(new object());

            IAssetLoadHandle captured = null;
            h.Completed += handle => captured = handle;
            Assert.AreSame(h, captured);
        }

        [Test]
        public void GetAllLoadingHandles_TracksInflight()
        {
            var (rm, loader) = NewManager();
            var h1 = rm.LoadAssetWithHandle("a");
            var h2 = rm.LoadAssetWithHandle("b");
            Assert.AreEqual(2, rm.GetAllLoadingHandles().Length);

            loader.FlushSuccess(new object());
            Assert.AreEqual(1, rm.GetAllLoadingHandles().Length);
        }

        [Test]
        public void LoadAssetWithHandle_NotInitialized_FailsSynchronously()
        {
            var t = typeof(IResourceManager).Assembly.GetType("EjoyFramework.Core.Resource.ResourceManager");
            var rm = (IResourceManager)Activator.CreateInstance(t, true);
            // 故意没有 SetLoader/InitializeAsync
            var h = rm.LoadAssetWithHandle("x");
            Assert.AreEqual(LoadAssetStatus.Failed, h.Status);
            Assert.AreEqual(LoadResourceStatus.NotReady, h.FailureStatus);
        }
    }
}
