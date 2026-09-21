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
    ///
    /// 历史修复：
    ///   - 公开 API 入口加 Framework.EnsureMainThread 主线程断言（仅 Editor/DEV 构建生效）。
    ///   - 共享缓存 List 改 ThreadStatic + Filter 写入到调用方传入的 output，避免重入相互覆盖。
    ///   - Unspawn/Register/Capacity 路径不再误重置 m_AutoReleaseTime。
    ///   - Shutdown 路径加 try/catch 隔离用户代码异常。
    ///
    /// 专业化（WS1-M1）：
    ///   - 释放路径零分配：快照走池内复用的 List，重入（用户 Release 回调里再触发释放）时才退化为分配。
    ///   - Prewarm / Trim / GetMetrics / 非分配 GetAllObjectInfos。
    ///   - 在用计数与累计指标增量维护，读取 O(1)。
    ///
    /// 警告：仍非线程安全；线程断言用于在 Editor/DEV 快速发现误用。
    /// </summary>
    internal sealed class ObjectPool<T> : ObjectPoolBase, IObjectPool<T> where T : ObjectBase
    {
        [ThreadStatic]
        private static List<T> ts_CachedCanRelease;
        [ThreadStatic]
        private static List<T> ts_CachedToRelease;

        /// <summary>
        /// Trim 按最久未用优先释放。排序走 NoAllocSort（List.Sort 的两个重载在 Mono 下都会分配委托/包装对象）。
        /// </summary>
        private static readonly IComparer<T> s_ByLastUseTimeAscending = new LastUseTimeComparer();

        private readonly MultiDictionary<string, Object<T>> m_Objects;
        private readonly Dictionary<object, Object<T>> m_ObjectMap;
        private readonly List<T> m_ReleaseSnapshot;
        /// <summary>实例方法转委托每次都会分配；缓存一次，Release 稳态零分配。</summary>
        private readonly ReleaseObjectFilterCallback<T> m_DefaultFilter;
        private readonly bool m_AllowMultiSpawn;
        private float m_AutoReleaseInterval;
        private int m_Capacity;
        private float m_ExpireTime;
        private int m_Priority;
        private float m_AutoReleaseTime;
        private bool m_Releasing;

        // 指标（增量维护，读取 O(1)）
        private int m_InUseCount;
        private int m_PeakCount;
        private long m_TotalSpawn;
        private long m_TotalUnspawn;
        private long m_TotalRelease;
        private long m_SpawnMiss;

        public ObjectPool(string name, bool allowMultiSpawn, float autoReleaseInterval, int capacity, float expireTime, int priority)
            : base(name)
        {
            m_Objects = new MultiDictionary<string, Object<T>>();
            m_ObjectMap = new Dictionary<object, Object<T>>();
            m_ReleaseSnapshot = new List<T>();
            m_DefaultFilter = DefaultReleaseObjectFilterCallback;
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

        public override int SpawnedCount
        {
            get { return m_InUseCount; }
        }

        public override int PeakCount
        {
            get { return m_PeakCount; }
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

        // ================================================================
        //  注册 / 预热
        // ================================================================

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
            if (spawned)
            {
                m_InUseCount++;
            }

            if (Count > m_PeakCount)
            {
                m_PeakCount = Count;
            }

            if (Count > m_Capacity)
            {
                ReleaseInternal(/*resetAutoReleaseTimer*/ false);
            }
        }

        public int Prewarm(int count, Func<T> factory)
        {
            Framework.EnsureMainThread("ObjectPool.Prewarm");
            if (count < 0)
            {
                throw new FrameworkException("Prewarm count is invalid.");
            }

            if (factory == null)
            {
                throw new FrameworkException("Prewarm factory is invalid.");
            }

            if (count > m_Capacity)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ObjectPool '{0}'：Prewarm 目标 {1} 超过 Capacity {2}——超出部分会在注册后立刻被容量回收，预热等于白做。请先调大 Capacity。",
                    FullName, count, m_Capacity));
            }

            int created = 0;
            while (Count < count)
            {
                T obj = factory();
                if (obj == null)
                {
                    throw new FrameworkException(Utility.Text.Format("ObjectPool '{0}'：Prewarm factory 返回了 null（已创建 {1} 个）。", FullName, created));
                }

                Register(obj, false);
                created++;
            }

            return created;
        }

        // ================================================================
        //  获取 / 归还
        // ================================================================

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
                        bool wasIdle = !internalObject.IsInUse;
                        T result = internalObject.Spawn();
                        m_TotalSpawn++;
                        if (wasIdle)
                        {
                            m_InUseCount++;
                        }

                        return result;
                    }
                }
            }

            m_SpawnMiss++;
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
            if (internalObject == null)
            {
                throw new FrameworkException(Utility.Text.Format("Can not find target in object pool '{0}', target type is '{1}', target value is '{2}'.", FullName, target.GetType().FullName, target));
            }

            internalObject.Unspawn();
            m_TotalUnspawn++;
            if (internalObject.SpawnCount <= 0)
            {
                m_InUseCount--;
                if (Count > m_Capacity && CountUnused() > 0)
                {
                    ReleaseInternal(/*resetAutoReleaseTimer*/ false);
                }
            }
        }

        // ================================================================
        //  释放
        // ================================================================

        public override void Release()
        {
            Framework.EnsureMainThread("ObjectPool.Release");
            ReleaseInternal(/*resetAutoReleaseTimer*/ true);
        }

        public override void ReleaseAllUnused()
        {
            Framework.EnsureMainThread("ObjectPool.ReleaseAllUnused");
            List<T> candidates = AcquireCanReleaseScratch();
            GetCanReleaseObjects(candidates);
            ReleaseSnapshot(candidates);
        }

        public override int Trim(int keepCount)
        {
            Framework.EnsureMainThread("ObjectPool.Trim");
            if (keepCount < 0)
            {
                throw new FrameworkException("Keep count is invalid.");
            }

            int toReleaseCount = Count - keepCount;
            if (toReleaseCount <= 0)
            {
                return 0;
            }

            List<T> candidates = AcquireCanReleaseScratch();
            GetCanReleaseObjects(candidates);
            if (candidates.Count == 0)
            {
                return 0;
            }

            // 最久未用的先走：与自动过期的判定方向一致，收缩不会误伤刚归还的热对象。
            NoAllocSort.Sort(candidates, s_ByLastUseTimeAscending);

            List<T> toRelease = AcquireToReleaseScratch();
            int n = toReleaseCount < candidates.Count ? toReleaseCount : candidates.Count;
            for (int i = 0; i < n; i++)
            {
                toRelease.Add(candidates[i]);
            }

            ReleaseSnapshot(toRelease);
            return n;
        }

        // ================================================================
        //  诊断
        // ================================================================

        public override void GetMetrics(out ObjectPoolMetrics metrics)
        {
            metrics = new ObjectPoolMetrics(
                Count, m_InUseCount, m_PeakCount,
                m_TotalSpawn, m_TotalUnspawn, m_TotalRelease, m_SpawnMiss,
                m_Capacity, m_ExpireTime);
        }

        public override ObjectInfo[] GetAllObjectInfos()
        {
            Framework.EnsureMainThread("ObjectPool.GetAllObjectInfos");
            List<ObjectInfo> results = new List<ObjectInfo>(Count);
            GetAllObjectInfos(results);
            return results.ToArray();
        }

        public override void GetAllObjectInfos(List<ObjectInfo> results)
        {
            Framework.EnsureMainThread("ObjectPool.GetAllObjectInfos");
            if (results == null)
            {
                throw new FrameworkException("Results is invalid.");
            }

            results.Clear();
            foreach (KeyValuePair<string, GameLinkedListRange<Object<T>>> objectRanges in m_Objects)
            {
                foreach (Object<T> internalObject in objectRanges.Value)
                {
                    results.Add(new ObjectInfo(internalObject.Name, internalObject.Locked, internalObject.CustomCanReleaseFlag, internalObject.Priority, internalObject.LastUseTime, internalObject.SpawnCount));
                }
            }
        }

        // ================================================================
        //  生命周期
        // ================================================================

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
            List<Object<T>> toRelease = new List<Object<T>>(Count);
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
            m_ReleaseSnapshot.Clear();
            m_InUseCount = 0;
            m_Releasing = false;
        }

        // ================================================================
        //  内部
        // ================================================================

        private Object<T> GetObject(object target)
        {
            Object<T> internalObject;
            return m_ObjectMap.TryGetValue(target, out internalObject) ? internalObject : null;
        }

        private int CountUnused()
        {
            return Count - m_InUseCount;
        }

        private void GetCanReleaseObjects(List<T> results)
        {
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
            ReleaseInternal(toReleaseCount, m_DefaultFilter, resetAutoReleaseTimer);
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

            ReleaseSnapshot(toReleaseObjects);
        }

        /// <summary>
        /// 按快照释放一组对象。
        ///
        /// 快照的意义：ReleaseObject 会调用用户的 Release 回调，回调里再 Spawn/Register/Release 都是合法的，
        /// 这些操作会改写 ThreadStatic 暂存 List 与链表，所以迭代必须基于一份独立拷贝。
        /// 稳态走池内复用的 m_ReleaseSnapshot（零分配）；只有"释放过程中被重入"这一罕见情形才退化为分配一份
        /// 新数组——正确性优先，代价只落在重入路径上。
        /// </summary>
        private void ReleaseSnapshot(List<T> toRelease)
        {
            if (toRelease == null || toRelease.Count == 0)
            {
                return;
            }

            if (m_Releasing)
            {
                T[] reentrantSnapshot = toRelease.ToArray();
                for (int i = 0; i < reentrantSnapshot.Length; i++)
                {
                    ReleaseObject(reentrantSnapshot[i]);
                }

                return;
            }

            m_Releasing = true;
            try
            {
                m_ReleaseSnapshot.Clear();
                for (int i = 0; i < toRelease.Count; i++)
                {
                    m_ReleaseSnapshot.Add(toRelease[i]);
                }

                for (int i = 0; i < m_ReleaseSnapshot.Count; i++)
                {
                    ReleaseObject(m_ReleaseSnapshot[i]);
                }
            }
            finally
            {
                m_ReleaseSnapshot.Clear();
                m_Releasing = false;
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

                    // 先摘除再回调：用户回调里的任何池操作都看不到这个半释放对象。
                    m_Objects.Remove(obj.Name, internalObject);
                    m_ObjectMap.Remove(obj.Target);
                    m_TotalRelease++;
                    try { internalObject.Release(false); }
                    catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' Release threw: {1}", FullName, ex); }
                    try { ReferencePool.Release(internalObject); }
                    catch (Exception ex) { FrameworkLog.Error("ObjectPool '{0}' ReferencePool.Release(internal) threw: {1}", FullName, ex); }
                    return;
                }
            }

            // 快照里的对象可能已被重入的释放先行处理掉：这不是错误，静默跳过即可。
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

        private sealed class LastUseTimeComparer : IComparer<T>
        {
            public int Compare(T a, T b)
            {
                return a.LastUseTime.CompareTo(b.LastUseTime);
            }
        }
    }

    /// <summary>
    /// 释放对象筛选函数。output 由调用方提供，避免分配。
    /// </summary>
    public delegate List<T> ReleaseObjectFilterCallback<T>(List<T> candidateObjects, int toReleaseCount, DateTime expireTime, List<T> output) where T : ObjectBase;
}
