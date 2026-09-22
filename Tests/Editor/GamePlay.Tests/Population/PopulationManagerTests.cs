//------------------------------------------------------------
// EjoyGame Framework Tests — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Serialization;
using EjoyFramework.Core.Streaming;
using EjoyFramework.GamePlay.Population;

namespace EjoyFramework.GamePlay.Tests.Population
{
    /// <summary>WS3-M3：种群管理——单元加载/卸载生成回收、击杀与复活、原型预算、持久化往返、与流送链整体联动、零分配。</summary>
    public sealed class PopulationManagerTests
    {
        private sealed class RecordingSpawner : IPopulationSpawner
        {
            public int NextInstance = 1;
            public int Alive;
            public readonly List<int> SpawnedPoints = new List<int>();
            public bool Fail;

            public int Spawn(int spawnPointId, int archetype, float x, float z, int cellId)
            {
                if (Fail) return 0;
                SpawnedPoints.Add(spawnPointId);
                Alive++;
                return NextInstance++;
            }

            public void Despawn(int instance, int spawnPointId) { Alive--; }
        }

        private RecordingSpawner m_Spawner;
        private PopulationManager m_Pop;

        [SetUp]
        public void SetUp()
        {
            m_Spawner = new RecordingSpawner();
            m_Pop = new PopulationManager(m_Spawner);
        }

        [Test]
        public void CellLoad_SpawnsPoints_CellUnload_DespawnsThem()
        {
            int a = m_Pop.AddSpawnPoint(cellId: 1, archetype: 3, x: 1f, z: 2f, respawnSeconds: 10f);
            int b = m_Pop.AddSpawnPoint(1, 3, 5f, 5f, 10f);
            Assert.AreEqual(0, m_Pop.AliveCount, "未加载的单元不生成。");

            m_Pop.OnCellLoaded(1);
            Assert.AreEqual(2, m_Pop.AliveCount);
            Assert.AreEqual(2, m_Pop.GetAliveCount(3));
            CollectionAssert.AreEquivalent(new[] { a, b }, m_Spawner.SpawnedPoints);

            m_Pop.OnCellUnloaded(1);
            Assert.AreEqual(0, m_Pop.AliveCount);
            Assert.AreEqual(0, m_Spawner.Alive);

            m_Pop.OnCellLoaded(1);
            Assert.AreEqual(2, m_Pop.AliveCount, "再次加载存活点应重新生成。");
        }

        [Test]
        public void Kill_ThenRespawnAfterTimer_OnlyWhileLoaded()
        {
            int p = m_Pop.AddSpawnPoint(1, 0, 0f, 0f, respawnSeconds: 5f);
            m_Pop.OnCellLoaded(1);
            m_Pop.NotifyKilled(p);
            Assert.AreEqual(0, m_Pop.AliveCount);
            Assert.AreEqual(1, m_Pop.TotalKilled);

            m_Pop.Update(3f);
            Assert.AreEqual(0, m_Pop.AliveCount);
            m_Pop.OnCellUnloaded(1);
            m_Pop.Update(100f);   // 卸载期间时间不流逝
            m_Pop.OnCellLoaded(1);
            Assert.AreEqual(0, m_Pop.AliveCount, "剩余 2 秒未到，重新加载后仍是死亡态。");
            SpawnPointInfo info;
            Assert.IsTrue(m_Pop.TryGetSpawnPoint(p, out info));
            Assert.IsTrue(info.Dead);
            Assert.AreEqual(2f, info.RespawnRemaining, 1e-4f);

            m_Pop.Update(2.5f);
            Assert.AreEqual(1, m_Pop.AliveCount, "倒计时到期应复活。");
        }

        [Test]
        public void RespawnZero_NeverRespawns()
        {
            int p = m_Pop.AddSpawnPoint(1, 0, 0f, 0f, respawnSeconds: 0f);
            m_Pop.OnCellLoaded(1);
            m_Pop.NotifyKilled(p);
            m_Pop.Update(1000f);
            Assert.AreEqual(0, m_Pop.AliveCount);
        }

        [Test]
        public void ArchetypeBudget_CapsAlive_AndBackfillsOnDespawn()
        {
            m_Pop.SetArchetypeBudget(7, 2);
            int a = m_Pop.AddSpawnPoint(1, 7, 0f, 0f, 1f);
            int b = m_Pop.AddSpawnPoint(1, 7, 1f, 0f, 1f);
            int c = m_Pop.AddSpawnPoint(1, 7, 2f, 0f, 1f);
            m_Pop.OnCellLoaded(1);
            Assert.AreEqual(2, m_Pop.GetAliveCount(7));

            m_Pop.NotifyKilled(a);   // 立即腾出预算；c 在下一次 Update 补上
            m_Pop.Update(0.01f);
            Assert.AreEqual(2, m_Pop.GetAliveCount(7));
            SpawnPointInfo info;
            m_Pop.TryGetSpawnPoint(c, out info);
            Assert.Greater(info.Instance, 0, "被预算压住的点应在有空位后补生成。");
        }

        [Test]
        public void SpawnFailure_LeavesPointUnspawned_AndRetriesLater()
        {
            m_Spawner.Fail = true;
            m_Pop.AddSpawnPoint(1, 0, 0f, 0f, 1f);
            m_Pop.OnCellLoaded(1);
            Assert.AreEqual(0, m_Pop.AliveCount);
            m_Spawner.Fail = false;
            m_Pop.Update(0.01f);
            Assert.AreEqual(1, m_Pop.AliveCount);
        }

