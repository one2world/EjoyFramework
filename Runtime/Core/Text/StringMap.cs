//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 以字符串为键的哈希表，可用 <c>string</c> / <c>ReadOnlySpan&lt;char&gt;</c> / <see cref="StringHash"/> 三种方式零分配查找。
    /// 解决 <see cref="Dictionary{TKey,TValue}"/> 在 netstandard2.1 下无法用字符切片查找的问题：路径逐段查找、
    /// 解析文本时按切片查表、拼接键（如 "key.one"）在栈缓冲里组好直接查，都不必先生成 string。
    ///
    /// 设计理由：
    ///   - 哈希函数就是 <see cref="StringHash"/>（FNV-1a-32，UTF-8），因此 <c>map.TryGetValue(k_MaxHp)</c> 与
    ///     <c>map.TryGetValue("MaxHp")</c> 命中同一条目；比较按序数（ordinal）逐字符。
    ///   - 两种模式：
    ///       · 默认：允许不同键同哈希（按字符比较区分），适合本地化这类键量大、键由内容决定的表；
    ///         此模式禁止按 StringHash 查找（无法消歧），调用会抛出并说明原因。
    ///       · uniqueHashes：插入时拒绝与已有键同哈希的新键（<see cref="Add"/> 抛出、<see cref="TryAdd"/> 返回 false），
    ///         由此保证按 StringHash 查找唯一确定——配置名、黑板键这类受控标识符用它。
    ///   - 存储布局与 Dictionary 相同（桶 + 条目数组 + 空闲链），无删除时枚举顺序即插入顺序；桶数为 2 的幂，
    ///     用 Fibonacci 散列取桶，避免 FNV 低位在 2 的幂取模下的聚集。
    ///   - 查找、枚举、覆盖写零分配；仅扩容分配。
    /// 线程契约：非线程安全；无并发写时可多线程并发读（同 Dictionary）。
    /// </summary>
    /// <typeparam name="TValue">值类型。</typeparam>
    public sealed class StringMap<TValue>
    {
        private const uint FibonacciMultiplier = 2654435769u;
        private const int MinBucketCount = 4;

        private struct Entry
        {
            public uint Hash;
            public int Next;          // 同桶链的下一条；空闲条目里是空闲链的下一条
            public string Key;        // null 表示空闲条目
            public TValue Value;
        }

        private readonly bool m_UniqueHashes;
        private int[] m_Buckets;      // 条目下标 + 1，0 表示空桶
        private Entry[] m_Entries;
        private int m_Shift;
        private int m_Used;           // 已使用过的条目高水位（含空闲）
        private int m_FreeList = -1;
        private int m_FreeCount;
        private int m_Version;

        /// <summary>
        /// 构造默认模式（允许哈希碰撞）的空表。
        /// </summary>
        public StringMap()
            : this(0, false)
        {
        }

        /// <summary>
        /// 构造空表。
        /// </summary>
        /// <param name="capacity">初始容量（条目数），小于等于 0 时首次插入再分配。</param>
        /// <param name="uniqueHashes">true：拒绝同哈希的不同键，允许按 <see cref="StringHash"/> 查找。</param>
        public StringMap(int capacity, bool uniqueHashes = false)
        {
            if (capacity < 0)
            {
                throw new FrameworkException("StringMap capacity must be non-negative.");
            }

            m_UniqueHashes = uniqueHashes;
            if (capacity > 0)
            {
                Initialize(capacity);
            }
        }

        /// <summary>条目数。</summary>
        public int Count
        {
            get { return m_Used - m_FreeCount; }
        }

        /// <summary>是否为 uniqueHashes 模式。</summary>
        public bool UniqueHashes
        {
            get { return m_UniqueHashes; }
        }

        /// <summary>
        /// 读写值。读不存在的键抛出；写入时新增或覆盖（uniqueHashes 模式下与其他键同哈希则抛出）。
        /// </summary>
        /// <param name="key">键。</param>
        public TValue this[string key]
        {
            get
            {
                TValue value;
                if (TryGetValue(key, out value))
                {
                    return value;
                }

                throw new FrameworkException("StringMap: key '" + key + "' was not found.");
            }
            set
            {
                Insert(key, value, InsertMode.Overwrite);
            }
        }

        /// <summary>
        /// 新增条目。键已存在、或 uniqueHashes 模式下与其他键同哈希时抛出 <see cref="FrameworkException"/>。
        /// </summary>
        /// <param name="key">键，不能为 null。</param>
        /// <param name="value">值。</param>
        public void Add(string key, TValue value)
        {
            Insert(key, value, InsertMode.Throw);
        }

        /// <summary>
        /// 尝试新增条目。键已存在、或 uniqueHashes 模式下与其他键同哈希时返回 false（可用
        /// <see cref="ContainsKey(string)"/> 区分两种情况，<see cref="TryGetKey(StringHash, out string)"/> 取回碰撞的键）。
        /// </summary>
        /// <param name="key">键，不能为 null。</param>
        /// <param name="value">值。</param>
        /// <returns>新增成功返回 true。</returns>
        public bool TryAdd(string key, TValue value)
        {
            return Insert(key, value, InsertMode.Try);
        }

        /// <summary>是否包含键。</summary>
        /// <param name="key">键，null 返回 false。</param>
        /// <returns>包含返回 true。</returns>
        public bool ContainsKey(string key)
        {
            return key != null && FindIndex(key) >= 0;
        }

        /// <summary>是否包含键（按字符切片）。</summary>
        /// <param name="key">键。</param>
        /// <returns>包含返回 true。</returns>
        public bool ContainsKey(ReadOnlySpan<char> key)
        {
            return FindIndex(key, StringHash.Compute(key)) >= 0;
        }

        /// <summary>是否包含该哈希的键（仅 uniqueHashes 模式）。</summary>
        /// <param name="key">哈希键。</param>
        /// <returns>包含返回 true。</returns>
        public bool ContainsKey(StringHash key)
        {
            return FindIndex(key) >= 0;
        }

        /// <summary>按字符串查找。</summary>
        /// <param name="key">键，null 返回 false。</param>
        /// <param name="value">找到的值。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetValue(string key, out TValue value)
        {
            int index = key != null ? FindIndex(key) : -1;
            if (index >= 0)
            {
                value = m_Entries[index].Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>按字符切片查找，零分配。</summary>
        /// <param name="key">键。</param>
        /// <param name="value">找到的值。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetValue(ReadOnlySpan<char> key, out TValue value)
        {
            int index = FindIndex(key, StringHash.Compute(key));
            if (index >= 0)
            {
                value = m_Entries[index].Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// 按 <see cref="StringHash"/> 查找（只比较整数）。仅 uniqueHashes 模式可用，否则抛出——
        /// 默认模式允许碰撞，按哈希查找无法确定是哪个键。
        /// </summary>
        /// <param name="key">哈希键。</param>
        /// <param name="value">找到的值。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetValue(StringHash key, out TValue value)
        {
            int index = FindIndex(key);
            if (index >= 0)
            {
                value = m_Entries[index].Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// 按哈希取回键（仅 uniqueHashes 模式），用于碰撞报错与调试显示。
        /// </summary>
        /// <param name="key">哈希键。</param>
        /// <param name="storedKey">表内的键实例。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetKey(StringHash key, out string storedKey)
        {
            int index = FindIndex(key);
            storedKey = index >= 0 ? m_Entries[index].Key : null;
            return index >= 0;
        }

        /// <summary>移除键。</summary>
        /// <param name="key">键，null 返回 false。</param>
        /// <returns>移除成功返回 true。</returns>
        public bool Remove(string key)
        {
            return key != null && RemoveCore(key.AsSpan(), StringHash.Compute(key.AsSpan()));
        }

        /// <summary>按字符切片移除键。</summary>
        /// <param name="key">键。</param>
        /// <returns>移除成功返回 true。</returns>
        public bool Remove(ReadOnlySpan<char> key)
        {
            return RemoveCore(key, StringHash.Compute(key));
        }

        /// <summary>清空全部条目，保留容量。</summary>
        public void Clear()
        {
            if (m_Used == 0)
            {
                return;
            }

            Array.Clear(m_Buckets, 0, m_Buckets.Length);
            Array.Clear(m_Entries, 0, m_Used);
            m_Used = 0;
            m_FreeList = -1;
            m_FreeCount = 0;
            m_Version++;
        }

        /// <summary>获取零分配枚举器。枚举期间修改表会在下一次 MoveNext 时抛出。</summary>
        /// <returns>枚举器。</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private enum InsertMode
        {
            Throw,
            Try,
            Overwrite,
        }

        private bool Insert(string key, TValue value, InsertMode mode)
        {
            if (key == null)
            {
                throw new FrameworkException("StringMap: key is null.");
            }

            if (m_Buckets == null)
            {
                Initialize(MinBucketCount);
            }

            uint hash = StringHash.Compute(key.AsSpan());
            int bucket = BucketOf(hash);
            for (int i = m_Buckets[bucket] - 1; i >= 0; i = m_Entries[i].Next)
            {
                if (m_Entries[i].Hash != hash)
                {
                    continue;
                }

                if (string.Equals(m_Entries[i].Key, key, StringComparison.Ordinal))
                {
                    if (mode == InsertMode.Overwrite)
                    {
                        m_Entries[i].Value = value;
                        m_Version++;
                        return true;
                    }

                    if (mode == InsertMode.Throw)
                    {
                        throw new FrameworkException("StringMap: key '" + key + "' already exists.");
                    }

                    return false;
                }

                if (m_UniqueHashes)
                {
                    if (mode == InsertMode.Try)
                    {
                        return false;
                    }

                    throw new FrameworkException("StringMap: key '" + key + "' has the same StringHash 0x" + hash.ToString("X8") +
                                                 " as existing key '" + m_Entries[i].Key +
                                                 "'; this map requires unique hashes. Rename one of the keys.");
                }
            }

            int index;
            if (m_FreeCount > 0)
            {
                index = m_FreeList;
                m_FreeList = m_Entries[index].Next;
                m_FreeCount--;
            }
            else
            {
                if (m_Used == m_Entries.Length)
                {
                    Resize(m_Entries.Length * 2);
                    bucket = BucketOf(hash);
                }

                index = m_Used;
                m_Used++;
            }

            m_Entries[index].Hash = hash;
            m_Entries[index].Key = key;
            m_Entries[index].Value = value;
            m_Entries[index].Next = m_Buckets[bucket] - 1;
            m_Buckets[bucket] = index + 1;
            m_Version++;
            return true;
        }

        private int FindIndex(string key)
        {
            if (m_Buckets == null)
            {
                return -1;
            }

            uint hash = StringHash.Compute(key.AsSpan());
            for (int i = m_Buckets[BucketOf(hash)] - 1; i >= 0; i = m_Entries[i].Next)
            {
                if (m_Entries[i].Hash == hash)
                {
                    string stored = m_Entries[i].Key;
                    if (ReferenceEquals(stored, key) || string.Equals(stored, key, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private int FindIndex(ReadOnlySpan<char> key, uint hash)
        {
            if (m_Buckets == null)
            {
                return -1;
            }

            for (int i = m_Buckets[BucketOf(hash)] - 1; i >= 0; i = m_Entries[i].Next)
            {
                if (m_Entries[i].Hash == hash && m_Entries[i].Key.AsSpan().SequenceEqual(key))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindIndex(StringHash key)
        {
            if (!m_UniqueHashes)
            {
                throw new FrameworkException("StringMap: StringHash lookups require a map constructed with uniqueHashes: true " +
                                             "(new StringMap<T>(capacity, uniqueHashes: true)); otherwise look up by string or span.");
            }

            if (m_Buckets == null)
            {
                return -1;
            }

            uint hash = key.Value;
            for (int i = m_Buckets[BucketOf(hash)] - 1; i >= 0; i = m_Entries[i].Next)
            {
                if (m_Entries[i].Hash == hash)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool RemoveCore(ReadOnlySpan<char> key, uint hash)
        {
            if (m_Buckets == null)
            {
                return false;
            }

            int bucket = BucketOf(hash);
            int previous = -1;
            for (int i = m_Buckets[bucket] - 1; i >= 0; previous = i, i = m_Entries[i].Next)
            {
                if (m_Entries[i].Hash != hash || !m_Entries[i].Key.AsSpan().SequenceEqual(key))
                {
                    continue;
                }

                if (previous < 0)
                {
                    m_Buckets[bucket] = m_Entries[i].Next + 1;
                }
                else
                {
                    m_Entries[previous].Next = m_Entries[i].Next;
                }

                m_Entries[i].Hash = 0u;
                m_Entries[i].Key = null;
                m_Entries[i].Value = default(TValue);
                m_Entries[i].Next = m_FreeList;
                m_FreeList = i;
                m_FreeCount++;
                m_Version++;
                return true;
            }

            return false;
        }

        private int BucketOf(uint hash)
        {
            return (int)(unchecked(hash * FibonacciMultiplier) >> m_Shift);
        }

        private void Initialize(int capacity)
        {
            int size = MinBucketCount;
            while (size < capacity)
            {
                if (size >= (1 << 30))
                {
                    throw new FrameworkException("StringMap capacity is too large.");
                }

                size <<= 1;
            }

            m_Buckets = new int[size];
            m_Entries = new Entry[size];
            m_Shift = 32 - Log2(size);
        }

        private void Resize(int newSize)
        {
            if (newSize <= 0 || newSize > (1 << 30))
            {
                throw new FrameworkException("StringMap capacity is too large.");
            }

            Entry[] entries = new Entry[newSize];
            Array.Copy(m_Entries, entries, m_Used);
            m_Buckets = new int[newSize];
            m_Shift = 32 - Log2(newSize);
            for (int i = 0; i < m_Used; i++)
            {
                if (entries[i].Key == null)
                {
                    continue;
                }

                int bucket = BucketOf(entries[i].Hash);
                entries[i].Next = m_Buckets[bucket] - 1;
                m_Buckets[bucket] = i + 1;
            }

            m_Entries = entries;
        }

        private static int Log2(int powerOfTwo)
        {
            int log = 0;
            while ((1 << log) < powerOfTwo)
            {
                log++;
            }

            return log;
        }

        /// <summary>
        /// 零分配枚举器，按条目数组顺序（无删除时即插入顺序）。
        /// </summary>
        public struct Enumerator
        {
            private readonly StringMap<TValue> m_Map;
            private readonly int m_Version;
            private int m_Index;
            private KeyValuePair<string, TValue> m_Current;

            internal Enumerator(StringMap<TValue> map)
            {
                m_Map = map;
                m_Version = map.m_Version;
                m_Index = 0;
                m_Current = default(KeyValuePair<string, TValue>);
            }

            /// <summary>当前条目。</summary>
            public KeyValuePair<string, TValue> Current
            {
                get { return m_Current; }
            }

            /// <summary>前进到下一个条目。</summary>
            /// <returns>还有条目返回 true。</returns>
            public bool MoveNext()
            {
                if (m_Version != m_Map.m_Version)
                {
                    throw new FrameworkException("StringMap was modified during enumeration.");
                }

                while (m_Index < m_Map.m_Used)
                {
                    int i = m_Index++;
                    if (m_Map.m_Entries[i].Key != null)
                    {
                        m_Current = new KeyValuePair<string, TValue>(m_Map.m_Entries[i].Key, m_Map.m_Entries[i].Value);
                        return true;
                    }
                }

                m_Current = default(KeyValuePair<string, TValue>);
                return false;
            }
        }
    }
}
