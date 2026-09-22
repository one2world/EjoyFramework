//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// <see cref="StringBuilder"/> 静态池。用法与 <see cref="ListPool{T}"/> 一致，另提供 <see cref="GetStringAndRelease"/>
    /// 一步完成"取字符串 + 归还"。
    ///
    /// 与集合池的差别：StringBuilder 的内部块会随内容膨胀，归还时容量超过 <see cref="MaxRetainedCapacity"/> 的实例
    /// 直接丢弃，避免一次超长拼接让池里长期囤着大块内存。
    /// </summary>
    public static class StringBuilderPool
    {
        [ThreadStatic]
        private static Stack<StringBuilder> ts_Pool;

        private static int s_MaxRetainedPerThread = 16;
        private static int s_MaxRetainedCapacity = 8 * 1024;
        private static long s_RentedCount;
        private static long s_ReleasedCount;
        private static long s_CreatedCount;

        /// <summary>每线程最多保留数。默认 16。</summary>
        public static int MaxRetainedPerThread
        {
            get { return s_MaxRetainedPerThread; }
            set { s_MaxRetainedPerThread = value < 0 ? 0 : value; }
        }

        /// <summary>归还时容量超过此值的实例不入池。默认 8K 字符。</summary>
        public static int MaxRetainedCapacity
        {
            get { return s_MaxRetainedCapacity; }
            set { s_MaxRetainedCapacity = value < 0 ? 0 : value; }
        }

        public static long RentedCount { get { return Interlocked.Read(ref s_RentedCount); } }
        public static long ReleasedCount { get { return Interlocked.Read(ref s_ReleasedCount); } }
        public static long CreatedCount { get { return Interlocked.Read(ref s_CreatedCount); } }

        /// <summary>租借一个空 StringBuilder。</summary>
        public static StringBuilder Get()
        {
            Interlocked.Increment(ref s_RentedCount);
            Stack<StringBuilder> pool = ts_Pool;
            if (pool != null && pool.Count > 0)
            {
                return pool.Pop();
            }

            Interlocked.Increment(ref s_CreatedCount);
            return new StringBuilder();
        }

        /// <summary>租借并返回 using 作用域句柄。</summary>
        public static PooledStringBuilder Get(out StringBuilder builder)
        {
            builder = Get();
            return new PooledStringBuilder(builder);
        }

        /// <summary>归还（Length 清零）。null 忽略。</summary>
        public static void Release(StringBuilder builder)
        {
            if (builder == null)
            {
                return;
            }

            Stack<StringBuilder> pool = ts_Pool;
            if (pool == null)
            {
                pool = new Stack<StringBuilder>();
                ts_Pool = pool;
            }

            PoolDebug.ThrowIfAlreadyPooled(pool, builder, "StringBuilder");

            Interlocked.Increment(ref s_ReleasedCount);
            if (builder.Capacity > s_MaxRetainedCapacity || pool.Count >= s_MaxRetainedPerThread)
            {
                return;
            }

            builder.Length = 0;
            pool.Push(builder);
        }

        /// <summary>取出字符串并归还 builder。</summary>
        public static string GetStringAndRelease(StringBuilder builder)
        {
            if (builder == null)
            {
                throw new FrameworkException("StringBuilderPool.GetStringAndRelease：builder 不能为 null。");
            }

            string result = builder.ToString();
            Release(builder);
            return result;
        }

        /// <summary>清空当前线程的空闲栈。</summary>
        public static void ClearCurrentThread()
        {
            if (ts_Pool != null)
            {
                ts_Pool.Clear();
            }
        }
    }

    /// <summary>StringBuilder 池的 using 作用域句柄。</summary>
    public readonly struct PooledStringBuilder : IDisposable
    {
        private readonly StringBuilder m_Builder;

        public PooledStringBuilder(StringBuilder builder)
        {
            m_Builder = builder;
        }

        public StringBuilder Builder
        {
            get { return m_Builder; }
        }

        public void Dispose()
        {
            StringBuilderPool.Release(m_Builder);
        }
    }
}
