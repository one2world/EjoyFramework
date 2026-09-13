//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Entity;
using EjoyFramework.Core.ObjectPool;
using EjoyFramework.Tests.TestSupport;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// EntityManager 完整流程测试。
    /// 验证：异步加载成功/失败、实例池复用、加载中取消、HideAllLoaded、事件 args 字段。
    /// </summary>
    public class EntityManagerTests
    {
        private sealed class FakeInstance { public string Name; public bool Released; }

        private sealed class FakeEntity : IEntity
        {
            public int Id { get; set; }
            public string EntityAssetName { get; set; }
            public object Handle { get; set; }
            public IEntityGroup EntityGroup { get; set; }
        }

        private sealed class FakeEntityHelper : IEntityHelper
        {
            public int InstantiateCount, CreateCount, ReleaseCount, HideCount, UpdateCount, AttachCount, DetachCount;

            public object InstantiateEntity(object asset)
            {
                InstantiateCount++;
                var ma = asset as MockAsset;
                return new FakeInstance { Name = ma != null ? ma.Name : "?" };
            }

            public IEntity CreateEntity(object instance, IEntityGroup group, object userData)
            {
                CreateCount++;
                var fi = instance as FakeInstance;
                return new FakeEntity { Id = 0, EntityAssetName = fi != null ? fi.Name : null, Handle = instance, EntityGroup = group };
            }

            public void HideEntity(IEntity entity, bool isShutdown, object userData) { HideCount++; }
            public void UpdateEntity(IEntity entity, float elapseSeconds, float realElapseSeconds) { UpdateCount++; }
            public void AttachEntity(IEntity child, IEntity parent, object userData) { AttachCount++; }
            public void DetachEntity(IEntity child, IEntity parent, object userData) { DetachCount++; }

            public void ReleaseEntity(object asset, object instance)
            {
                ReleaseCount++;
                var fi = instance as FakeInstance;
                if (fi != null) fi.Released = true;
            }
        }

        private EntityManager m_EM;
        private ObjectPoolManager m_OP;
        private MockResourceManager m_Res;
        private FakeEntityHelper m_Helper;

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
            m_OP = new ObjectPoolManager();
            m_Res = new MockResourceManager();
            m_Helper = new FakeEntityHelper();

            m_EM = new EntityManager(m_Res, m_OP);
            m_EM.SetEntityHelper(m_Helper);
            m_EM.AddEntityGroup("Default", 60f, 16, 60f, 0, null);
        }

        [TearDown]
        public void Teardown()
        {
            m_EM.Shutdown();
            m_OP.Shutdown();
        }

        [Test]
        public void ShowEntity_SyncSuccess_FiresEventWithFilledArgs()
        {
            int capturedId = -1;
            string capturedAsset = null;
            IEntityGroup capturedGroup = null;
            float capturedDuration = -1f;
            IEntity capturedEntity = null;

            m_EM.ShowEntitySuccess += (s, e) =>
            {
                capturedId = e.EntityId;
                capturedAsset = e.EntityAssetName;
                capturedGroup = e.EntityGroup;
                capturedDuration = e.Duration;
                capturedEntity = e.Entity;
            };

            m_EM.ShowEntity(42, "Entities/Player.prefab", "Default", 0, null);

            Assert.AreEqual(42, capturedId);
            Assert.AreEqual("Entities/Player.prefab", capturedAsset);
            Assert.IsNotNull(capturedGroup);
            Assert.AreEqual("Default", capturedGroup.Name);
            Assert.GreaterOrEqual(capturedDuration, 0f);
            Assert.IsNotNull(capturedEntity);
            Assert.IsTrue(m_EM.HasEntity(42));
            Assert.AreEqual(1, m_EM.EntityCount);
        }

        [Test]
        public void ShowEntity_LoadFailure_FiresFailureEvent()
        {
            m_Res.SyncSucceed = false;
            int capturedId = -1;
            string capturedAsset = null;
            string capturedError = null;
            m_EM.ShowEntityFailure += (s, e) =>
            {
                capturedId = e.EntityId;
                capturedAsset = e.EntityAssetName;
                capturedError = e.ErrorMessage;
            };

            m_EM.ShowEntity(1, "Entities/Bad.prefab", "Default", 0, null);

            Assert.AreEqual(1, capturedId);
            Assert.AreEqual("Entities/Bad.prefab", capturedAsset);
            StringAssert.Contains("fake load failed", capturedError);
            Assert.IsFalse(m_EM.HasEntity(1));
        }

        [Test]
        public void ShowEntity_ReuseFromInstancePool_NoSecondLoad()
        {
            m_EM.ShowEntity(1, "Entities/Player.prefab", "Default", 0, null);
            m_EM.HideEntity(1);
            int instantiateCountAfterFirst = m_Helper.InstantiateCount;

            m_EM.ShowEntity(2, "Entities/Player.prefab", "Default", 0, null);

            // 复用：Instantiate 不应再次调用
            Assert.AreEqual(instantiateCountAfterFirst, m_Helper.InstantiateCount,
                "InstantiateEntity should NOT be called when pool hits");
            Assert.IsTrue(m_EM.HasEntity(2));
        }

        [Test]
        public void HideEntity_LoadingInFlight_CancelsRequest()
        {
            m_Res.Async = true;
            m_EM.ShowEntity(5, "Entities/Hero.prefab", "Default", 0, null);
            // 加载中
            m_EM.HideEntity(5);
            m_Res.FlushAllPending(); // 异步回调到达，但请求已取消

            Assert.IsFalse(m_EM.HasEntity(5));
            // MockResourceManager 在 cancelled handle 上完成 load 时，会模拟"晚到资产卸载"路径
            Assert.AreEqual(1, m_Res.UnloadCalls, "cancelled asset should be unloaded");
        }

        [Test]
        public void ShowEntity_DuplicateId_Throws()
        {
            m_EM.ShowEntity(1, "Entities/A.prefab", "Default", 0, null);
            Assert.Throws<FrameworkException>(() => m_EM.ShowEntity(1, "Entities/B.prefab", "Default", 0, null));
        }

        [Test]
        public void ShowEntity_NoGroup_Throws()
        {
            Assert.Throws<FrameworkException>(() =>
                m_EM.ShowEntity(1, "Entities/A.prefab", "Missing", 0, null));
        }

        [Test]
        public void HideAllLoadedEntities_ClearsAll()
        {
            m_EM.ShowEntity(1, "Entities/A.prefab", "Default", 0, null);
            m_EM.ShowEntity(2, "Entities/B.prefab", "Default", 0, null);
            m_EM.ShowEntity(3, "Entities/C.prefab", "Default", 0, null);
            Assert.AreEqual(3, m_EM.EntityCount);

            m_EM.HideAllLoadedEntities();
            Assert.AreEqual(0, m_EM.EntityCount);
        }

        [Test]
        public void HideEntity_FiresHideCompleteEvent()
        {
            int capturedId = -1;
            string capturedAsset = null;
            object capturedUserData = null;
            m_EM.HideEntityComplete += (s, e) =>
            {
                capturedId = e.EntityId;
                capturedAsset = e.EntityAssetName;
                capturedUserData = e.UserData;
            };

            m_EM.ShowEntity(7, "Entities/Foo.prefab", "Default", 0, null);
            var ud = new object();
            m_EM.HideEntity(7, ud);

            Assert.AreEqual(7, capturedId);
            Assert.AreEqual("Entities/Foo.prefab", capturedAsset);
            Assert.AreSame(ud, capturedUserData);
        }
    }
}
