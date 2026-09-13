//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 内部对象。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    internal sealed class Object<T> : IReference where T : ObjectBase
    {
        private T m_Object;
        private int m_SpawnCount;

        public Object()
        {
            m_Object = null;
            m_SpawnCount = 0;
        }

        public string Name
        {
            get { return m_Object != null ? m_Object.Name : null; }
        }

        public bool Locked
        {
            get { return m_Object != null && m_Object.Locked; }
            set
            {
                if (m_Object != null)
                {
                    m_Object.Locked = value;
                }
            }
        }

        public int Priority
        {
            get { return m_Object != null ? m_Object.Priority : 0; }
        }

        public bool CustomCanReleaseFlag
        {
            get { return m_Object != null && m_Object.CustomCanReleaseFlag; }
        }

        public bool IsInUse
        {
            get { return m_SpawnCount > 0; }
        }

        public int SpawnCount
        {
            get { return m_SpawnCount; }
        }

        public DateTime LastUseTime
        {
            get { return m_Object != null ? m_Object.LastUseTime : default(DateTime); }
        }

        public static Object<T> Create(T obj, bool spawned)
        {
            if (obj == null)
            {
                throw new FrameworkException("Object is invalid.");
            }

            Object<T> internalObject = ReferencePool.Acquire<Object<T>>();
            internalObject.m_Object = obj;
            internalObject.m_SpawnCount = spawned ? 1 : 0;
            if (spawned)
            {
                obj.OnSpawn();
            }

            return internalObject;
        }

        public void Clear()
        {
            m_Object = null;
            m_SpawnCount = 0;
        }

        public T Peek()
        {
            return m_Object;
        }

        public T Spawn()
        {
            if (m_Object == null) throw new FrameworkException("Internal object payload is null (Spawn on a cleared/released object).");
            m_SpawnCount++;
            m_Object.LastUseTime = DateTime.UtcNow;
            m_Object.OnSpawn();
            return m_Object;
        }

        public void Unspawn()
        {
            if (m_Object == null) throw new FrameworkException("Internal object payload is null (Unspawn on a cleared/released object).");
            m_Object.OnUnspawn();
            m_Object.LastUseTime = DateTime.UtcNow;
            m_SpawnCount--;
            if (m_SpawnCount < 0)
            {
                throw new FrameworkException(Utility.Text.Format("Object '{0}' spawn count is less than 0.", Name));
            }
        }

        public void Release(bool isShutdown)
        {
            if (m_Object == null) throw new FrameworkException("Internal object payload is null (Release on a cleared/released object).");
            m_Object.Release(isShutdown);
        }
    }
}
