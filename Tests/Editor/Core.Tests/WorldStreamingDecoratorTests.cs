//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Navigation;
using EjoyFramework.Core.Resource;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Streaming;
using EjoyFramework.Tests.TestSupport;

namespace EjoyFramework.Tests
{
    /// <summary>WS3-M3：WorldStateStore 序列化/事务性、持久化装饰器采集与恢复、navmesh 装饰器挂载与卸载。</summary>
    public sealed class WorldStreamingDecoratorTests
    {
        // ================================================================
        //  WorldStateStore
        // ================================================================

        [Test]
        public void Store_SetGetRemove_TracksBytesAndDirty()
        {
            var store = new WorldStateStore();
            byte[] payload = { 1, 2, 3, 4, 5 };
            store.SetCell(0, -3, 7, payload, 1, 3);
            Assert.AreEqual(1, store.CellCount);
            Assert.AreEqual(3, store.TotalBytes);
            Assert.IsTrue(store.IsDirty);

            byte[] buffer; int length;
            Assert.IsTrue(store.TryGetCell(0, -3, 7, out buffer, out length));
            Assert.AreEqual(3, length);
            Assert.AreEqual(2, buffer[0]);
            Assert.AreEqual(4, buffer[2]);

            store.MarkClean();
            store.SetCell(0, -3, 7, payload, 0, 0);   // 0 字节 = 删除
            Assert.AreEqual(0, store.CellCount);
            Assert.IsTrue(store.IsDirty);
            Assert.IsFalse(store.HasCell(0, -3, 7));
        }

        [Test]
        public void Store_RoundTrip_PreservesCellsGlobalsAndNegativeCoords()
        {
            var store = new WorldStateStore();
            store.SetCell(2, -100000, 99999, new byte[] { 9, 8, 7 }, 0, 3);
            store.SetCell(0, 0, 0, new byte[] { 1 }, 0, 1);
            store.SetGlobal(42, new byte[] { 5, 5 }, 0, 2);

            byte[] bytes = store.ToArray();
            var loaded = new WorldStateStore();
            loaded.Load(bytes);

            Assert.AreEqual(2, loaded.CellCount);
            Assert.AreEqual(1, loaded.GlobalCount);
            Assert.AreEqual(6, loaded.TotalBytes);
            Assert.IsFalse(loaded.IsDirty);
            byte[] b; int n;
            Assert.IsTrue(loaded.TryGetCell(2, -100000, 99999, out b, out n));
            Assert.AreEqual(3, n);
            Assert.AreEqual(9, b[0]);
            Assert.IsTrue(loaded.TryGetGlobal(42, out b, out n));
            Assert.AreEqual(2, n);
        }

