//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Scene;
using EjoyFramework.Core.Streaming;

namespace EjoyFramework.Tests
{
    /// <summary>WS3-M2：SceneStreamingHandler——单元 ↔ 附加场景的映射、成功/失败/卸载回报、无场景单元、映射重复、Dispose 解绑。</summary>
    public sealed class SceneStreamingHandlerTests
    {
        private WorldStreamingManager m_Mgr;
        private FakeSceneManager m_Scenes;
        private SceneStreamingHandler m_Handler;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Mgr = new WorldStreamingManager();
            m_Mgr.CellSize = 10f;
            m_Mgr.ConfigureLayer(0, new StreamingLayerSettings { LoadRadius = 25f, UnloadRadius = 35f });
            m_Mgr.MaxLoadStartsPerFrame = 100;
            m_Mgr.MaxLoadsInFlight = 100;
            m_Scenes = new FakeSceneManager();
            m_Handler = new SceneStreamingHandler(m_Mgr, m_Scenes, new Resolver());
            m_Mgr.SetHandler(m_Handler);
        }

        [TearDown]
        public void TearDown()
        {
            m_Handler.Dispose();
            m_Mgr.Shutdown();
        }

        [Test]
        public void Load_MapsCellToScene_AndReportsOnSceneSuccess()
        {
            int id = m_Mgr.RegisterCell(0, 0, 0, 7);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);

            CollectionAssert.AreEqual(new[] { "World/L0_0_0" }, m_Scenes.LoadRequests);
            Assert.AreEqual(StreamingCellState.Loading, m_Mgr.GetCellState(id));
            Assert.AreEqual(1, m_Handler.TrackedSceneCount);

