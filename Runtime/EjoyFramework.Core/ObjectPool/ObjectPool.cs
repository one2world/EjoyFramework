//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.ObjectPool
{
    /// <summary>
    /// 对象池。
    /// 修复：
    ///   - 公开 API 入口加 Framework.EnsureMainThread 主线程断言（仅 Editor/DEV 构建生效）。
    ///   - 共享缓存 List 改 ThreadStatic + Filter 写入到调用方传入的 output，避免重入相互覆盖。
    ///   - Unspawn/Register/Capacity 路径不再误重置 m_AutoReleaseTime。
    ///   - Shutdown 路径加 try/catch 隔离用户代码异常。
    /// 警告：仍非线程安全；线程断言用于在 Editor/DEV 快速发现误用。
    /// </summary>
    internal sealed class ObjectPool<T> : ObjectPoolBase, IObjectPool<T> where T : ObjectBase
    {
        [ThreadStatic]
        private static List<T> ts_CachedCanRelease;
        [ThreadStatic]
        private static List<T> ts_CachedToRelease;

        private readonly MultiDictionary<string, Object<T>> m_Objects;
        private readonly Dictionary<object, Object<T>> m_ObjectMap;
        private readonly bool m_AllowMultiSpawn;
        private float m_AutoReleaseInterval;
        private int m_Capacity;
        private float m_ExpireTime;
        private int m_Priority;
        private float m_AutoReleaseTime;

        public ObjectPool(string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity, float expireTime, int priority)
            : base(name)
        {
            m_Objects = new MultiDictionary<string, Object<T>>();
            m_ObjectMap = new Dictionary<object, Object<T>>();
            m_AllowMultiSpawn = allowMultiSpawn;
            m_AutoReleaseInterval = autoReleaseInterval;
            m_Capacity = capacity;
            m_ExpireTime = expireTime;
            m_Priority = priority;
            m_AutoReleaseTime = 0f;
        }

        public override Type ObjectType
        {
            get { return typeof(T); }
        }

        public override int Count
        {
            get { return m_ObjectMap.Count; }
        }

        public override int CanReleaseCount
        {
            get
            {
                Framework.EnsureMainThread("ObjectPool.CanReleaseCount");
                int n = 0;
                foreach (KeyValuePair<string, GameLinkedListRange<Object<T>>> kvp in m_Objects)
                {
                    foreach (Object<T> obj in kvp.Value)
                    {
                        if (obj.IsInUse || obj.Locked || !obj.CustomCanReleaseFlag) continue;
                        n++;
                    }
                }
                return n;
            }
        }

        public override bool AllowMultiSpawn
        {
            get { return m_AllowMultiSpawn; }
        }

        public override float AutoReleaseInterval
        {
            get { return m_AutoReleaseInterval; }
            set { m_AutoReleaseInterval = value; }
        }

        public override int Capacity
        {
            get { return m_Capacity; }
            set
            {
                if (value < 0)
                {
                    throw new FrameworkException("Capacity is invalid.");
                }

                Framework.EnsureMainThread("ObjectPool.Capacity_set");
                m_Capacity = value;
                ReleaseInternal(/*resetAutoReleaseTimer*/ false);
            }
        }

        public override float ExpireTime
        {
            get { return m_ExpireTime; }
            set
            {
                if (value < 0f)
                {
                    throw new FrameworkException("Expire time is invalid.");
                }

                m_ExpireTime = value;
            }
        }

        public override int Priority
        {
            get { return m_Priority; }
            set { m_Priority = value; }
        }

        public void Register(T obj, bool spawned)
        {
            Framework.EnsureMainThread("ObjectPool.Register");
            if (obj == null)
            {
                throw new FrameworkException("Object is invalid.");
            }

            Object<T> internalObject = Object<T>.Create(obj, spawned);
            m_Objects.Add(obj.Name, internalObject);
            m_ObjectMap.Add(obj.Target, internalObject);

            if (Count > m_Capacity)
            {
                ReleaseInternal(/*resetAutoReleaseTimer*/ false);
            }
        }

        public bool CanSpawn()
        {
            return CanSpawn(string.Empty);
        }

        public bool CanSpawn(string name)
        {
            Framework.EnsureMainThread("ObjectPool.CanSpawn");
            if (name == null)
            {
                throw new FrameworkException("Name is invalid.");
            }

            GameLinkedListRange<Object<T>> objectRange = default(GameLinkedListRange<Object<T>>);
            if (m_Objects.TryGetValue(name, out objectRange))
            {
                foreach (Object<T> internalObject in objectRange)
                {
                    if (m_AllowMultiSpawn || !internalObject.IsInUse)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public T Spawn()
        {
            return Spawn(string.Empty);
        }

        public T Spawn(string name)
        {
            Framework.EnsureMainThread("ObjectPool.Spawn");
            if (name == null)
            {
                throw new FrameworkException("Name is invalid.");
            }

            GameLinkedListRange<Object<T>> objectRange = default(GameLinkedListRange<Object<T>>);
            if (m_Objects.TryGetValue(name, out objectRange))
            {
                foreach (Object<T> internalObject in objectRange)
                {
                    if (m_AllowMultiSpawn || !internalObject.IsInUse)
                    {
                        return internalObject.Spawn();
                    }
                }
            }

            return null;
        }

        public void Unspawn(object target)
        {
            Framework.EnsureMainThread("ObjectPool.Unspawn");
            if (target == null)
            {
                throw new FrameworkException("Target is invalid.");
            }

            Object<T> internalObject = GetObject(target);
            if (internalObject != null)
            {
                internalObject.Unspawn();
                if (internalObject.SpawnCount <= 0)
                {
                    int unused = CountUnused();
                    if (Count > m_Capacity && unused > 0)
                    {
                        ReleaseInternal(/*resetAutoReleaseTimer*/ false);
                    }
                }
            }
            else
            {
                throw new FrameworkException(Utility.Text.Format("Can not find target in object pool '{0}', target type is '{1}', target value is '{2}'.", FullName, target.GetType().FullName, target));
            }
        }

        public override void Release()
        {
            Framework.EnsureMainThread("ObjectPool.Release");
            ReleaseInternal(/*resetAutoReleaseTimer*/ true);
        }

        public override void ReleaseAllUnused()
        {
            Framework.EnsureMainThread("ObjectPool.ReleaseAllUnused");
            // 用本地局部 List，完全独立于 ThreadStatic，杜绝重入冲突。
            List<T> candidates = new List<T>();
            GetCanReleaseObjects(candidates);
            foreach (T toReleaseObject in candidates)
            {
                ReleaseObject(toReleaseObject);
            }
        }

        public override ObjectInfo[] GetAllObjectInfos()
        {
            Framework.EnsureMainThread("ObjectPool.GetAllObjectInfos");
            List<ObjectInfo> results = new List<ObjectInfo>();
            foreach (KeyValuePair<string, GameLinkedListRange<Object<T>>> objectRanges in m_Objects)
            {
                foreach (Object<T> internalObject in objectRanges.Value)
                {
                    results.Add(new ObjectInfo(internalObject.Name, internalObject.Locked, internalObject.CustomCanReleaseFlag, internalObject.Priority, internalObject.LastUseTime, internalObject.SpawnCount));
                }
            }

            return results.ToArray();
        }

        internal override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_AutoReleaseTime += realElapseSeconds;
            if (m_AutoReleaseTime < m_AutoReleaseInterval)
            {
                return;
            }

            ReleaseInternal(/*resetAutoReleaseTimer*/ true);
        }

        internal override void Shutdown()
        {
            // 拷贝再迭代，避免在 Release 中改 m_Objects 时破坏迭代器
            List<Object<T>> toRelease = new List<Object<T>>();
            foreach (KeyValuePair<string, GameLinkedListRange<Object<T>>> objectRanges in m_Objects)
            {
                foreach (Object<T> internalObject in objectRanges.Value)
                {
                    toRelease.Add(internalObject);
                }
            }

            for (int i = 0; i < toRelease.Count; i++)
            {
                try { toRelease[i].Release(true); }
                catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' Release(shutdown) threw: {1}", FullName, ex); }
                try { ReferencePool.Release(toRelease[i]); }
                catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' ReferencePool.Release(internal) threw: {1}", FullName, ex); }
            }

            m_Objects.Clear();
            m_ObjectMap.Clear();
        }

        private Object<T> GetObject(object target)
        {
            if (target == null)
            {
                throw new FrameworkException("Target is invalid.");
            }

            Object<T> internalObject = null;
            if (m_ObjectMap.TryGetValue(target, out internalObject))
            {
                return internalObject;
            }

            return null;
        }

        private int CountUnused()
        {
            int n = 0;
            foreach (KeyValuePair<object, Object<T>> kvp in m_ObjectMap)
            {
                if (!kvp.Value.IsInUse) n++;
            }
            return n;
        }

        private void GetCanReleaseObjects(List<T> results)
        {
            if (results == null)
            {
                throw new FrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<string, GameLinkedListRange<Object<T>>> objectRanges in m_Objects)
            {
                foreach (Object<T> internalObject in objectRanges.Value)
                {
                    if (internalObject.IsInUse || internalObject.Locked || !internalObject.CustomCanReleaseFlag)
                    {
                        continue;
                    }

                    results.Add(internalObject.Peek());
                }
            }
        }

        private void ReleaseInternal(bool resetAutoReleaseTimer)
        {
            int toReleaseCount = Count - m_Capacity;
            if (toReleaseCount < 0) toReleaseCount = 0;
            ReleaseInternal(toReleaseCount, DefaultReleaseObjectFilterCallback, resetAutoReleaseTimer);
        }

        private void ReleaseInternal(int toReleaseCount, ReleaseObjectFilterCallback<T> releaseObjectFilterCallback, bool resetAutoReleaseTimer)
        {
            if (resetAutoReleaseTimer) m_AutoReleaseTime = 0f;
            if (toReleaseCount < 0) toReleaseCount = 0;

            DateTime expireTime = DateTime.MinValue;
            if (m_ExpireTime < float.MaxValue)
            {
                expireTime = DateTime.UtcNow.AddSeconds(-m_ExpireTime);
            }

            List<T> canRelease = AcquireCanReleaseScratch();
            List<T> toReleaseScratch = AcquireToReleaseScratch();

            GetCanReleaseObjects(canRelease);
            List<T> toReleaseObjects = releaseObjectFilterCallback(canRelease, toReleaseCount, expireTime, toReleaseScratch);
            if (toReleaseObjects == null || toReleaseObjects.Count <= 0)
            {
                return;
            }

            // 拷贝快照，避免 ReleaseObject 间接修改 ThreadStatic 缓存导致迭代失效。
            // 重入安全性说明：snapshot 已脱离 ThreadStatic/链表迭代器；ReleaseObject 内在调用用户
            // Object<T>.Release(true) 之前就已从 m_Objects / m_ObjectMap 摘除该对象，故用户回调中
            // 再次 Spawn/Register 不会破坏本次释放循环正在使用的快照或集合迭代。
            T[] snapshot = toReleaseObjects.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ReleaseObject(snapshot[i]);
            }
        }

        private static List<T> AcquireCanReleaseScratch()
        {
            if (ts_CachedCanRelease == null) ts_CachedCanRelease = new List<T>();
            ts_CachedCanRelease.Clear();
            return ts_CachedCanRelease;
        }

        private static List<T> AcquireToReleaseScratch()
        {
            if (ts_CachedToRelease == null) ts_CachedToRelease = new List<T>();
            ts_CachedToRelease.Clear();
            return ts_CachedToRelease;
        }

        private void ReleaseObject(T obj)
        {
            if (obj == null)
            {
                throw new FrameworkException("Object is invalid.");
            }

            GameLinkedListRange<Object<T>> objectRange = default(GameLinkedListRange<Object<T>>);
            if (m_Objects.TryGetValue(obj.Name, out objectRange))
            {
                foreach (Object<T> internalObject in objectRange)
                {
                    if (internalObject.Peek() != obj)
                    {
                        continue;
                    }

                    m_Objects.Remove(obj.Name, internalObject);
                    m_ObjectMap.Remove(obj.Target);
                    try { internalObject.Release(false); }
                    catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' Release threw: {1}", FullName, ex); }
                    try { ReferencePool.Release(internalObject); }
                    catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' ReferencePool.Release(internal) threw: {1}", FullName, ex); }
                    return;
                }
            }

            throw new FrameworkException(Utility.Text.Format("Can not release object which is not found."));
        }

        private List<T> DefaultReleaseObjectFilterCallback(List<T> candidateObjects, int toReleaseCount, DateTime expireTime, List<T> output)
        {
            output.Clear();

            if (expireTime > DateTime.MinValue)
            {
                for (int i = candidateObjects.Count - 1; i >= 0; i--)
                {
                    if (candidateObjects[i].LastUseTime <= expireTime)
                    {
                        output.Add(candidateObjects[i]);
                        candidateObjects.RemoveAt(i);
                    }
                }

                toReleaseCount -= output.Count;
            }

            for (int i = 0; toReleaseCount > 0 && i < candidateObjects.Count; i++)
            {
                output.Add(candidateObjects[i]);
                toReleaseCount--;
            }

            return output;
        }
    }

    /// <summary>
    /// 释放对象筛选函数。output 由调用方提供，避免分配。
    /// </summary>
    public delegate List<T> ReleaseObjectFilterCallback<T>(List<T> candidateObjects, int toReleaseCount, DateTime expireTime, List<T> output) where T : ObjectBase;
}
