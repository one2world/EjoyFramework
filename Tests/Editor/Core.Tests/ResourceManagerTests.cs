//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Resource;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// ResourceManager 转发与生命周期测试。
    /// 用一个 mock IResourceLoader 验证：
    ///   - LoaderNotSet/NotInitialized 时 LoadAsset 走失败回调而不抛
    ///   - InitializeAsync 成功后才接受加载
    ///   - 路径/Variant 透传到 Loader.Initialize
    ///   - LoadScene / UnloadAsset / UnloadUnusedAssets 转发
    /// </summary>
    public class ResourceManagerTests
    {
        private sealed class FakeLoader : IResourceLoader
        {
            public bool Initialized;
            public string RecordedReadOnly, RecordedReadWrite, RecordedVariant;
            public AssetManifest RecordedManifest;
            public Action OnComplete;
            public Action<string> OnFailure;
            public int InitCalls, LoadCalls, LoadSceneCalls, UnloadSceneCalls, UnloadAssetCalls, UnloadUnusedCalls, ShutdownCalls;
            public bool AutoCompleteInit = true;
            public Func<string, bool> HasAssetFn;
            public Action<string, Type, int, LoadAssetCallbacks, object> LoadHandler;

            public bool IsInitialized { get { return Initialized; } }
            public bool RequiresManifest { get { return false; } }
            public int LoadedBundleCount { get { return 1; } }
            public int LoadedAssetCount { get { return 2; } }
            public int LoadingTaskCount { get { return 3; } }

            public void Initialize(string ro, string rw, string variant, AssetManifest m, Action onComplete, Action<string> onFailure)
            {
                InitCalls++;
                RecordedReadOnly = ro; RecordedReadWrite = rw; RecordedVariant = variant; RecordedManifest = m;
                OnComplete = onComplete; OnFailure = onFailure;
                if (AutoCompleteInit) { Initialized = true; onComplete?.Invoke(); }
            }

            public bool HasAsset(string assetName) { return HasAssetFn != null ? HasAssetFn(assetName) : true; }

            public void LoadAssetAsync(string assetName, Type assetType, int priority, LoadAssetCallbacks cb, object userData)
            {
                LoadCalls++;
                if (LoadHandler != null) LoadHandler(assetName, assetType, priority, cb, userData);
                else cb.LoadAssetSuccessCallback?.Invoke(assetName, new object(), 0f, userData);
            }

            public void LoadSceneAsync(string n, int p, Action<float> onP, Action<string> onS, Action<string, string> onF, object ud)
            {
                LoadSceneCalls++;
                onS?.Invoke(n);
            }

            public void UnloadSceneAsync(string n, Action<string> onS, Action<string, string> onF, object ud)
            {
                UnloadSceneCalls++;
                onS?.Invoke(n);
            }

            public void UnloadAsset(object asset) { UnloadAssetCalls++; }
            public void UnloadUnusedAssets(bool gc) { UnloadUnusedCalls++; }
            public void Shutdown() { ShutdownCalls++; Initialized = false; }
        }

        private static IResourceManager NewManager()
        {
            // 使用反射避免 internal 限制
            var t = typeof(LoadAssetCallbacks).Assembly.GetType("EjoyFramework.Core.Resource.ResourceManager");
            return (IResourceManager)Activator.CreateInstance(t, true);
        }

        [Test]
        public void LoadAsset_BeforeInitialize_FiresNotReadyFailure()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(new FakeLoader { AutoCompleteInit = false });

            LoadResourceStatus capturedStatus = LoadResourceStatus.Success;
            string capturedMsg = null;
            var cb = new LoadAssetCallbacks(
                (n, a, d, ud) => Assert.Fail("Should not succeed before init"),
                (n, s, m, ud) => { capturedStatus = s; capturedMsg = m; });

            rm.LoadAsset("UI/Main.prefab", cb);
            Assert.AreEqual(LoadResourceStatus.NotReady, capturedStatus);
            Assert.IsNotNull(capturedMsg);
        }

        [Test]
        public void LoadAsset_AfterInitialize_ForwardsToLoader()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetCurrentVariant("hd");
            rm.SetMode(ResourceMode.AssetBundle);
            var loader = new FakeLoader();
            rm.SetLoader(loader);
            rm.InitializeAsync(null, null, null);

            Assert.IsTrue(rm.IsInitialized);
            Assert.AreEqual("/ro", loader.RecordedReadOnly);
            Assert.AreEqual("/rw", loader.RecordedReadWrite);
            Assert.AreEqual("hd", loader.RecordedVariant);

            string assetReceived = null;
            var cb = new LoadAssetCallbacks((n, a, d, ud) => assetReceived = n);
            rm.LoadAsset("UI/Main.prefab", cb);
            Assert.AreEqual(1, loader.LoadCalls);
            Assert.AreEqual("UI/Main.prefab", assetReceived);
        }

        [Test]
        public void SetMode_AfterInitialize_Throws()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(new FakeLoader());
            rm.InitializeAsync(null, null, null);
            Assert.Throws<FrameworkException>(() => rm.SetMode(ResourceMode.EditorSimulation));
        }

        [Test]
        public void SetLoader_AfterInitialize_Throws()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(new FakeLoader());
            rm.InitializeAsync(null, null, null);
            Assert.Throws<FrameworkException>(() => rm.SetLoader(new FakeLoader()));
        }

        [Test]
        public void InitializeAsync_WithoutLoader_FiresFailure()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);

            string err = null;
            rm.InitializeAsync(null, () => Assert.Fail("Should not succeed"), e => err = e);
            Assert.IsNotNull(err);
        }

        [Test]
        public void HasAsset_NotInitialized_ReturnsFalse()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            Assert.IsFalse(rm.HasAsset("UI/Main.prefab"));
        }

        [Test]
        public void Stats_DelegateToLoader()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            rm.SetLoader(new FakeLoader());
            rm.InitializeAsync(null, null, null);

            Assert.AreEqual(1, rm.LoadedBundleCount);
            Assert.AreEqual(2, rm.LoadedAssetCount);
            Assert.AreEqual(3, rm.LoadingTaskCount);
        }

        [Test]
        public void LoadScene_Unload_Forward()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            var loader = new FakeLoader();
            rm.SetLoader(loader);
            rm.InitializeAsync(null, null, null);

            string s = null;
            rm.LoadScene("Scenes/Main.unity", 0, null, ok => s = ok, null, null);
            Assert.AreEqual(1, loader.LoadSceneCalls);
            Assert.AreEqual("Scenes/Main.unity", s);

            s = null;
            rm.UnloadScene("Scenes/Main.unity", ok => s = ok, null, null);
            Assert.AreEqual(1, loader.UnloadSceneCalls);
            Assert.AreEqual("Scenes/Main.unity", s);
        }

        [Test]
        public void UnloadAsset_ForwardsToLoader()
        {
            var rm = NewManager();
            rm.SetReadOnlyPath("/ro");
            rm.SetReadWritePath("/rw");
            rm.SetMode(ResourceMode.AssetBundle);
            var loader = new FakeLoader();
            rm.SetLoader(loader);
            rm.InitializeAsync(null, null, null);

            rm.UnloadAsset(new object());
            Assert.AreEqual(1, loader.UnloadAssetCalls);
        }
    }
}
