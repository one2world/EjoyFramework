//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core
{
    /// <summary>
    /// 分级 char 缓冲池，供 <see cref="TempText"/> 等零 GC 字符串构建设施使用。
    /// 设计理由：
    ///   - 使用 [ThreadStatic] 每线程独立缓存，租借/归还全程无锁、无原子操作，主线程热路径开销最小。
    ///   - 采用 64/256/1024/4096 四档固定容量，归还时按长度精确归档，避免"大缓冲被当小缓冲复用"造成的内存放大。
    ///   - 超过最大档位的请求直接 new 并且不回收：这类超大缓冲通常是偶发的，长期缓存会永久占用内存。
    ///   - 每档只缓存少量（<see cref="MaxPerTier"/>）个，防止池本身变成内存泄漏源。
    /// 注意：归还后不得再持有该缓冲的引用，否则会与后续租借者产生数据竞争。
    /// </summary>
    public static class CharBufferPool
    {
        /// <summary>
        /// 各档位容量，必须升序排列。
        /// </summary>
        private static readonly int[] s_TierSizes = new int[] { 64, 256, 1024, 4096 };

        /// <summary>
        /// 每档最多缓存的缓冲个数。
        /// </summary>
        private const int MaxPerTier = 4;

        [System.ThreadStatic]
        private static char[][] s_Buffers;

        [System.ThreadStatic]
        private static int[] s_Counts;

        /// <summary>
        /// 池能够缓存的最大缓冲容量，超过此值的请求不走缓存。
        /// </summary>
        public static int MaxPooledCapacity
        {
            get { return s_TierSizes[s_TierSizes.Length - 1]; }
        }

        /// <summary>
        /// 租借一个容量不小于 minCapacity 的 char 缓冲。
        /// </summary>
        /// <param name="minCapacity">所需的最小容量，小于 1 时按 1 处理。</param>
        /// <returns>可写的 char 缓冲，其长度总是等于某个档位容量或恰好等于 minCapacity（超大请求）。</returns>
        public static char[] Rent(int minCapacity)
        {
            if (minCapacity < 1)
            {
                minCapacity = 1;
            }

            int tier = GetTier(minCapacity);
            if (tier < 0)
            {
                // 超过最大档位，直接分配且不纳入缓存。
                return new char[minCapacity];
            }

            EnsureStorage();
            int count = s_Counts[tier];
            if (count > 0)
            {
                int slot = tier * MaxPerTier + count - 1;
                char[] buffer = s_Buffers[slot];
                s_Buffers[slot] = null;
                s_Counts[tier] = count - 1;
                if (buffer != null)
                {
                    return buffer;
                }
            }

            return new char[s_TierSizes[tier]];
        }

        /// <summary>
        /// 归还缓冲。仅当长度恰好匹配某个档位且该档未满时才会被缓存，其余情况静默丢弃交给 GC。
        /// </summary>
        /// <param name="buffer">待归还的缓冲，允许为 null。</param>
        public static void Return(char[] buffer)
        {
            if (buffer == null)
            {
                return;
            }

            int tier = GetExactTier(buffer.Length);
            if (tier < 0)
            {
                return;
            }

            EnsureStorage();
            int count = s_Counts[tier];
            if (count >= MaxPerTier)
            {
                return;
            }

            s_Buffers[tier * MaxPerTier + count] = buffer;
            s_Counts[tier] = count + 1;
        }

        /// <summary>
        /// 清空当前线程缓存的全部缓冲，一般仅在测试或内存告警时调用。
        /// </summary>
        public static void Clear()
        {
            if (s_Buffers == null)
            {
                return;
            }

            for (int i = 0; i < s_Buffers.Length; i++)
            {
                s_Buffers[i] = null;
            }

            for (int i = 0; i < s_Counts.Length; i++)
            {
                s_Counts[i] = 0;
            }
        }

        /// <summary>
        /// 获取当前线程指定档位已缓存的缓冲数量，供测试与调试使用。
        /// </summary>
        /// <param name="tierIndex">档位下标。</param>
        /// <returns>缓存数量，下标非法时返回 0。</returns>
        public static int GetCachedCount(int tierIndex)
        {
            if (s_Counts == null || tierIndex < 0 || tierIndex >= s_TierSizes.Length)
            {
                return 0;
            }

            return s_Counts[tierIndex];
        }

        private static void EnsureStorage()
        {
            if (s_Buffers == null)
            {
                s_Buffers = new char[s_TierSizes.Length * MaxPerTier][];
                s_Counts = new int[s_TierSizes.Length];
            }
        }

        private static int GetTier(int minCapacity)
        {
            for (int i = 0; i < s_TierSizes.Length; i++)
            {
                if (minCapacity <= s_TierSizes[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private static int GetExactTier(int length)
        {
            for (int i = 0; i < s_TierSizes.Length; i++)
            {
                if (length == s_TierSizes[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
