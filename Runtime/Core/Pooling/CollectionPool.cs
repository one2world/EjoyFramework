//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 集合静态池：<c>List / HashSet / Dictionary</c> 等临时集合的租借与归还，替代热路径上的 <c>new List&lt;T&gt;()</c>。
    ///
    /// 用法（推荐 using 作用域，归还不会漏）：
    /// <code>
    /// using (ListPool&lt;int&gt;.Get(out List&lt;int&gt; hits))
    /// {
    ///     grid.Query(center, radius, hits);
    ///     ...
    /// }   // 自动 Clear + 归还
    /// </code>
    ///
    /// 设计：
    ///   • 每线程一个栈（<c>[ThreadStatic]</c>），无锁；对象在哪个线程归还就进哪个线程的栈，跨线程归还只是让池分布变化，不影响正确性。
    ///   • 归还时 Clear；每线程最多保留 <see cref="MaxRetainedPerThread"/> 个，超出丢给 GC，避免峰值后长期占内存。
    ///   • 编辑器 / 开发构建下检测重复归还（同一实例在池里出现两次会让两个调用方共用一个集合，是最难查的 bug 之一）。
    ///   • 指标为进程级累计（Interlocked），用于诊断面板与泄漏排查：<c>Rented - Released</c> 持续增长即有泄漏。
    /// </summary>
    /// <typeparam name="TCollection">集合类型，须有无参构造。</typeparam>
    /// <typeparam name="TItem">元素类型。</typeparam>
    public static class CollectionPool<TCollection, TItem> where TCollection : class, ICollection<TItem>, new()
    {
        [ThreadStatic]
        private static Stack<TCollection> ts_Pool;

        private static readonly string s_DebugName = typeof(TCollection).Name;
        private static int s_MaxRetainedPerThread = 64;
        private static long s_RentedCount;
        private static long s_ReleasedCount;
        private static long s_CreatedCount;

        /// <summary>每线程最多保留的空闲集合数。默认 64。</summary>
        public static int MaxRetainedPerThread
        {
            get { return s_MaxRetainedPerThread; }
            set { s_MaxRetainedPerThread = value < 0 ? 0 : value; }
        }

        /// <summary>累计租借次数。</summary>
        public static long RentedCount { get { return Interlocked.Read(ref s_RentedCount); } }

        /// <summary>累计归还次数。</summary>
        public static long ReleasedCount { get { return Interlocked.Read(ref s_ReleasedCount); } }

        /// <summary>累计新建次数（= 池未命中次数）。</summary>
        public static long CreatedCount { get { return Interlocked.Read(ref s_CreatedCount); } }

        /// <summary>当前线程池内空闲数量。</summary>
        public static int IdleCountOnCurrentThread { get { return ts_Pool != null ? ts_Pool.Count : 0; } }

        /// <summary>租借一个空集合。</summary>
        public static TCollection Get()
        {
            Interlocked.Increment(ref s_RentedCount);
            Stack<TCollection> pool = ts_Pool;
            if (pool != null && pool.Count > 0)
            {
                return pool.Pop();
            }

            Interlocked.Increment(ref s_CreatedCount);
            return new TCollection();
        }

        /// <summary>租借一个空集合，并返回 using 作用域句柄；Dispose 时自动归还。</summary>
        public static PooledCollection<TCollection, TItem> Get(out TCollection collection)
        {
            collection = Get();
            return new PooledCollection<TCollection, TItem>(collection);
        }

        /// <summary>归还集合（内部 Clear）。null 忽略。</summary>
        public static void Release(TCollection collection)
        {
            if (collection == null)
            {
                return;
            }

            Stack<TCollection> pool = ts_Pool;
            if (pool == null)
            {
                pool = new Stack<TCollection>();
                ts_Pool = pool;
            }

            PoolDebug.ThrowIfAlreadyPooled(pool, collection, s_DebugName);

            collection.Clear();
            Interlocked.Increment(ref s_ReleasedCount);
            if (pool.Count < s_MaxRetainedPerThread)
            {
                pool.Push(collection);
            }
        }

        /// <summary>清空当前线程的空闲栈（关卡切换等主动回收时机）。</summary>
        public static void ClearCurrentThread()
        {
            if (ts_Pool != null)
            {
                ts_Pool.Clear();
            }
        }
    }

    /// <summary>集合池的 using 作用域句柄；Dispose 归还。readonly struct，不装箱、不分配。</summary>
    public readonly struct PooledCollection<TCollection, TItem> : IDisposable where TCollection : class, ICollection<TItem>, new()
    {
        private readonly TCollection m_Collection;

        public PooledCollection(TCollection collection)
        {
            m_Collection = collection;
        }

        /// <summary>被租借的集合。</summary>
        public TCollection Collection
        {
            get { return m_Collection; }
        }

        public void Dispose()
        {
            CollectionPool<TCollection, TItem>.Release(m_Collection);
        }
    }

    /// <summary><c>List&lt;T&gt;</c> 池。</summary>
    public static class ListPool<T>
    {
        public static List<T> Get() { return CollectionPool<List<T>, T>.Get(); }
        public static PooledCollection<List<T>, T> Get(out List<T> list) { return CollectionPool<List<T>, T>.Get(out list); }
        public static void Release(List<T> list) { CollectionPool<List<T>, T>.Release(list); }
    }

    /// <summary><c>HashSet&lt;T&gt;</c> 池。</summary>
    public static class HashSetPool<T>
    {
        public static HashSet<T> Get() { return CollectionPool<HashSet<T>, T>.Get(); }
        public static PooledCollection<HashSet<T>, T> Get(out HashSet<T> set) { return CollectionPool<HashSet<T>, T>.Get(out set); }
        public static void Release(HashSet<T> set) { CollectionPool<HashSet<T>, T>.Release(set); }
    }

    /// <summary><c>Dictionary&lt;TKey, TValue&gt;</c> 池。</summary>
    public static class DictionaryPool<TKey, TValue>
    {
        public static Dictionary<TKey, TValue> Get() { return CollectionPool<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>.Get(); }
        public static PooledCollection<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>> Get(out Dictionary<TKey, TValue> dictionary) { return CollectionPool<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>.Get(out dictionary); }
        public static void Release(Dictionary<TKey, TValue> dictionary) { CollectionPool<Dictionary<TKey, TValue>, KeyValuePair<TKey, TValue>>.Release(dictionary); }
    }

    /// <summary>
    /// 池的调试检查。只在编辑器 / 开发构建编译，Player 正式构建零开销（与 <see cref="Framework.EnsureMainThread"/> 同一策略）。
    /// </summary>
    internal static class PoolDebug
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("DEBUG")]
        public static void ThrowIfAlreadyPooled<T>(Stack<T> pool, T item, string poolName) where T : class
        {
            foreach (T pooled in pool)
            {
                if (ReferenceEquals(pooled, item))
                {
                    throw new FrameworkException(Utility.Text.Format(
                        "{0} 池：同一实例被重复归还。重复归还会让两个调用方共用一个集合并互相覆盖数据，请检查 using 作用域或手动 Release 的配对。", poolName));
                }
            }
        }
    }
}