        [Test]
        public void Store_ReadFrom_CorruptData_ThrowsAndLeavesEmpty()
        {
            var store = new WorldStateStore();
            store.SetCell(0, 1, 1, new byte[] { 1 }, 0, 1);
            byte[] good = store.ToArray();
            byte[] bad = new byte[good.Length];
            Array.Copy(good, bad, good.Length);
            bad[11] = 0x7F;   // 篡改 cellCount 最高字节（偏移 8..11）→ 巨大计数，读取越界

            Assert.Throws<FrameworkException>(() => store.Load(bad));
            Assert.AreEqual(0, store.CellCount, "读入失败不得残留半读的记录。");
            Assert.Throws<FrameworkException>(() => store.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        [Test]
        public void Store_Overwrite_ReusesOrGrowsBuffer()
        {
            var store = new WorldStateStore();
            store.SetCell(0, 0, 0, new byte[4], 0, 4);
            byte[] first; int n;
            store.TryGetCell(0, 0, 0, out first, out n);
            store.SetCell(0, 0, 0, new byte[2], 0, 2);
            byte[] second;
            store.TryGetCell(0, 0, 0, out second, out n);
            Assert.AreSame(first, second, "更小的记录应复用原缓冲。");
            Assert.AreEqual(2, n);
            store.SetCell(0, 0, 0, new byte[4096], 0, 4096);
            byte[] third;
            store.TryGetCell(0, 0, 0, out third, out n);
            Assert.AreNotSame(first, third);
            Assert.AreEqual(4096, n);
            Assert.AreEqual(4096, store.TotalBytes);
        }

        // ================================================================
        //  PersistentStreamingHandler
        // ================================================================

        private WorldStreamingManager NewManager()
        {
            Framework.MarkMainThread();
            var mgr = new WorldStreamingManager();
            mgr.CellSize = 10f;
            mgr.ConfigureLayer(0, new StreamingLayerSettings { LoadRadius = 25f, UnloadRadius = 35f });
            mgr.MaxLoadStartsPerFrame = 100;
            mgr.MaxLoadsInFlight = 100;
            mgr.MaxUnloadsPerFrame = 100;
            return mgr;
        }

        [Test]
        public void Persistence_CapturesOnUnload_RestoresOnReload()
        {
            var mgr = NewManager();
            var store = new WorldStateStore();
            var serializer = new CounterSerializer();
            var persistence = new PersistentStreamingHandler(mgr, mgr, store, serializer);
            var inner = new EchoHandler(persistence);
            persistence.Inner = inner;
            mgr.SetHandler(persistence);
            int id = mgr.RegisterCell(0, 0, 0, 5);

            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Loaded, mgr.GetCellState(id));
            Assert.AreEqual(0, persistence.RestoreCount, "首次加载没有记录，不恢复。");

            serializer.Value = 77;   // 玩家在单元里做了事
            mgr.SetObserver(1, 500f, 500f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Unloaded, mgr.GetCellState(id));
            Assert.AreEqual(1, persistence.CaptureCount);
            Assert.IsTrue(store.HasCell(0, 0, 0));

            serializer.Value = 0;
            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Loaded, mgr.GetCellState(id));
            Assert.AreEqual(1, persistence.RestoreCount);
            Assert.AreEqual(77, serializer.Restored, "重新加载后应恢复离开时的状态。");
            mgr.Shutdown();
        }

        [Test]
        public void Persistence_ZeroByteCapture_RemovesRecord_AndExceptionsAreIsolated()
        {
            var mgr = NewManager();
            var store = new WorldStateStore();
            var serializer = new CounterSerializer { WriteNothing = true };
            var persistence = new PersistentStreamingHandler(mgr, mgr, store, serializer);
            persistence.Inner = new EchoHandler(persistence);
            mgr.SetHandler(persistence);
            int id = mgr.RegisterCell(0, 0, 0, 5);
            store.SetCell(0, 0, 0, new byte[] { 1 }, 0, 1);

            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            mgr.SetObserver(1, 500f, 500f);
            mgr.Update(0f, 0f);
            Assert.IsFalse(store.HasCell(0, 0, 0), "采集 0 字节应删除旧记录。");

            serializer.Throw = true;
            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            mgr.SetObserver(1, 500f, 500f);
            Assert.DoesNotThrow(() => mgr.Update(0f, 0f));
            Assert.AreEqual(StreamingCellState.Unloaded, mgr.GetCellState(id), "采集异常不得卡住卸载。");
            mgr.Shutdown();
        }

        [Test]
        public void Persistence_NoRestore_ForCancelledLateResult()
        {
            var mgr = NewManager();
            var store = new WorldStateStore();
            store.SetCell(0, 0, 0, new byte[] { 1, 2 }, 0, 2);
            var serializer = new CounterSerializer();
            var persistence = new PersistentStreamingHandler(mgr, mgr, store, serializer);
            var inner = new EchoHandler(persistence) { Manual = true };
            persistence.Inner = inner;
            mgr.SetHandler(persistence);
            int id = mgr.RegisterCell(0, 0, 0, 5);

            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            mgr.SetObserver(1, 500f, 500f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Cancelling, mgr.GetCellState(id));
            persistence.NotifyLoaded(id, true);   // 迟到结果
            Assert.AreEqual(0, persistence.RestoreCount, "取消后的迟到结果不应恢复状态。");
            mgr.Shutdown();
        }

        // ================================================================
        //  NavMeshStreamingHandler
        // ================================================================