        [Test]
        public void RemoveSpawnPointsOfCell_DespawnsAndForgets()
        {
            int p = m_Pop.AddSpawnPoint(1, 0, 0f, 0f, 1f);
            m_Pop.OnCellLoaded(1);
            m_Pop.RemoveSpawnPointsOfCell(1);
            Assert.AreEqual(0, m_Pop.AliveCount);
            Assert.AreEqual(0, m_Pop.SpawnPointCount);
            SpawnPointInfo info;
            Assert.IsFalse(m_Pop.TryGetSpawnPoint(p, out info));
        }

        [Test]
        public void Persistence_RoundTrip_ByLocalIndex()
        {
            int a = m_Pop.AddSpawnPoint(1, 0, 0f, 0f, 10f);
            int b = m_Pop.AddSpawnPoint(1, 0, 1f, 0f, 10f);
            m_Pop.OnCellLoaded(1);
            m_Pop.NotifyKilled(b);
            m_Pop.Update(4f);   // b 剩 6 秒

            ByteBuffer buffer = ByteBuffer.Acquire();
            m_Pop.Capture(1, 0, 0, 0, 0, buffer);
            Assert.Greater(buffer.Length, 0);

            // 新会话：重新登记同样布局的生成点，再恢复
            var spawner2 = new RecordingSpawner();
            var pop2 = new PopulationManager(spawner2);
            pop2.AddSpawnPoint(1, 0, 0f, 0f, 10f);
            int b2 = pop2.AddSpawnPoint(1, 0, 1f, 0f, 10f);
            pop2.Restore(1, 0, 0, 0, 0, buffer);
            pop2.OnCellLoaded(1);
            Assert.AreEqual(1, pop2.AliveCount, "恢复后 b 仍是死亡态，只生成 a。");
            SpawnPointInfo info;
            pop2.TryGetSpawnPoint(b2, out info);
            Assert.IsTrue(info.Dead);
            Assert.AreEqual(6f, info.RespawnRemaining, 1e-4f);
            buffer.Release();
        }

        [Test]
        public void Persistence_AllAlive_WritesNothing()
        {
            m_Pop.AddSpawnPoint(1, 0, 0f, 0f, 10f);
            m_Pop.OnCellLoaded(1);
            ByteBuffer buffer = ByteBuffer.Acquire();
            m_Pop.Capture(1, 0, 0, 0, 0, buffer);
            Assert.AreEqual(0, buffer.Length, "全部存活是默认态，不应产生记录。");
            buffer.Release();
        }

        [Test]
        public void FullChain_StreamingPersistencePopulation_KilledEnemyStaysDeadAcrossReload()
        {
            Framework.MarkMainThread();
            var mgr = new WorldStreamingManager();
            mgr.CellSize = 10f;
            mgr.ConfigureLayer(0, new StreamingLayerSettings { LoadRadius = 25f, UnloadRadius = 35f });
            mgr.MaxLoadStartsPerFrame = 100; mgr.MaxLoadsInFlight = 100; mgr.MaxUnloadsPerFrame = 100;
            var store = new WorldStateStore();

            // 回报链：echo → persistence(Restore) → population(OnCellLoaded) → manager
            var population = new PopulationStreamingHandler(mgr, mgr, m_Pop);
            var persistence = new PersistentStreamingHandler(mgr, population, store, m_Pop);
            var echo = new EchoHandler(persistence);
            // handler 链：manager → persistence → population → echo
            persistence.Inner = population;
            population.Inner = echo;
            mgr.SetHandler(persistence);

            int cell = mgr.RegisterCell(0, 0, 0, 1);
            int p = m_Pop.AddSpawnPoint(cell, 0, 3f, 3f, respawnSeconds: 60f);

            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(1, m_Pop.AliveCount, "单元加载后应生成。");

            m_Pop.NotifyKilled(p);
            mgr.SetObserver(1, 500f, 500f);
            mgr.Update(0f, 0f);
            Assert.IsTrue(store.HasCell(0, 0, 0), "离开时死亡状态应被采集。");

            mgr.SetObserver(1, 5f, 5f);
            mgr.Update(0f, 0f);
            Assert.AreEqual(StreamingCellState.Loaded, mgr.GetCellState(cell));
            Assert.AreEqual(0, m_Pop.AliveCount, "回来后被击杀的敌人不应复活（先恢复再生成）。");
            mgr.Shutdown();
        }

        [Test]
        public void LoadUnloadCycle_SteadyState_DoesNotAllocate()
        {
            for (int i = 0; i < 32; i++) m_Pop.AddSpawnPoint(1, i % 4, i, i, 1f);
            TestDelegate body = () =>
            {
                m_Pop.OnCellLoaded(1);
                m_Pop.Update(0.5f);
                m_Pop.OnCellUnloaded(1);
            };
            body();
            body();
            m_Spawner.SpawnedPoints.Clear();
            m_Spawner.SpawnedPoints.Capacity = 64;
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        private sealed class EchoHandler : IWorldStreamingHandler
        {
            private readonly IWorldStreamingNotifier m_Notifier;
            public EchoHandler(IWorldStreamingNotifier notifier) { m_Notifier = notifier; }
            public void BeginLoad(int cellId, int layer, int cx, int cz, int contentKey, int lod) { m_Notifier.NotifyLoaded(cellId, true); }
            public void CancelLoad(int cellId) { }
            public void BeginUnload(int cellId, int layer, int cx, int cz, int contentKey) { m_Notifier.NotifyUnloaded(cellId); }
            public void OnLodChanged(int cellId, int fromLod, int toLod) { }
        }
    }
}
