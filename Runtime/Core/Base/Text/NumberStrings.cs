//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 整数 → string 的缓存。只在"接口必须要 string"时使用（uGUI Text.text、只收 string 的第三方 API、字典键），
    /// 让 HP、数量、等级这类小整数每次赋值不再新建字符串；能走 char[] / Span 的地方（TMP、BindableText）用 TempText。
    ///
    /// 默认缓存 [0, 1023]，按需惰性生成（首次取某个值时分配一次，之后恒返回同一实例）；范围外的值照常分配。
    /// 用 <see cref="SetCachedRange"/> 按项目需要调整（例如 [-100, 9999]），最多 1M 个。
    /// 输出与 <c>value.ToString(CultureInfo.InvariantCulture)</c> 相同。
    ///
    /// 线程契约：任意线程安全。惰性填充的竞态是良性的（两个线程可能各生成一次同值字符串，引用写入是原子的）；
    /// 调整范围会换一张新表，正在使用旧表的调用不受影响。
    /// </summary>
    public static class NumberStrings
    {
        /// <summary>默认缓存下限。</summary>
        public const int DefaultMin = 0;

        /// <summary>默认缓存上限。</summary>
        public const int DefaultMax = 1023;

        /// <summary>缓存范围的最大宽度。</summary>
        public const int MaxRangeSize = 1 << 20;

        private sealed class Table
        {
            public readonly int Min;
            public readonly string[] Strings;

            public Table(int min, int max)
            {
                Min = min;
                Strings = new string[max - min + 1];
            }
        }

        private static Table s_Table = new Table(DefaultMin, DefaultMax);

        /// <summary>当前缓存下限。</summary>
        public static int CachedMin
        {
            get { return Volatile.Read(ref s_Table).Min; }
        }

        /// <summary>当前缓存上限。</summary>
        public static int CachedMax
        {
            get
            {
                Table table = Volatile.Read(ref s_Table);
                return table.Min + table.Strings.Length - 1;
            }
        }

        /// <summary>
        /// 调整缓存范围（丢弃已缓存的字符串，按需重新生成）。
        /// </summary>
        /// <param name="min">下限。</param>
        /// <param name="max">上限，max - min + 1 不超过 <see cref="MaxRangeSize"/>。</param>
        public static void SetCachedRange(int min, int max)
        {
            if (max < min || (long)max - min + 1 > MaxRangeSize)
            {
                throw new FrameworkException("NumberStrings.SetCachedRange: invalid range [" + min + ", " + max + "] (at most " + MaxRangeSize + " values).");
            }

            Volatile.Write(ref s_Table, new Table(min, max));
        }

        /// <summary>
        /// 取整数的字符串；缓存范围内返回共享实例（零分配），范围外新建。
        /// </summary>
        /// <param name="value">整数。</param>
        /// <returns>不变文化的十进制字符串。</returns>
        public static string Get(int value)
        {
            Table table = Volatile.Read(ref s_Table);
            uint index = unchecked((uint)(value - table.Min));
            if (index >= (uint)table.Strings.Length)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            string text = table.Strings[index];
            if (text == null)
            {
                text = value.ToString(CultureInfo.InvariantCulture);
                table.Strings[index] = text;
            }

            return text;
        }

        /// <summary>
        /// 取长整数的字符串；落在 int 范围且在缓存范围内时返回共享实例。
        /// </summary>
        /// <param name="value">长整数。</param>
        /// <returns>不变文化的十进制字符串。</returns>
        public static string Get(long value)
        {
            if (value >= int.MinValue && value <= int.MaxValue)
            {
                return Get((int)value);
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
