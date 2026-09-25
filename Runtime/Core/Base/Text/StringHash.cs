//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 字符串哈希 ID：标准 32 位 FNV-1a（按 UTF-8 字节），把热路径上的字符串键换成一次整数比较。
    /// 典型用法：
    /// <code>
    /// static readonly StringHash k_MaxHp = StringHash.Of("MaxHp");   // 静态缓存，只算一次
    /// int maxHp = config.GetInt(k_MaxHp);                            // 查找只比较 uint
    /// </code>
    /// 设计理由：
    ///   - 标准 FNV-1a-32 over UTF-8：与服务端 / 工具链（Python、Go 的 fnv32a）逐位一致，与 GamePlay 的
    ///     StableHash 同值；非 ASCII 字符在循环内就地编码成 UTF-8，不分配。孤立代理项按 U+FFFD 编码，
    ///     与 <see cref="System.Text.Encoding.UTF8"/> 的替换行为一致。<see cref="Compute64(ReadOnlySpan{char})"/>
    ///     是同一族的 64 位版本（ConfigBlob 表名哈希即它）。
    ///   - 0 保留为"无"（<see cref="None"/> = default）。任何字符串若恰好哈希到 0，注册表按碰撞报告，要求改名。
    ///   - 32 位的代价是碰撞概率（n 个键约 n²/2³³：1000 个键约 0.01%，1 万个约 1.2%），因此有三道检查：
    ///       ① 开发版（编辑器 / Development Build）下经 <see cref="Of(string)"/> 生成的哈希都进
    ///          <see cref="StringHashRegistry"/>，同哈希不同名立即报错（含两个名字与修复办法）；
    ///       ② 以 StringHash 为键的容器（<see cref="StringMap{TValue}"/> 的 uniqueHashes 模式、ConfigManager）
    ///          插入时校验，碰撞键被拒绝；
    ///       ③ 编辑期 / 导出期对整批键（配置名、代码常量表）调用 <see cref="StringHashRegistry.FindCollisions"/> 一次性体检。
    ///   - 相等性只看数值：两个不同字符串若碰撞会被视为相等——这正是上面三道检查存在的原因。
    ///   - <see cref="Of(string)"/> 用于标识符（会登记）；对玩家输入等动态数据取哈希请用 <see cref="Compute(string)"/>（纯函数，不登记）。
    /// 线程契约：计算是纯函数，任意线程安全；注册表内部加锁。
    /// </summary>
    public readonly struct StringHash : IEquatable<StringHash>, IComparable<StringHash>
    {
        /// <summary>FNV-1a 32 位偏移基。空串的哈希即它。</summary>
        public const uint OffsetBasis = 2166136261u;

        /// <summary>FNV-1a 32 位质数。</summary>
        public const uint Prime = 16777619u;

        /// <summary>FNV-1a 64 位偏移基。</summary>
        public const ulong OffsetBasis64 = 14695981039346656037UL;

        /// <summary>FNV-1a 64 位质数。</summary>
        public const ulong Prime64 = 1099511628211UL;

        /// <summary>"无"，即 default(StringHash)，数值 0。</summary>
        public static readonly StringHash None = default(StringHash);

        /// <summary>哈希值。</summary>
        public readonly uint Value;

        /// <summary>
        /// 用已知数值构造（如从网络包、存档或 ConfigBlob 读出的哈希）。
        /// </summary>
        /// <param name="value">哈希值。</param>
        public StringHash(uint value)
        {
            Value = value;
        }

        /// <summary>是否为 <see cref="None"/>。</summary>
        public bool IsNone
        {
            get { return Value == 0u; }
        }

        /// <summary>
        /// 计算标识符的哈希；开发版下同时登记到 <see cref="StringHashRegistry"/> 做碰撞检测。
        /// 应缓存到 static readonly 字段，不要在每帧重复计算。
        /// </summary>
        /// <param name="text">标识符，不能为 null（空串允许）。</param>
        /// <returns>哈希 ID。</returns>
        public static StringHash Of(string text)
        {
            if (text == null)
            {
                throw new FrameworkException("StringHash.Of: text is null. Pass a non-null identifier (an empty string is allowed).");
            }

            uint hash = Append(OffsetBasis, text.AsSpan());
            if (StringHashRegistry.Enabled)
            {
                StringHashRegistry.Record(hash, text);
            }

            return new StringHash(hash);
        }

        /// <summary>
        /// 纯函数：FNV-1a-32（UTF-8）。不登记，适合对动态数据取哈希。
        /// </summary>
        /// <param name="text">待哈希字符串，不能为 null。</param>
        /// <returns>32 位哈希值。</returns>
        public static uint Compute(string text)
        {
            if (text == null)
            {
                throw new FrameworkException("StringHash.Compute: text is null.");
            }

            return Append(OffsetBasis, text.AsSpan());
        }

        /// <summary>
        /// 纯函数：FNV-1a-32（UTF-8）。不登记，零分配。
        /// </summary>
        /// <param name="text">待哈希字符切片。</param>
        /// <returns>32 位哈希值。</returns>
        public static uint Compute(ReadOnlySpan<char> text)
        {
            return Append(OffsetBasis, text);
        }

        /// <summary>
        /// 流式混入：在已有哈希上继续混入 text 的 UTF-8 字节，满足
        /// <c>Compute(a + b) == Append(Compute(a), b)</c>，拼接键无需先拼字符串。
        /// 代理对不能跨两次调用拆开（拆开的两半各自按 U+FFFD 编码，与整串结果不同）。
        /// </summary>
        /// <param name="hash">已有哈希（起点用 <see cref="OffsetBasis"/>）。</param>
        /// <param name="text">继续混入的字符。</param>
        /// <returns>新的哈希值。</returns>
        public static uint Append(uint hash, ReadOnlySpan<char> text)
        {
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    uint c = text[i];
                    if (c < 0x80u)
                    {
                        hash = (hash ^ c) * Prime;
                        continue;
                    }

                    if (c < 0x800u)
                    {
                        hash = (hash ^ (0xC0u | (c >> 6))) * Prime;
                        hash = (hash ^ (0x80u | (c & 0x3Fu))) * Prime;
                        continue;
                    }

                    if (c - 0xD800u <= 0x7FFu)
                    {
                        // 代理项区间 D800..DFFF：合法代理对编成 4 字节，孤立代理项按 U+FFFD 编码。
                        if (c <= 0xDBFFu && i + 1 < text.Length && (uint)text[i + 1] - 0xDC00u <= 0x3FFu)
                        {
                            uint cp = 0x10000u + ((c - 0xD800u) << 10) + ((uint)text[i + 1] - 0xDC00u);
                            i++;
                            hash = (hash ^ (0xF0u | (cp >> 18))) * Prime;
                            hash = (hash ^ (0x80u | ((cp >> 12) & 0x3Fu))) * Prime;
                            hash = (hash ^ (0x80u | ((cp >> 6) & 0x3Fu))) * Prime;
                            hash = (hash ^ (0x80u | (cp & 0x3Fu))) * Prime;
                            continue;
                        }

                        c = 0xFFFDu;
                    }

                    hash = (hash ^ (0xE0u | (c >> 12))) * Prime;
                    hash = (hash ^ (0x80u | ((c >> 6) & 0x3Fu))) * Prime;
                    hash = (hash ^ (0x80u | (c & 0x3Fu))) * Prime;
                }

                return hash;
            }
        }

        /// <summary>
        /// 纯函数：FNV-1a-64（UTF-8），与 ConfigBlob 表名哈希同值。碰撞概率对任何现实规模的键集合都可忽略。
        /// </summary>
        /// <param name="text">待哈希字符串，不能为 null。</param>
        /// <returns>64 位哈希值。</returns>
        public static ulong Compute64(string text)
        {
            if (text == null)
            {
                throw new FrameworkException("StringHash.Compute64: text is null.");
            }

            return Append64(OffsetBasis64, text.AsSpan());
        }

        /// <summary>
        /// 纯函数：FNV-1a-64（UTF-8），零分配。
        /// </summary>
        /// <param name="text">待哈希字符切片。</param>
        /// <returns>64 位哈希值。</returns>
        public static ulong Compute64(ReadOnlySpan<char> text)
        {
            return Append64(OffsetBasis64, text);
        }

        /// <summary>
        /// 64 位流式混入，语义同 <see cref="Append(uint, ReadOnlySpan{char})"/>。
        /// </summary>
        /// <param name="hash">已有哈希（起点用 <see cref="OffsetBasis64"/>）。</param>
        /// <param name="text">继续混入的字符。</param>
        /// <returns>新的哈希值。</returns>
        public static ulong Append64(ulong hash, ReadOnlySpan<char> text)
        {
            unchecked
            {
                for (int i = 0; i < text.Length; i++)
                {
                    uint c = text[i];
                    if (c < 0x80u)
                    {
                        hash = (hash ^ c) * Prime64;
                        continue;
                    }

                    if (c < 0x800u)
                    {
                        hash = (hash ^ (0xC0u | (c >> 6))) * Prime64;
                        hash = (hash ^ (0x80u | (c & 0x3Fu))) * Prime64;
                        continue;
                    }

                    if (c - 0xD800u <= 0x7FFu)
                    {
                        if (c <= 0xDBFFu && i + 1 < text.Length && (uint)text[i + 1] - 0xDC00u <= 0x3FFu)
                        {
                            uint cp = 0x10000u + ((c - 0xD800u) << 10) + ((uint)text[i + 1] - 0xDC00u);
                            i++;
                            hash = (hash ^ (0xF0u | (cp >> 18))) * Prime64;
                            hash = (hash ^ (0x80u | ((cp >> 12) & 0x3Fu))) * Prime64;
                            hash = (hash ^ (0x80u | ((cp >> 6) & 0x3Fu))) * Prime64;
                            hash = (hash ^ (0x80u | (cp & 0x3Fu))) * Prime64;
                            continue;
                        }

                        c = 0xFFFDu;
                    }

                    hash = (hash ^ (0xE0u | (c >> 12))) * Prime64;
                    hash = (hash ^ (0x80u | ((c >> 6) & 0x3Fu))) * Prime64;
                    hash = (hash ^ (0x80u | (c & 0x3Fu))) * Prime64;
                }

                return hash;
            }
        }

        /// <summary>
        /// 查询登记过的原始名字（仅开发版登记；Release 下通常返回 false）。用于日志与调试面板。
        /// </summary>
        /// <param name="name">原始名字。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetName(out string name)
        {
            return StringHashRegistry.TryGetName(Value, out name);
        }

        /// <summary>相等比较，只比数值。</summary>
        /// <param name="other">另一个哈希。</param>
        /// <returns>数值相等返回 true。</returns>
        public bool Equals(StringHash other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StringHash && Equals((StringHash)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)Value;
        }

        /// <summary>按数值比较，用于排序与二分。</summary>
        /// <param name="other">另一个哈希。</param>
        /// <returns>比较结果。</returns>
        public int CompareTo(StringHash other)
        {
            return Value.CompareTo(other.Value);
        }

        /// <summary>
        /// 调试字符串：登记过名字时为 "名字 (0xXXXXXXXX)"，否则为 "0xXXXXXXXX"。会分配，勿用于热路径。
        /// </summary>
        /// <returns>调试字符串。</returns>
        public override string ToString()
        {
            string hex = "0x" + Value.ToString("X8", CultureInfo.InvariantCulture);
            string name;
            return TryGetName(out name) ? name + " (" + hex + ")" : hex;
        }

        public static bool operator ==(StringHash left, StringHash right)
        {
            return left.Value == right.Value;
        }

        public static bool operator !=(StringHash left, StringHash right)
        {
            return left.Value != right.Value;
        }
    }
}
