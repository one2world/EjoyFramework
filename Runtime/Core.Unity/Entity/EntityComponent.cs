//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Entity;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 实体组件。
    /// 负责：注入 ResourceManager/ObjectPoolManager/EntityHelper，预创建 InstanceRoot 与 Inspector 配置的 group。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Entity")]
    public sealed class EntityComponent : GameFrameworkComponent
    {
        [Serializable]
        public sealed class EntityGroupSetting
        {
            public string Name = "Default";
            [Tooltip("实例自动回收检查间隔（秒）")]
            public float AutoReleaseInterval = 60f;
            [Tooltip("池容量")]
            public int Capacity = 16;
            [Tooltip("过期时间（秒）")]
            public float ExpireTime = 60f;
            [Tooltip("优先级")]
            public int Priority = 0;
        }

        [SerializeField]
        private Transform m_InstanceRoot = null;

        [SerializeField]
        private EntityGroupSetting[] m_EntityGroups = null;

        private IEntityManager m_EntityManager;

        protected override void Awake()
        {
            base.Awake();
            m_EntityManager = Framework.GetModule<IEntityManager>();
            if (m_EntityManager == null)
            {
                Log.Fatal("Entity manager is invalid.");
                return;
            }
            ConfigureManager();
        }

        private void ConfigureManager()
        {
            // EntityManager 自取 ResourceManager 和 ObjectPoolManager（懒拉取）
            if (m_InstanceRoot == null)
            {
                m_InstanceRoot = new GameObject("Entity Instances").transform;
                m_InstanceRoot.SetParent(gameObject.transform);
                m_InstanceRoot.localScale = Vector3.one;
            }

            m_EntityManager.SetEntityHelper(new DefaultEntityHelper(m_InstanceRoot));

            if (m_EntityGroups != null)
            {
                foreach (var g in m_EntityGroups)
                {
                    if (g == null || string.IsNullOrEmpty(g.Name)) continue;
                    AddEntityGroup(g.Name, g.AutoReleaseInterval, g.Capacity, g.ExpireTime, g.Priority);
                }
            }
        }

        public int EntityCount { get { return m_EntityManager.EntityCount; } }
        public int EntityGroupCount { get { return m_EntityManager.EntityGroupCount; } }

        public bool HasEntityGroup(string n) { return m_EntityManager.HasEntityGroup(n); }
        public IEntityGroup GetEntityGroup(string n) { return m_EntityManager.GetEntityGroup(n); }

        public bool AddEntityGroup(string name, float autoReleaseInterval, int capacity, float expireTime, int priority)
        {
            return m_EntityManager.AddEntityGroup(name, autoReleaseInterval, capacity, expireTime, priority, null);
        }

        public bool HasEntity(int id) { return m_EntityManager.HasEntity(id); }
        public IEntity GetEntity(int id) { return m_EntityManager.GetEntity(id); }
        public IEntity[] GetAllLoadedEntities() { return m_EntityManager.GetAllLoadedEntities(); }

        public void ShowEntity(int entityId, string assetName, string groupName)
        {
            m_EntityManager.ShowEntity(entityId, assetName, groupName, 0, new EntityData { EntityId = entityId, AssetName = assetName });
        }

        public void ShowEntity(int entityId, string assetName, string groupName, int priority, object userData)
        {
            object packed = userData;
            if (!(userData is EntityData))
            {
                packed = new EntityData { EntityId = entityId, AssetName = assetName, UserData = userData };
            }
            m_EntityManager.ShowEntity(entityId, assetName, groupName, priority, packed);
        }

        public void ShowEntity<T>(int entityId, string assetName, string groupName, object userData) where T : EntityLogic
        {
            ShowEntity(entityId, assetName, groupName, 0, userData);
        }

        public void HideEntity(int entityId) { m_EntityManager.HideEntity(entityId); }
        public void HideEntity(int entityId, object userData) { m_EntityManager.HideEntity(entityId, userData); }
        public void HideAllLoadedEntities() { m_EntityManager.HideAllLoadedEntities(); }
        public void HideAllLoadingEntities() { m_EntityManager.HideAllLoadingEntities(); }
    }
}