        [Test]
        public void NavMesh_TileAddedAfterLoad_RemovedOnUnload_AssetReturned()
        {
            var mgr = NewManager();
            var nav = new NavigationManager();
            var navHelper = new TileNavHelper();
            nav.SetHelper(navHelper);
            var resources = new MockResourceManager();
            var navmesh = new NavMeshStreamingHandler(mgr, mgr, nav, resources, new NavResolver());
            var inner = new EchoHandler(navmesh);
            navmesh.Inner = inner;
            mgr.SetHandler(navmesh);
            int id = mgr.RegisterCell(0, 3, -2, 1);

            mgr.SetObserver(1, 35f, -15f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Loaded, mgr.GetCellState(id));
            Assert.AreEqual(1, nav.NavMeshTileCount);
            Assert.AreEqual(1, navmesh.TrackedTileCount);
            Assert.AreEqual(30f, navHelper.LastPosition.X, 1e-4f, "tile 应挂在单元原点 (cx*cellSize)。");
            Assert.AreEqual(-20f, navHelper.LastPosition.Z, 1e-4f);
            Assert.AreEqual("NavMesh/L0_3_-2", resources.LastAssetName);

            mgr.SetObserver(1, 500f, 500f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(0, nav.NavMeshTileCount);
            Assert.AreEqual(0, navmesh.TrackedTileCount);
            Assert.AreEqual(1, resources.UnloadCalls, "卸载单元时 navmesh 资产应归还。");
            nav.Shutdown();
            mgr.Shutdown();
        }

        [Test]
        public void NavMesh_ResolverNull_SkipsTile()
        {
            var mgr = NewManager();
            var nav = new NavigationManager();
            nav.SetHelper(new TileNavHelper());
            var resources = new MockResourceManager();
            var navmesh = new NavMeshStreamingHandler(mgr, mgr, nav, resources, new NavResolver { None = true });
            navmesh.Inner = new EchoHandler(navmesh);
            mgr.SetHandler(navmesh);
            mgr.RegisterCell(0, 0, 0, 1);
            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(0, nav.NavMeshTileCount);
            Assert.IsNull(resources.LastAssetName);
            nav.Shutdown();
            mgr.Shutdown();
        }

        // ================================================================
        //  doubles
        // ================================================================

        private sealed class CounterSerializer : ICellStateSerializer
        {
            public int Value;
            public int Restored;
            public bool WriteNothing;
            public bool Throw;

            public void Capture(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer)
            {
                if (Throw) throw new InvalidOperationException("capture boom");
                if (WriteNothing) return;
                buffer.WriteInt(Value);
            }

            public void Restore(int cellId, int layer, int cx, int cz, int contentKey, ByteBuffer buffer)
            {
                Restored = buffer.ReadInt();
            }
        }

        /// <summary>同步完成的内层 handler：把回报交给链头（装饰器）。Manual 时不自动完成。</summary>
        private sealed class EchoHandler : IWorldStreamingHandler
        {
            private readonly IWorldStreamingNotifier m_Notifier;
            public bool Manual;
            public EchoHandler(IWorldStreamingNotifier notifier) { m_Notifier = notifier; }
            public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod) { if (!Manual) m_Notifier.NotifyLoaded(cellId, true); }
            public void CancelLoad(int cellId) { }
            public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey) { m_Notifier.NotifyUnloaded(cellId); }
            public void OnLodChanged(int cellId, int fromLod, int toLod) { }
        }

        private sealed class NavResolver : NavMeshStreamingHandler.INavMeshAssetResolver
        {
            public bool None;
            public string Resolve(int layer, int cx, int cz, int contentKey) { return None ? null : "NavMesh/L" + layer + "_" + cx + "_" + cz; }
        }

        private sealed class TileNavHelper : INavigationHelper
        {
            public Vector3Lite LastPosition;
            public object AddNavMeshData(object navMeshData, Vector3Lite position) { LastPosition = position; return new object(); }
            public void RemoveNavMeshData(object helperHandle) { }
            public void CalculatePath(Vector3Lite from, Vector3Lite to, int areaMask, Action<NavPathResult> onComplete) { }
            public object CreateAgent(NavAgentConfig config) { return new object(); }
            public void DestroyAgent(object helperHandle) { }
            public void SetAgentDestination(object helperHandle, Vector3Lite destination) { }
            public void WarpAgent(object helperHandle, Vector3Lite position) { }
            public Vector3Lite GetAgentPosition(object helperHandle) { return default(Vector3Lite); }
            public bool HasReachedDestination(object helperHandle) { return true; }
            public void StopAgent(object helperHandle) { }
        }

    }
}
