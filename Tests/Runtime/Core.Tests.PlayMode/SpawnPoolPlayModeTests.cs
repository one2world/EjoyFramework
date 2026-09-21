//------------------------------------------------------------
// EjoyGame Framework Tests (PlayMode)
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.ObjectPool;
using EjoyFramework.Core.Unity;

namespace EjoyFramework.Tests.PlayMode
{
    /// <summary>
    /// WS1-M2 SpawnPool 专业化：条目热路径 / 预热 / MaxIdle / 过期 / 收缩 / 外部销毁计数 / Clear 语义 / 零分配。
    /// prefab 经 RegisterPrefab 直接登记，不依赖 ResourceManager。
    /// </summary>
    public sealed class SpawnPoolPlayModeTests : PlayModeTestBase
    {
        private SpawnPoolComponent m_Pool;
        private GameObject m_Prefab;

        public override void SetUp()
        {
            base.SetUp();
            GameObject host = CreateGameObject("SpawnPoolHost");
            host.AddComponent<BaseComponent>();
            m_Pool = host.AddComponent<SpawnPoolComponent>();

            // 用一个 inactive 的场景物体充当 prefab 模板（Instantiate 对二者语义一致）。
            m_Prefab = CreateGameObject("ProbePrefab");
            m_Prefab.AddComponent<PooledProbe>();
            m_Prefab.SetActive(false);
        }

        public override void TearDown()
        {
            // 场景卸载是异步的，下一个测试的 SetUp 会先于上一个组件的 OnDestroy 执行；
            // 同步销毁让 ComponentRegistry 立即注销（与 ComponentRegistryLifecycleTests 同一做法）。
            if (m_Pool != null)
            {
                Object.DestroyImmediate(m_Pool.gameObject);
                m_Pool = null;
            }

            base.TearDown();
        }

        // ================================================================
        //  主路径
        // ================================================================

        [Test]
        public void RegisterPrefab_Prewarm_SpawnDespawn_Cycle()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            Assert.IsTrue(entry.IsReady);
            Assert.AreEqual(3, m_Pool.Prewarm(entry, 3));
            Assert.AreEqual(3, entry.IdleCount);
            Assert.AreEqual(0, entry.SpawnedCount);

            Transform parent = CreateGameObject("Parent").transform;
            GameObject a = m_Pool.Spawn(entry, parent);
            GameObject b = m_Pool.Spawn(entry, parent);
            Assert.IsTrue(a.activeSelf && b.activeSelf);
            Assert.AreSame(parent, a.transform.parent);
            Assert.AreEqual(2, entry.SpawnedCount);
            Assert.AreEqual(1, entry.IdleCount);
            Assert.AreEqual(1, a.GetComponent<PooledProbe>().SpawnCalls);
            Assert.AreEqual(0, a.GetComponent<PooledProbe>().DespawnCalls);
            Assert.AreEqual("probe", a.GetComponent<SpawnPoolInstance>().PoolKey);

            m_Pool.Despawn(a);
            Assert.IsFalse(a.activeSelf);
            Assert.AreEqual(1, a.GetComponent<PooledProbe>().DespawnCalls);
            Assert.AreEqual(1, entry.SpawnedCount);
            Assert.AreEqual(2, entry.IdleCount);

            ObjectPoolMetrics m;
            entry.GetMetrics(out m);
            Assert.AreEqual(3, m.Count);
            Assert.AreEqual(3, m.PeakCount);
            Assert.AreEqual(2, m.TotalSpawnCount);
            Assert.AreEqual(1, m.TotalUnspawnCount);
            Assert.AreEqual(0, m.TotalReleaseCount);
            Assert.AreEqual(0, m.SpawnMissCount);
            Assert.AreEqual(3, entry.TotalInstantiatedCount, "预热 3 个后取出应全部复用，不再 Instantiate。");
        }

        [Test]
        public void Spawn_LifoReuse_ReturnsMostRecentlyDespawned()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            GameObject a = m_Pool.Spawn(entry);
            GameObject b = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            m_Pool.Despawn(b);

