//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.ObjectPool;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 对象池组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/ObjectPool")]
    public sealed class ObjectPoolComponent : GameFrameworkComponent
    {
        private IObjectPoolManager m_ObjectPoolManager;

        protected override void Awake()
        {
            base.Awake();
            m_ObjectPoolManager = Framework.GetModule<IObjectPoolManager>();
            if (m_ObjectPoolManager == null)
            {
                Log.Fatal("Object pool manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 获取对象池数量。
        /// </summary>
        public int Count
        {
            get { return m_ObjectPoolManager.Count; }
        }

        /// <summary>
        /// 检查是否存在对象池。
        /// </summary>
        public bool HasObjectPool<T>() where T : ObjectBase
        {
            return m_ObjectPoolManager.HasObjectPool<T>();
        }

        /// <summary>
        /// 获取对象池。
        /// </summary>
        public IObjectPool<T> GetObjectPool<T>() where T : ObjectBase
        {
            return m_ObjectPoolManager.GetObjectPool<T>();
        }

        /// <summary>
        /// 创建允许单次获取的对象池。
        /// </summary>
        public IObjectPool<T> CreateSingleSpawnObjectPool<T>(string name = "", float autoReleaseInterval = 60f, int capacity = int.MaxValue, float expireTime = float.MaxValue, int priority = 0) where T : ObjectBase
        {
            return m_ObjectPoolManager.CreateSingleSpawnObjectPool<T>(name, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 创建允许多次获取的对象池。
        /// </summary>
        public IObjectPool<T> CreateMultiSpawnObjectPool<T>(string name = "", float autoReleaseInterval = 60f, int capacity = int.MaxValue, float expireTime = float.MaxValue, int priority = 0) where T : ObjectBase
        {
            return m_ObjectPoolManager.CreateMultiSpawnObjectPool<T>(name, autoReleaseInterval, capacity, expireTime, priority);
        }

        /// <summary>
        /// 销毁对象池。
        /// </summary>
        public bool DestroyObjectPool<T>() where T : ObjectBase
        {
            return m_ObjectPoolManager.DestroyObjectPool<T>();
        }

        /// <summary>
        /// 释放对象池中的可释放对象。
        /// </summary>
        public void Release()
        {
            m_ObjectPoolManager.Release();
        }

        /// <summary>
        /// 释放对象池中的所有未使用对象。
        /// </summary>
        public void ReleaseAllUnused()
        {
            m_ObjectPoolManager.ReleaseAllUnused();
        }
    }
}
