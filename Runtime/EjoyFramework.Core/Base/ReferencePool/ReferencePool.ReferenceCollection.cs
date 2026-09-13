//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core
{
    public static partial class ReferencePool
    {
        /// <summary>
        /// 引用集合：每类型一个。
        /// 修复：
        ///   - 五个统计字段全部用 Interlocked。
        ///   - Acquire 路径下 m_AddReferenceCount 在创建对象后 Interlocked.Increment。
        ///   - Release.Clear 用 try/finally 保证计数对齐。
        ///   - 增加 MaxCapacity，超过即丢弃避免无界增长（同时调用 Clear 释放成员资源）。
        ///   - Strict check 用 HashSet 实现 O(1) 查找（仅 Strict 模式时分配）。
        /// </summary>
        private sealed class ReferenceCollection
        {
            private readonly Queue<IReference> m_References;
            private readonly Type m_ReferenceType;
            // 反射-free 工厂：首次经泛型 Acquire<T>/Add<T> 触达时捕获 () => new T()（编译期按具体类型解析、
            // IL2CPP 不裁剪），供非泛型 Type 路径复用，取代 Activator.CreateInstance(Type) 的运行时反射。
            private Func<IReference> m_Factory;
            // Strict 模式辅助集合，O(1) 防重复 Release。仅在 EnableStrictCheck 时使用（懒分配）。
            private HashSet<IReference> m_StrictSet;
            private int m_UsingReferenceCount;
            private int m_AcquireReferenceCount;
            private int m_ReleaseReferenceCount;
            private int m_AddReferenceCount;
            private int m_RemoveReferenceCount;
            // 0 表示无上限。
            private int m_MaxCapacity = 0;

            public ReferenceCollection(Type referenceType)
            {
                m_References = new Queue<IReference>();
                m_ReferenceType = referenceType;
            }

            public Type ReferenceType
            {
                get { return m_ReferenceType; }
            }

            public int UnusedReferenceCount
            {
                get
                {
                    lock (m_References) { return m_References.Count; }
                }
            }

            public int UsingReferenceCount { get { return Volatile.Read(ref m_UsingReferenceCount); } }
            public int AcquireReferenceCount { get { return Volatile.Read(ref m_AcquireReferenceCount); } }
            public int ReleaseReferenceCount { get { return Volatile.Read(ref m_ReleaseReferenceCount); } }
            public int AddReferenceCount { get { return Volatile.Read(ref m_AddReferenceCount); } }
            public int RemoveReferenceCount { get { return Volatile.Read(ref m_RemoveReferenceCount); } }

            public int MaxCapacity
            {
                get { return Volatile.Read(ref m_MaxCapacity); }
                set
                {
                    if (value < 0) value = 0;
                    Volatile.Write(ref m_MaxCapacity, value);
                    TrimToCapacity();
                }
            }

            public T Acquire<T>() where T : class, IReference, new()
            {
                if (typeof(T) != m_ReferenceType)
                {
                    throw new FrameworkException("Type is invalid.");
                }

                if (m_Factory == null) m_Factory = () => new T();

                Interlocked.Increment(ref m_UsingReferenceCount);
                Interlocked.Increment(ref m_AcquireReferenceCount);

                lock (m_References)
                {
                    if (m_References.Count > 0)
                    {
                        T obj = (T)m_References.Dequeue();
                        if (m_StrictSet != null) m_StrictSet.Remove(obj);
                        return obj;
                    }
                }

                Interlocked.Increment(ref m_AddReferenceCount);
                return new T();
            }

            public IReference Acquire()
            {
                Interlocked.Increment(ref m_UsingReferenceCount);
                Interlocked.Increment(ref m_AcquireReferenceCount);

                lock (m_References)
                {
                    if (m_References.Count > 0)
                    {
                        IReference obj = m_References.Dequeue();
                        if (m_StrictSet != null) m_StrictSet.Remove(obj);
                        return obj;
                    }
                }

                Interlocked.Increment(ref m_AddReferenceCount);
                return CreateInstance();
            }

            // 反射-free 创建：用泛型路径捕获的 () => new T() 工厂；非泛型 Type 路径若从未经泛型 Acquire<T>/Add<T>
            // 触达则无工厂可用——抛出明确异常（取代原 Activator.CreateInstance(Type) 的运行时反射）。
            private IReference CreateInstance()
            {
                Func<IReference> factory = m_Factory;
                if (factory == null)
                {
                    throw new FrameworkException(string.Format(
                        "ReferencePool has no factory for type '{0}'. The non-generic Type-based create path requires the " +
                        "type to be acquired via the generic ReferencePool.Acquire<T>() / Add<T>() at least once first " +
                        "(reflection-free; Activator.CreateInstance has been removed).",
                        m_ReferenceType.FullName));
                }
                return factory();
            }

            public void Release(IReference reference)
            {
                bool clearOk = true;
                try
                {
                    reference.Clear();
                }
                catch (Exception ex)
                {
                    clearOk = false;
                    FrameworkLog.Error("Reference.Clear() threw on type '{0}': {1}", m_ReferenceType.FullName, ex);
                }

                int max = Volatile.Read(ref m_MaxCapacity);

                if (!clearOk)
                {
                    // Clear 失败的对象不能复用，直接丢弃，但仍记录 Release 计数对齐。
                    Interlocked.Increment(ref m_ReleaseReferenceCount);
                    Interlocked.Decrement(ref m_UsingReferenceCount);
                    Interlocked.Increment(ref m_RemoveReferenceCount);
                    return;
                }

                lock (m_References)
                {
                    if (m_StrictCheckEnabled())
                    {
                        if (m_StrictSet == null) m_StrictSet = new HashSet<IReference>();
                        if (!m_StrictSet.Add(reference))
                        {
                            // 重复释放：该对象上一次 Release 已经把 UsingReferenceCount 减过一次了，
                            // 这次属于被拒绝的非法重复释放——绝不能再次 Decrement，否则 using-count 会偏负。
                            // 同理也不计入 ReleaseReferenceCount（本次没有发生有效释放），直接抛异常。
                            throw new FrameworkException(string.Format("Reference '{0}' has been released already.", m_ReferenceType.FullName));
                        }
                    }

                    if (max > 0 && m_References.Count >= max)
                    {
                        // 超出上限：丢弃。
                        if (m_StrictSet != null) m_StrictSet.Remove(reference);
                        Interlocked.Increment(ref m_ReleaseReferenceCount);
                        Interlocked.Decrement(ref m_UsingReferenceCount);
                        Interlocked.Increment(ref m_RemoveReferenceCount);
                        return;
                    }

                    m_References.Enqueue(reference);
                }

                Interlocked.Increment(ref m_ReleaseReferenceCount);
                Interlocked.Decrement(ref m_UsingReferenceCount);
            }

            public void Add<T>(int count) where T : class, IReference, new()
            {
                if (typeof(T) != m_ReferenceType)
                {
                    throw new FrameworkException("Type is invalid.");
                }
                if (m_Factory == null) m_Factory = () => new T();
                if (count <= 0) return;

                int max = Volatile.Read(ref m_MaxCapacity);
                lock (m_References)
                {
                    int actual = count;
                    if (max > 0)
                    {
                        int room = max - m_References.Count;
                        if (room <= 0) return;
                        if (actual > room) actual = room;
                    }
                    Interlocked.Add(ref m_AddReferenceCount, actual);
                    while (actual-- > 0)
                    {
                        m_References.Enqueue(new T());
                    }
                }
            }

            public void Add(int count)
            {
                if (count <= 0) return;
                int max = Volatile.Read(ref m_MaxCapacity);
                lock (m_References)
                {
                    int actual = count;
                    if (max > 0)
                    {
                        int room = max - m_References.Count;
                        if (room <= 0) return;
                        if (actual > room) actual = room;
                    }
                    Interlocked.Add(ref m_AddReferenceCount, actual);
                    while (actual-- > 0)
                    {
                        m_References.Enqueue(CreateInstance());
                    }
                }
            }

            public void Remove(int count)
            {
                if (count <= 0) return;
                lock (m_References)
                {
                    if (count > m_References.Count) count = m_References.Count;
                    Interlocked.Add(ref m_RemoveReferenceCount, count);
                    while (count-- > 0)
                    {
                        IReference r = m_References.Dequeue();
                        if (m_StrictSet != null) m_StrictSet.Remove(r);
                        // 已经 Clear 过了
                    }
                }
            }

            /// <summary>
            /// 移除所有未使用引用。对每个引用调一次 Clear 以释放成员资源（Variable&lt;T&gt; 等）。
            /// </summary>
            public void RemoveAll()
            {
                IReference[] toClear;
                lock (m_References)
                {
                    int n = m_References.Count;
                    toClear = new IReference[n];
                    Interlocked.Add(ref m_RemoveReferenceCount, n);
                    for (int i = 0; i < n; i++)
                    {
                        toClear[i] = m_References.Dequeue();
                    }
                    if (m_StrictSet != null) m_StrictSet.Clear();
                }

                for (int i = 0; i < toClear.Length; i++)
                {
                    try { toClear[i].Clear(); }
                    catch (Exception ex)
                    {
                        FrameworkLog.Error("Reference.Clear during RemoveAll threw on type '{0}': {1}", m_ReferenceType.FullName, ex);
                    }
                }
            }

            private void TrimToCapacity()
            {
                int max = Volatile.Read(ref m_MaxCapacity);
                if (max <= 0) return;
                lock (m_References)
                {
                    while (m_References.Count > max)
                    {
                        IReference r = m_References.Dequeue();
                        if (m_StrictSet != null) m_StrictSet.Remove(r);
                        Interlocked.Increment(ref m_RemoveReferenceCount);
                    }
                }
            }

            private bool m_StrictCheckEnabled()
            {
                return ReferencePool.EnableStrictCheck;
            }
        }
    }
}