            m_Scenes.CompleteLoad("World/L0_0_0");
            Assert.AreEqual(StreamingCellState.Loaded, m_Mgr.GetCellState(id));
        }

        [Test]
        public void Unload_UnloadsScene_AndReportsOnSceneUnloaded()
        {
            int id = m_Mgr.RegisterCell(0, 0, 0, 7);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            m_Scenes.CompleteLoad("World/L0_0_0");

            m_Mgr.SetObserver(1, 500f, 500f);
            m_Mgr.Update(0f, 0f);
            CollectionAssert.AreEqual(new[] { "World/L0_0_0" }, m_Scenes.UnloadRequests);
            Assert.AreEqual(StreamingCellState.Unloading, m_Mgr.GetCellState(id));

            m_Scenes.CompleteUnload("World/L0_0_0");
            Assert.AreEqual(StreamingCellState.Unloaded, m_Mgr.GetCellState(id));
            Assert.AreEqual(0, m_Handler.TrackedSceneCount);
        }

        [Test]
        public void LoadFailure_ReportsFailure_AndUntracks()
        {
            int id = m_Mgr.RegisterCell(0, 0, 0, 7);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            m_Scenes.FailLoad("World/L0_0_0");
            Assert.AreEqual(StreamingCellState.Unloaded, m_Mgr.GetCellState(id));
            Assert.AreEqual(1, m_Mgr.TotalLoadFailures);
            Assert.AreEqual(0, m_Handler.TrackedSceneCount);
        }

        [Test]
        public void CellWithoutScene_LoadsImmediately()
        {
            int id = m_Mgr.RegisterCell(0, 0, 0, Resolver.NoScene);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(0, m_Scenes.LoadRequests.Count);
            Assert.AreEqual(StreamingCellState.Loaded, m_Mgr.GetCellState(id));
        }

        [Test]
        public void DuplicateSceneMapping_FailsSecondCell()
        {
            int a = m_Mgr.RegisterCell(0, 0, 0, Resolver.Shared);
            int b = m_Mgr.RegisterCell(0, 1, 0, Resolver.Shared);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(1, m_Scenes.LoadRequests.Count, "重复映射的第二个单元不得再次请求同一场景。");
            Assert.AreEqual(1, m_Mgr.TotalLoadFailures);
            Assert.IsTrue(m_Mgr.GetCellState(a) == StreamingCellState.Loading || m_Mgr.GetCellState(b) == StreamingCellState.Loading);
        }

        [Test]
        public void LateSuccessAfterCancel_TurnsIntoUnload()
        {
            int id = m_Mgr.RegisterCell(0, 0, 0, 7);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            m_Mgr.SetObserver(1, 500f, 500f);
            m_Mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Cancelling, m_Mgr.GetCellState(id));

            m_Scenes.CompleteLoad("World/L0_0_0");   // 场景加载不可中断：迟到成功
            CollectionAssert.AreEqual(new[] { "World/L0_0_0" }, m_Scenes.UnloadRequests);
            m_Scenes.CompleteUnload("World/L0_0_0");
            Assert.AreEqual(StreamingCellState.Unloaded, m_Mgr.GetCellState(id));
        }

        [Test]
        public void Dispose_UnsubscribesFromSceneEvents()
        {
            m_Mgr.RegisterCell(0, 0, 0, 7);
            m_Mgr.SetObserver(1, 5f, 5f);
            m_Mgr.Update(0f, 0f);
            m_Handler.Dispose();
            Assert.AreEqual(0, m_Scenes.SubscriberCount);
            Assert.DoesNotThrow(() => m_Scenes.CompleteLoad("World/L0_0_0"));
        }

        // ================================================================
        //  doubles
        // ================================================================

        private sealed class Resolver : SceneStreamingHandler.ISceneNameResolver
        {
            public const int NoScene = -1;
            public const int Shared = -2;

            public string Resolve(int layer, int cx, int cz, int contentKey)
            {
                if (contentKey == NoScene) return null;
                if (contentKey == Shared) return "World/Shared";
                return "World/L" + layer + "_" + cx + "_" + cz;
            }
        }

        private sealed class FakeSceneManager : ISceneManager
        {
            public readonly List<string> LoadRequests = new List<string>();
            public readonly List<string> UnloadRequests = new List<string>();

            private EventHandler<LoadSceneSuccessEventArgs> m_LoadSuccess;
            private EventHandler<LoadSceneFailureEventArgs> m_LoadFailure;
            private EventHandler<LoadSceneUpdateEventArgs> m_LoadUpdate;
            private EventHandler<UnloadSceneSuccessEventArgs> m_UnloadSuccess;
            private EventHandler<UnloadSceneFailureEventArgs> m_UnloadFailure;

            public int SubscriberCount
            {
                get
                {
                    return (m_LoadSuccess == null ? 0 : m_LoadSuccess.GetInvocationList().Length)
                         + (m_LoadFailure == null ? 0 : m_LoadFailure.GetInvocationList().Length)
                         + (m_UnloadSuccess == null ? 0 : m_UnloadSuccess.GetInvocationList().Length)
                         + (m_UnloadFailure == null ? 0 : m_UnloadFailure.GetInvocationList().Length);
                }
            }

            public event EventHandler<LoadSceneSuccessEventArgs> LoadSceneSuccess { add { m_LoadSuccess += value; } remove { m_LoadSuccess -= value; } }
            public event EventHandler<LoadSceneFailureEventArgs> LoadSceneFailure { add { m_LoadFailure += value; } remove { m_LoadFailure -= value; } }
            public event EventHandler<LoadSceneUpdateEventArgs> LoadSceneUpdate { add { m_LoadUpdate += value; } remove { m_LoadUpdate -= value; } }
            public event EventHandler<UnloadSceneSuccessEventArgs> UnloadSceneSuccess { add { m_UnloadSuccess += value; } remove { m_UnloadSuccess -= value; } }
            public event EventHandler<UnloadSceneFailureEventArgs> UnloadSceneFailure { add { m_UnloadFailure += value; } remove { m_UnloadFailure -= value; } }

            public void CompleteLoad(string scene) { var e = LoadSceneSuccessEventArgs.Create(scene, 0f, null); m_LoadSuccess?.Invoke(this, e); }
            public void FailLoad(string scene) { var e = LoadSceneFailureEventArgs.Create(scene, "fail", null); m_LoadFailure?.Invoke(this, e); }
            public void CompleteUnload(string scene) { var e = UnloadSceneSuccessEventArgs.Create(scene, null); m_UnloadSuccess?.Invoke(this, e); }

            public bool SceneIsLoaded(string sceneAssetName) { return false; }
            public string[] GetLoadedSceneAssetNames() { return Array.Empty<string>(); }
            public bool SceneIsLoading(string sceneAssetName) { return false; }
            public string[] GetLoadingSceneAssetNames() { return Array.Empty<string>(); }
            public bool SceneIsUnloading(string sceneAssetName) { return false; }
            public string[] GetUnloadingSceneAssetNames() { return Array.Empty<string>(); }
            public void LoadScene(string sceneAssetName, int priority, object userData) { LoadRequests.Add(sceneAssetName); }
            public void UnloadScene(string sceneAssetName, object userData) { UnloadRequests.Add(sceneAssetName); }
            public void SetHelper(ISceneHelper sceneHelper) { }
        }
    }
}