            Assert.AreSame(b, m_Pool.Spawn(entry), "最近归还的应最先被复用（缓存更热）。");
            Assert.AreSame(a, m_Pool.Spawn(entry));
        }

        [Test]
        public void Spawn_WithoutPrefab_ReturnsNull_AndCountsMiss()
        {
            SpawnPoolEntry entry = m_Pool.GetOrCreateEntry("missing");
            Assert.IsNull(m_Pool.Spawn(entry));
            ObjectPoolMetrics m;
            entry.GetMetrics(out m);
            Assert.AreEqual(1, m.SpawnMissCount);
        }

        // ================================================================
        //  容量 / 过期 / 收缩
        // ================================================================

        [UnityTest]
        public IEnumerator MaxIdle_DestroysOverflowOnDespawn()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            entry.MaxIdle = 1;
            GameObject a = m_Pool.Spawn(entry);
            GameObject b = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            m_Pool.Despawn(b);

            Assert.AreEqual(1, entry.IdleCount);
            yield return null;   // Destroy 在帧末生效
            Assert.IsTrue(a != null, "先归还的进池。");
            Assert.IsTrue(b == null, "超出 MaxIdle 的归还应被销毁。");

            ObjectPoolMetrics m;
            entry.GetMetrics(out m);
            Assert.AreEqual(1, m.TotalReleaseCount);
        }

        [Test]
        public void Prewarm_BeyondMaxIdle_ThrowsWithActionableMessage()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            entry.MaxIdle = 2;
            FrameworkException ex = Assert.Throws<FrameworkException>(() => m_Pool.Prewarm(entry, 3));
            StringAssert.Contains("MaxIdle", ex.Message);
            Assert.AreEqual(0, entry.Count);
        }

        [UnityTest]
        public IEnumerator Trim_DestroysOldestIdle_KeepsMostRecent()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            GameObject a = m_Pool.Spawn(entry);
            GameObject b = m_Pool.Spawn(entry);
            GameObject c = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            m_Pool.Despawn(b);
            m_Pool.Despawn(c);   // 空闲顺序：a(最久) b c(最新)

            Assert.AreEqual(2, m_Pool.Trim(entry, 1));
            Assert.AreEqual(1, entry.IdleCount);
            yield return null;
            Assert.IsTrue(a == null && b == null, "最久空闲的两个应被销毁。");
            Assert.IsTrue(c != null, "最新归还的应保留。");
            Assert.AreEqual(0, m_Pool.TrimAll(1));
        }

        [UnityTest]
        public IEnumerator ExpireTime_SweepDestroysStaleIdle()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            entry.ExpireTime = 0.05f;
            m_Pool.SweepInterval = 0f;
            GameObject a = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            Assert.AreEqual(1, entry.IdleCount);

            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < 0.3f)
            {
                yield return null;
            }

            Assert.AreEqual(0, entry.IdleCount, "超过 ExpireTime 的空闲实例应被周期清扫销毁。");
            yield return null;
            Assert.IsTrue(a == null);
        }

        // ================================================================
        //  健壮性
        // ================================================================

        [Test]
        public void DoubleDespawn_DoesNotChangeState()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            GameObject a = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            m_Pool.Despawn(a);

            Assert.AreEqual(1, entry.IdleCount);
            Assert.AreEqual(0, entry.SpawnedCount);
            Assert.AreEqual(1, a.GetComponent<PooledProbe>().DespawnCalls, "重复归还不得再次触发 OnDespawn。");
            ObjectPoolMetrics m;
            entry.GetMetrics(out m);
            Assert.AreEqual(1, m.TotalUnspawnCount);
        }

        [UnityTest]
        public IEnumerator ExternalDestroy_OfSpawnedInstance_KeepsCountsTruthful()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            GameObject a = m_Pool.Spawn(entry);
            GameObject b = m_Pool.Spawn(entry);
            Object.Destroy(a);
            yield return null;

            Assert.AreEqual(1, entry.SpawnedCount, "被外部销毁的在用实例应从计数中摘除。");
            ObjectPoolMetrics m;
            entry.GetMetrics(out m);
            Assert.AreEqual(1, m.TotalReleaseCount);
            m_Pool.Despawn(b);
            Assert.AreEqual(1, entry.IdleCount);
        }

        [UnityTest]
        public IEnumerator ExternalDestroy_OfIdleInstance_RemovesItFromIdleList()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            GameObject a = m_Pool.Spawn(entry);
            m_Pool.Despawn(a);
            Object.Destroy(a);
            yield return null;

            Assert.AreEqual(0, entry.IdleCount);
            GameObject b = m_Pool.Spawn(entry);
            Assert.IsTrue(b != null && b != a, "空闲列表里不应残留已销毁实例。");
        }

        [UnityTest]
        public IEnumerator Clear_ReleasesPrefab_InWildDespawnDestroys_ReregisterRestores()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            m_Pool.Prewarm(entry, 2);
            GameObject inWild = m_Pool.Spawn(entry);

            m_Pool.Clear("probe");
            Assert.IsFalse(entry.IsReady);
            Assert.AreEqual(0, entry.IdleCount);
            Assert.AreEqual(1, entry.SpawnedCount);

            m_Pool.Despawn(inWild);
            Assert.AreEqual(0, entry.IdleCount, "prefab 已清空，在外实例归还时应销毁而非入池。");
            yield return null;
            Assert.IsTrue(inWild == null);

            m_Pool.RegisterPrefab("probe", m_Prefab);
            Assert.IsTrue(entry.IsReady);
            Assert.IsNotNull(m_Pool.Spawn(entry));
        }

        [Test]
        public void Despawn_NonPooledObject_IsTolerated()
        {
            GameObject stray = CreateGameObject("Stray");
            Assert.DoesNotThrow(() => m_Pool.Despawn(stray));
        }

        [Test]
        public void GetAllEntries_ListsEveryKey()
        {
            m_Pool.RegisterPrefab("a", m_Prefab);
            m_Pool.GetOrCreateEntry("b");
            var entries = new List<SpawnPoolEntry>();
            m_Pool.GetAllEntries(entries);
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual(2, m_Pool.EntryCount);
        }

        // ================================================================
        //  零分配
        // ================================================================

        [Test]
        public void SpawnDespawnCycle_WithCachedEntry_DoesNotAllocate()
        {
            SpawnPoolEntry entry = m_Pool.RegisterPrefab("probe", m_Prefab);
            m_Pool.Prewarm(entry, 4);
            Transform parent = CreateGameObject("Parent").transform;
            for (int i = 0; i < 8; i++)
            {
                GameObject go = m_Pool.Spawn(entry, parent);
                m_Pool.Despawn(go);
            }

            Assert.That(() =>
            {
                for (int i = 0; i < 32; i++)
                {
                    GameObject go = m_Pool.Spawn(entry, parent);
                    m_Pool.Despawn(go);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        private sealed class PooledProbe : MonoBehaviour, ISpawnCallback
        {
            public int SpawnCalls;
            public int DespawnCalls;

            public void OnSpawn() { SpawnCalls++; }
            public void OnDespawn() { DespawnCalls++; }
        }
    }
}
