//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 数组租借池（ArrayPool 语义）：按 2 的幂分桶，<see cref="Rent"/> 返回长度不小于请求值的数组，
    /// <see cref="Return"/> 归还。用于 Network 收发帧、Download 分片、Save 封包、ByteBuffer 扩容等
    /// 目前各自 <c>new T[]</c> 的路径。
    ///
    /// 契约（务必遵守）：
    ///   • 返回数组长度 ≥ 请求长度，调用方必须用自己记录的长度而不是 <c>array.Length</c>。
    ///   • 归还后不得再触碰该数组；重复归还在编辑器 / 开发构建下抛出。
    ///   • 只有本池租出的数组（长度为桶尺寸）才会被收纳；其它数组的 Return 被静默忽略——
    ///     这让"不确定来源"的数组也可以安全地 Return，但不要指望它们被复用。
    ///   • 内容不清零（性能）；承载敏感数据时用 <c>Return(array, clearArray: true)</c>。
    ///
    /// 线程安全：每桶一把锁，临界区只做一次栈操作；租借与归还可来自不同线程（网络线程收、主线程处理）。
    /// 超过 <see cref="MaxPooledLength"/> 的请求直接分配、不入池（大块内存不该长期囤在池里）。
    /// </summary>
    public static class BufferPool<T>
    {
        /// <summary>最小桶长度（2^4）。</summary>
        public const int MinPooledLength = 16;

        /// <summary>最大桶长度（2^24 = 16M 元素）。</summary>
        public const int MaxPooledLength = 1 << 24;

        private const int MinShift = 4;
        private const int BucketCount = 24 - MinShift + 1;

        private static readonly Stack<T[]>[] s_Buckets = CreateBuckets();
        private static readonly object[] s_Locks = CreateLocks();
        private static readonly string s_DebugName = "BufferPool<" + typeof(T).Name + ">";
        private static int s_MaxRetainedPerBucket = 16;
        private static long s_RentedCount;
        private static long s_ReturnedCount;
        private static long s_CreatedCount;
        private static long s_DroppedCount;

        /// <summary>每个桶最多保留的空闲数组数。默认 16。</summary>
        public static int MaxRetainedPerBucket
        {
            get { return s_MaxRetainedPerBucket; }
            set { s_MaxRetainedPerBucket = value < 0 ? 0 : value; }
        }

        /// <summary>累计租借次数。</summary>
        public static long RentedCount { get { return Interlocked.Read(ref s_RentedCount); } }

        /// <summary>累计归还并入池次数。</summary>
        public static long ReturnedCount { get { return Interlocked.Read(ref s_ReturnedCount); } }

        /// <summary>累计新建数组次数（池未命中 + 超大请求）。</summary>
        public static long CreatedCount { get { return Interlocked.Read(ref s_CreatedCount); } }

        /// <summary>累计因桶已满 / 尺寸不匹配而丢弃的归还次数。</summary>
        public static long DroppedCount { get { return Interlocked.Read(ref s_DroppedCount); } }

        /// <summary>租借长度不小于 <paramref name="minimumLength"/> 的数组。0 返回 <c>Array.Empty</c>。</summary>
        public static T[] Rent(int minimumLength)
        {
            if (minimumLength < 0)
            {
                throw new FrameworkException("BufferPool.Rent：minimumLength 不能为负数。");
            }

            if (minimumLength == 0)
            {
                return Array.Empty<T>();
            }

            Interlocked.Increment(ref s_RentedCount);
            if (minimumLength > MaxPooledLength)
            {
                Interlocked.Increment(ref s_CreatedCount);
                return new T[minimumLength];
            }

            int bucket = BucketIndex(minimumLength);
            Stack<T[]> stack = s_Buckets[bucket];
            lock (s_Locks[bucket])
            {
                if (stack.Count > 0)
                {
                    return stack.Pop();
                }
            }

            Interlocked.Increment(ref s_CreatedCount);
            return new T[MinPooledLength << bucket];
        }

        /// <summary>租借并返回 using 作用域句柄；Dispose 时归还。</summary>
        public static PooledBuffer<T> Rent(int minimumLength, out T[] array)
        {
            array = Rent(minimumLength);
            return new PooledBuffer<T>(array);
        }

        /// <summary>归还数组。非本池尺寸的数组被忽略；<paramref name="clearArray"/> 为 true 时先清零。</summary>
        public static void Return(T[] array, bool clearArray = false)
        {
            if (array == null || array.Length == 0)
            {
                return;
            }

            int length = array.Length;
            if (length < MinPooledLength || length > MaxPooledLength || (length & (length - 1)) != 0)
            {
                Interlocked.Increment(ref s_DroppedCount);
                return;
            }

            if (clearArray)
            {
                Array.Clear(array, 0, length);
            }

            int bucket = BucketIndex(length);
            Stack<T[]> stack = s_Buckets[bucket];
            lock (s_Locks[bucket])
            {
                PoolDebug.ThrowIfAlreadyPooled(stack, array, s_DebugName);
                if (stack.Count >= s_MaxRetainedPerBucket)
                {
                    Interlocked.Increment(ref s_DroppedCount);
                    return;
                }

                stack.Push(array);
            }

            Interlocked.Increment(ref s_ReturnedCount);
        }

        /// <summary>清空所有桶（关卡切换等主动回收时机）。</summary>
        public static void Clear()
        {
            for (int i = 0; i < BucketCount; i++)
            {
                lock (s_Locks[i])
                {
                    s_Buckets[i].Clear();
                }
            }
        }

        /// <summary>某桶当前空闲数组数（诊断用）。</summary>
        public static int GetIdleCount(int bucketLength)
        {
            if (bucketLength < MinPooledLength || bucketLength > MaxPooledLength || (bucketLength & (bucketLength - 1)) != 0)
            {
                return 0;
            }

            int bucket = BucketIndex(bucketLength);
            lock (s_Locks[bucket])
            {
                return s_Buckets[bucket].Count;
            }
        }

        /// <summary>桶序号：满足 16 &lt;&lt; index ≥ length 的最小 index。</summary>
        private static int BucketIndex(int length)
        {
            int index = 0;
            int size = MinPooledLength;
            while (size < length)
            {
                size <<= 1;
                index++;
            }

            return index;
        }

        private static Stack<T[]>[] CreateBuckets()
        {
            var buckets = new Stack<T[]>[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                buckets[i] = new Stack<T[]>();
            }

            return buckets;
        }

        private static object[] CreateLocks()
        {
            var locks = new object[BucketCount];
            for (int i = 0; i < BucketCount; i++)
            {
                locks[i] = new object();
            }

            return locks;
        }
    }

    /// <summary>数组池的 using 作用域句柄；Dispose 归还（不清零）。</summary>
    public readonly struct PooledBuffer<T> : IDisposable
    {
        private readonly T[] m_Array;

        public PooledBuffer(T[] array)
        {
            m_Array = array;
        }

        public T[] Array
        {
            get { return m_Array; }
        }

        public void Dispose()
        {
            BufferPool<T>.Return(m_Array);
        }
    }
}
