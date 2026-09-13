//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.ObjectPool;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// ObjectPoolManager 单测（Phase 17）。
    /// 覆盖：CreateMultiSpawn / CreateSingleSpawn / 重复创建抛错 /
    ///       Register-Spawn-Unspawn 配对 / Capacity / ExpireTime 自动释放 /
    ///       AllowMultiSpawn 行为差异 / DestroyObjectPool / ReleaseAllUnused /
    ///       GetAllObjectPools 调试接口。
    /// </summary>
    public class ObjectPoolManagerTests
    {
        private ObjectPoolManager m_OPM;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_OPM = new ObjectPoolManager();
        }

        [TearDown]
        public void TearDown()
        {
            m_OPM.Shutdown();
        }

        [Test]
        public void CreateMultiSpawnPool_RegistersAndIsRetrievable()
        {
            // 注意：HasObjectPool<T>() 只查"无名 (T, '') key"，命名池得通过 GetAllObjectPools 验证
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("test", 60f, 16, 60f, 0);
            Assert.IsNotNull(pool);
            Assert.AreEqual("test", pool.Name);
            Assert.IsTrue(pool.AllowMultiSpawn);
            Assert.AreEqual(1, m_OPM.Count);
            var all = m_OPM.GetAllObjectPools();
            Assert.AreEqual(1, all.Length);
            Assert.AreSame(pool, all[0]);
        }

        [Test]
        public void HasAndGetObjectPool_FindUnnamedPoolByType()
        {
            // 无名池可通过 HasObjectPool<T>() / GetObjectPool<T>() 查询
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>(string.Empty, 60f, 16, 60f, 0);
            Assert.IsTrue(m_OPM.HasObjectPool<FakeObj>());
            Assert.AreSame(pool, m_OPM.GetObjectPool<FakeObj>());
        }

        [Test]
        public void CreateSingleSpawnPool_DisallowsMultiSpawn()
        {
            var pool = m_OPM.CreateSingleSpawnObjectPool<FakeObj>("single", 60f, 16, 60f, 0);
            Assert.IsFalse(pool.AllowMultiSpawn);
        }

        [Test]
        public void Count_TracksPoolCreation()
        {
            Assert.AreEqual(0, m_OPM.Count);
            m_OPM.CreateMultiSpawnObjectPool<FakeObj>("a", 60f, 16, 60f, 0);
            Assert.AreEqual(1, m_OPM.Count);
        }

        [Test]
        public void Register_Then_Spawn_MovesObjectOutOfReleasable()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 16, 60f, 0);
            var obj = FakeObj.Make("asset1");
            pool.Register(obj, /*spawned*/ false);

            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(1, pool.CanReleaseCount, "未 spawn 的对象可释放");
            Assert.IsTrue(pool.CanSpawn("asset1"));

            var spawned = pool.Spawn("asset1");
            Assert.IsNotNull(spawned);
            Assert.AreSame(obj, spawned);
            Assert.AreEqual(0, pool.CanReleaseCount, "spawn 后对象不可释放");
        }

        [Test]
        public void Unspawn_MovesObjectBackToReleasable()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 16, 60f, 0);
            var obj = FakeObj.Make("asset");
            pool.Register(obj, /*spawned*/ true);
            Assert.AreEqual(0, pool.CanReleaseCount);

            pool.Unspawn(obj.Target);
            Assert.AreEqual(1, pool.CanReleaseCount);
        }

        [Test]
        public void MultiSpawn_ReturnsSameInstance_ForRepeatedSpawn()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 16, 60f, 0);
            var obj = FakeObj.Make("a");
            pool.Register(obj, false);

            var s1 = pool.Spawn("a");
            var s2 = pool.Spawn("a");
            Assert.AreSame(s1, s2);
            // MultiSpawn 模式 spawn 2 次仍可继续 spawn
            Assert.IsTrue(pool.CanSpawn("a"));
        }

        [Test]
        public void SingleSpawn_NotAvailable_AfterFirstSpawn()
        {
            var pool = m_OPM.CreateSingleSpawnObjectPool<FakeObj>("p", 60f, 16, 60f, 0);
            var obj = FakeObj.Make("a");
            pool.Register(obj, false);

            Assert.IsTrue(pool.CanSpawn("a"));
            var first = pool.Spawn("a");
            Assert.IsNotNull(first);
            // SingleSpawn 出 1 次后该对象被锁住
            Assert.IsFalse(pool.CanSpawn("a"));
        }

        [Test]
        public void DestroyObjectPool_RemovesUnnamedPool()
        {
            // DestroyObjectPool<T>() 同样按"无名 (T, '') key"销毁，需用空 name 创建
            m_OPM.CreateMultiSpawnObjectPool<FakeObj>(string.Empty, 60f, 16, 60f, 0);
            Assert.AreEqual(1, m_OPM.Count);
            Assert.IsTrue(m_OPM.DestroyObjectPool<FakeObj>());
            Assert.AreEqual(0, m_OPM.Count);
            Assert.IsFalse(m_OPM.HasObjectPool<FakeObj>());
        }

        [Test]
        public void GetAllObjectPools_ReturnsAllRegistered()
        {
            m_OPM.CreateMultiSpawnObjectPool<FakeObj>("a", 60f, 16, 60f, 0);
            m_OPM.CreateMultiSpawnObjectPool<FakeObj2>("b", 60f, 16, 60f, 0);
            var all = m_OPM.GetAllObjectPools();
            Assert.AreEqual(2, all.Length);
        }

        [Test]
        public void ReleaseAllUnused_ReleasesUnspawnedObjects()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 1, 60f, 0);
            var obj = FakeObj.Make("a");
            pool.Register(obj, false);   // unspawned
            Assert.AreEqual(1, pool.Count);

            pool.ReleaseAllUnused();
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void Capacity_CanBeReadAndUpdated()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 4, 60f, 0);
            Assert.AreEqual(4, pool.Capacity);
            pool.Capacity = 8;
            Assert.AreEqual(8, pool.Capacity);
        }

        [Test]
        public void ExpireTime_CanBeReadAndUpdated()
        {
            var pool = m_OPM.CreateMultiSpawnObjectPool<FakeObj>("p", 60f, 16, 30f, 0);
            Assert.AreEqual(30f, pool.ExpireTime, 0.001f);
            pool.ExpireTime = 45f;
            Assert.AreEqual(45f, pool.ExpireTime, 0.001f);
        }

        [Test]
        public void Shutdown_ClearsAllPools()
        {
            m_OPM.CreateMultiSpawnObjectPool<FakeObj>("a", 60f, 16, 60f, 0);
            m_OPM.CreateMultiSpawnObjectPool<FakeObj2>("b", 60f, 16, 60f, 0);
            Assert.AreEqual(2, m_OPM.Count);
            m_OPM.Shutdown();
            Assert.AreEqual(0, m_OPM.Count);
        }

        /// <summary>测试用 ObjectBase 子类。</summary>
        private sealed class FakeObj : ObjectBase
        {
            public bool Released;
            public static FakeObj Make(string name)
            {
                var o = ReferencePool.Acquire<FakeObj>();
                o.Initialize(name, new object());
                return o;
            }
            protected internal override void Release(bool isShutdown) { Released = true; }
        }

        /// <summary>另一个类型，验证 ObjectPoolManager 按 T 区分 pool。</summary>
        private sealed class FakeObj2 : ObjectBase
        {
            public static FakeObj2 Make(string name)
            {
                var o = ReferencePool.Acquire<FakeObj2>();
                o.Initialize(name, new object());
                return o;
            }
            protected internal override void Release(bool isShutdown) { }
        }
    }
}
