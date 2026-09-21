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

            // 先校验再回调：重复归还是业务 bug，此时若先跑一遍用户 OnUnspawn 再抛错，
            // 用户对象会经历第二次"归还"副作用（清状态/回收子资源），把一个可定位的配对错误变成难查的状态错乱。
            if (m_SpawnCount <= 0)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "Object '{0}' 被重复 Unspawn（当前 SpawnCount = {1}）：每次 Spawn 只能配对一次 Unspawn，请检查业务侧是否在多处归还同一对象。",
                    Name, m_SpawnCount));
            }

            m_Object.OnUnspawn();
            m_Object.LastUseTime = DateTime.UtcNow;
            m_SpawnCount--;
        }

        public void Release(bool isShutdown)
        {
            if (m_Object == null) throw new FrameworkException("Internal object payload is null (Release on a cleared/released object).");
            m_Object.Release(isShutdown);
        }
    }
}
