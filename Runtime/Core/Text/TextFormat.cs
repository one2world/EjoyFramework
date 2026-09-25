//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 游戏 UI 高频数字的零分配格式化：紧凑数字（1.2K / 1.2万）、时长（倒计时 / 在线时长）、百分比（进度）。
    /// 全部写入调用方提供的 Span，空间不足返回 false 且不写半截（TempText 的 AppendCompact / AppendDuration /
    /// AppendPercent 会自动扩容重试）。纯函数，任意线程安全。
    /// </summary>
    public static class TextFormat
    {
        private static readonly ulong[] s_Pow10 =
        {
            1UL, 10UL, 100UL, 1000UL, 10000UL, 100000UL, 1000000UL,
        };

        private const int MaxPercentDecimals = 6;

        /// <summary>2^53：double 能精确表示的最大整数。</summary>
        private const double MaxExactInteger = 9007199254740992.0;

        /// <summary>
        /// 紧凑数字：|value| 低于门槛时写完整整数（可选千分位），否则取不超过它的最大单位，
        /// <b>截断</b>（不四舍五入）到允许的小数位——1999 显示 "1.9K" 而不是 "2K"，玩家看到的数永远不多于实际。
        /// 负数输出前导 '-'，long.MinValue 也正确。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="destination">目标缓冲。</param>
        /// <param name="charsWritten">写入字符数。</param>
        /// <param name="format">紧凑格式，null 为 <see cref="CompactNumberFormat.Western"/>。</param>
        /// <returns>空间足够并写入返回 true。</returns>
        public static bool TryFormatCompact(long value, Span<char> destination, out int charsWritten, CompactNumberFormat format = null)
        {
            if (format == null)
            {
                format = CompactNumberFormat.Western;
            }

            charsWritten = 0;
            bool negative = value < 0;
            ulong magnitude = negative ? (ulong)(-(value + 1)) + 1UL : (ulong)value;
            SpanWriter writer = new SpanWriter(destination);
            if (negative && !writer.Write('-'))
            {
                return false;
            }

            if (magnitude < (ulong)format.Threshold)
            {
                if (!writer.WriteGrouped(magnitude, format.GroupSeparator))
                {
                    return false;
                }

                charsWritten = writer.Position;
                return true;
            }

            int unit = format.UnitCount - 1;
            while (unit > 0 && magnitude < (ulong)format.GetDivisor(unit))
            {
                unit--;
            }

            ulong divisor = (ulong)format.GetDivisor(unit);
            ulong integer = magnitude / divisor;
            ulong remainder = magnitude - integer * divisor;
            int decimals = format.MaxDecimals;
            if (format.MaxSignificantDigits > 0)
            {
                decimals = Math.Min(decimals, Math.Max(0, format.MaxSignificantDigits - CountDigits(integer)));
            }

            ulong fraction = decimals > 0 ? remainder * s_Pow10[decimals] / divisor : 0UL;
            if (format.TrimTrailingZeros)
            {
                while (decimals > 0 && fraction % 10UL == 0UL)
                {
                    fraction /= 10UL;
                    decimals--;
                }
            }

            if (!writer.WriteGrouped(integer, format.GroupSeparator))
            {
                return false;
            }

            if (decimals > 0 && !(writer.Write(format.DecimalSeparator) && writer.WriteDigits(fraction, decimals)))
            {
                return false;
            }

            if (!writer.Write(format.GetSuffix(unit).AsSpan()))
            {
                return false;
            }

            charsWritten = writer.Position;
            return true;
        }

        /// <summary>
        /// 时长（整秒）：
        ///   <see cref="DurationStyle.Clock"/> "m:ss"，满 1 小时 "h:mm:ss"（小时不封顶）；
        ///   <see cref="DurationStyle.MinutesSeconds"/> "mm:ss"（分钟不进位到小时）；
        ///   <see cref="DurationStyle.HoursMinutesSeconds"/> "hh:mm:ss"；
        ///   <see cref="DurationStyle.Compact"/> 从最大非零单位起取 <see cref="DurationUnits.MaxParts"/> 个相邻单位、略去其中为 0 的，
        ///   如 "1d 2h" / "3m 5s" / "1h"。负数输出前导 '-'。
        /// </summary>
        /// <param name="totalSeconds">总秒数。</param>
        /// <param name="destination">目标缓冲。</param>
        /// <param name="charsWritten">写入字符数。</param>
        /// <param name="style">样式。</param>
        /// <param name="units">Compact 样式的单位文字，null 为 <see cref="DurationUnits.English"/>。</param>
        /// <returns>空间足够并写入返回 true。</returns>
        public static bool TryFormatDuration(long totalSeconds, Span<char> destination, out int charsWritten,
            DurationStyle style = DurationStyle.Clock, DurationUnits units = null)
        {
            charsWritten = 0;
            bool negative = totalSeconds < 0;
            ulong total = negative ? (ulong)(-(totalSeconds + 1)) + 1UL : (ulong)totalSeconds;
            ulong seconds = total % 60UL;
            ulong totalMinutes = total / 60UL;
            ulong minutes = totalMinutes % 60UL;
            ulong totalHours = totalMinutes / 60UL;
            SpanWriter writer = new SpanWriter(destination);
            if (negative && !writer.Write('-'))
            {
                return false;
            }

            bool ok;
            switch (style)
            {
                case DurationStyle.Clock:
                    ok = totalHours == 0UL
                        ? writer.WriteDigits(minutes, 1) && writer.Write(':') && writer.WriteDigits(seconds, 2)
                        : writer.WriteDigits(totalHours, 1) && writer.Write(':') && writer.WriteDigits(minutes, 2) &&
                          writer.Write(':') && writer.WriteDigits(seconds, 2);
                    break;
                case DurationStyle.MinutesSeconds:
                    ok = writer.WriteDigits(totalMinutes, 2) && writer.Write(':') && writer.WriteDigits(seconds, 2);
                    break;
                case DurationStyle.HoursMinutesSeconds:
                    ok = writer.WriteDigits(totalHours, 2) && writer.Write(':') && writer.WriteDigits(minutes, 2) &&
                         writer.Write(':') && writer.WriteDigits(seconds, 2);
                    break;
                case DurationStyle.Compact:
                    ok = WriteCompactDuration(ref writer, totalHours / 24UL, totalHours % 24UL, minutes, seconds,
                        units ?? DurationUnits.English);
                    break;
                default:
                    throw new FrameworkException("TextFormat.TryFormatDuration: unknown DurationStyle " + (int)style + ".");
            }

            if (!ok)
            {
                return false;
            }

            charsWritten = writer.Position;
            return true;
        }

        /// <summary>
        /// 百分比：ratio 0.456 → "46%"（decimals = 0）/ "45.6%"（decimals = 1），四舍五入（远离零），
        /// 但 ratio &lt; 1 时绝不显示 100%（取该精度下的最大值，如 "99%" / "99.9%"）——进度条未完成就显示 100% 是常见体验 bug。
        /// NaN / 无穷按 0；百分数的绝对值超过 2^53（远超任何 UI 需要）时钳位。
        /// </summary>
        /// <param name="ratio">比例（1 = 100%）。</param>
        /// <param name="destination">目标缓冲。</param>
        /// <param name="charsWritten">写入字符数。</param>
        /// <param name="decimals">小数位 0-6。</param>
        /// <returns>空间足够并写入返回 true。</returns>
        public static bool TryFormatPercent(double ratio, Span<char> destination, out int charsWritten, int decimals = 0)
        {
            if (decimals < 0 || decimals > MaxPercentDecimals)
            {
                throw new FrameworkException("TextFormat.TryFormatPercent: decimals must be 0-" + MaxPercentDecimals + ", got " + decimals + ".");
            }

            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
            {
                ratio = 0.0;
            }

            // 先舍入成"最小显示单位"（10^-decimals 个百分点）的整数个数，再按整数写出数字，
            // 与 TryFormatCompact / TryFormatDuration 一样不经过浮点格式化。
            ulong factor = s_Pow10[decimals];
            double full = 100.0 * factor;
            double scaled = Math.Round(ratio * full, MidpointRounding.AwayFromZero);
            if (ratio < 1.0 && scaled >= full)
            {
                scaled = full - 1.0;
            }

            long units = (long)Math.Max(-MaxExactInteger, Math.Min(MaxExactInteger, scaled));
            ulong magnitude = units < 0 ? (ulong)(-units) : (ulong)units;
            charsWritten = 0;
            SpanWriter writer = new SpanWriter(destination);
            if (units < 0 && !writer.Write('-'))
            {
                return false;
            }

            if (!writer.WriteDigits(magnitude / factor, 1))
            {
                return false;
            }

            if (decimals > 0 && !(writer.Write('.') && writer.WriteDigits(magnitude % factor, decimals)))
            {
                return false;
            }

            if (!writer.Write('%'))
            {
                return false;
            }

            charsWritten = writer.Position;
            return true;
        }

        /// <summary>
        /// 小数秒按取整方式换成整秒（NaN 按 0，超出 long 范围时钳位）。
        /// </summary>
        /// <param name="seconds">秒数。</param>
        /// <param name="rounding">取整方式。</param>
        /// <returns>整秒。</returns>
        public static long ToWholeSeconds(double seconds, DurationRounding rounding)
        {
            if (double.IsNaN(seconds))
            {
                return 0L;
            }

            double whole = rounding == DurationRounding.Ceiling ? Math.Ceiling(seconds) : Math.Floor(seconds);
            if (whole >= 9.2e18)
            {
                return long.MaxValue;
            }

            if (whole <= -9.2e18)
            {
                return long.MinValue;
            }

            return (long)whole;
        }

        private static bool WriteCompactDuration(ref SpanWriter writer, ulong days, ulong hours, ulong minutes, ulong seconds, DurationUnits units)
        {
            int first = days != 0UL ? 0 : hours != 0UL ? 1 : minutes != 0UL ? 2 : seconds != 0UL ? 3 : -1;
            if (first < 0)
            {
                return writer.WriteDigits(0UL, 1) && writer.Write(units.Second.AsSpan());
            }

            int end = Math.Min(4, first + units.MaxParts);
            bool wroteAny = false;
            for (int i = first; i < end; i++)
            {
                ulong amount = i == 0 ? days : i == 1 ? hours : i == 2 ? minutes : seconds;
                if (amount == 0UL)
                {
                    continue;
                }

                if (wroteAny && !writer.Write(units.Separator.AsSpan()))
                {
                    return false;
                }

                string unit = i == 0 ? units.Day : i == 1 ? units.Hour : i == 2 ? units.Minute : units.Second;
                if (!writer.WriteDigits(amount, 1) || !writer.Write(unit.AsSpan()))
                {
                    return false;
                }

                wroteAny = true;
            }

            return true;
        }

        internal static int CountDigits(ulong value)
        {
            int digits = 1;
            while (value >= 10UL)
            {
                value /= 10UL;
                digits++;
            }

            return digits;
        }

        internal static ulong Pow10(int exponent)
        {
            return s_Pow10[exponent];
        }

        /// <summary>带边界检查的顺序写入器；任何一次写入失败都返回 false，调用方整体放弃。</summary>
        private ref struct SpanWriter
        {
            private readonly Span<char> m_Destination;
            private int m_Position;

            public SpanWriter(Span<char> destination)
            {
                m_Destination = destination;
                m_Position = 0;
            }

            public int Position
            {
                get { return m_Position; }
            }

            public bool Write(char c)
            {
                if (m_Position >= m_Destination.Length)
                {
                    return false;
                }

                m_Destination[m_Position++] = c;
                return true;
            }

            public bool Write(ReadOnlySpan<char> text)
            {
                if (text.Length > m_Destination.Length - m_Position)
                {
                    return false;
                }

                text.CopyTo(m_Destination.Slice(m_Position));
                m_Position += text.Length;
                return true;
            }

            /// <summary>写十进制数字，不足 minDigits 位时补前导 0。</summary>
            public bool WriteDigits(ulong value, int minDigits)
            {
                int digits = Math.Max(CountDigits(value), minDigits);
                if (digits > m_Destination.Length - m_Position)
                {
                    return false;
                }

                for (int i = m_Position + digits - 1; i >= m_Position; i--)
                {
                    m_Destination[i] = (char)('0' + (int)(value % 10UL));
                    value /= 10UL;
                }

                m_Position += digits;
                return true;
            }

            /// <summary>写十进制整数，separator 非 '\0' 时每三位插入分组符。</summary>
            public bool WriteGrouped(ulong value, char separator)
            {
                if (separator == '\0')
                {
                    return WriteDigits(value, 1);
                }

                int digits = CountDigits(value);
                int length = digits + (digits - 1) / 3;
                if (length > m_Destination.Length - m_Position)
                {
                    return false;
                }

                int end = m_Position + length - 1;
                int group = 0;
                for (int i = end; i >= m_Position; i--)
                {
                    if (group == 3)
                    {
                        m_Destination[i] = separator;
                        group = 0;
                        continue;
                    }

                    m_Destination[i] = (char)('0' + (int)(value % 10UL));
                    value /= 10UL;
                    group++;
                }

                m_Position += length;
                return true;
            }
        }
    }

    /// <summary>
    /// 紧凑数字的单位表与规则。不可变（With* 方法返回新实例），可在多线程共享。
    /// </summary>
    public sealed class CompactNumberFormat
    {
        /// <summary>K / M / B / T，门槛 1000，最多 1 位小数，去尾零："999"、"1K"、"1.5K"、"12.3M"。</summary>
        public static readonly CompactNumberFormat Western =
            new CompactNumberFormat(new[] { 1000L, 1000000L, 1000000000L, 1000000000000L }, new[] { "K", "M", "B", "T" });

        /// <summary>万 / 亿 / 万亿，门槛 1 万，最多 1 位小数，去尾零："9999"、"1万"、"1.2万"、"3.4亿"。</summary>
        public static readonly CompactNumberFormat Chinese =
            new CompactNumberFormat(new[] { 10000L, 100000000L, 1000000000000L }, new[] { "万", "亿", "万亿" });

        private readonly long[] m_Divisors;
        private readonly string[] m_Suffixes;

        /// <summary>
        /// 构造自定义单位表。
        /// </summary>
        /// <param name="divisors">各单位的除数，严格递增且至少为 2。</param>
        /// <param name="suffixes">各单位的后缀，与 divisors 一一对应。</param>
        public CompactNumberFormat(long[] divisors, string[] suffixes)
        {
            if (divisors == null || suffixes == null || divisors.Length == 0 || divisors.Length != suffixes.Length)
            {
                throw new FrameworkException("CompactNumberFormat: divisors and suffixes must be non-empty arrays of the same length.");
            }

            for (int i = 0; i < divisors.Length; i++)
            {
                if (divisors[i] < 2 || (i > 0 && divisors[i] <= divisors[i - 1]))
                {
                    throw new FrameworkException("CompactNumberFormat: divisors must be >= 2 and strictly increasing (index " + i + ").");
                }

                if (suffixes[i] == null)
                {
                    throw new FrameworkException("CompactNumberFormat: suffix at index " + i + " is null.");
                }
            }

            m_Divisors = (long[])divisors.Clone();
            m_Suffixes = (string[])suffixes.Clone();
            Threshold = m_Divisors[0];
            MaxDecimals = 1;
            MaxSignificantDigits = 0;
            TrimTrailingZeros = true;
            DecimalSeparator = '.';
            GroupSeparator = '\0';
            Validate();
        }

        private CompactNumberFormat(CompactNumberFormat source)
        {
            m_Divisors = source.m_Divisors;
            m_Suffixes = source.m_Suffixes;
            Threshold = source.Threshold;
            MaxDecimals = source.MaxDecimals;
            MaxSignificantDigits = source.MaxSignificantDigits;
            TrimTrailingZeros = source.TrimTrailingZeros;
            DecimalSeparator = source.DecimalSeparator;
            GroupSeparator = source.GroupSeparator;
        }

        /// <summary>单位个数。</summary>
        public int UnitCount
        {
            get { return m_Divisors.Length; }
        }

        /// <summary>|值| 低于它时写完整整数；不低于第一个除数。</summary>
        public long Threshold { get; private set; }

        /// <summary>最多小数位（0-6）。</summary>
        public int MaxDecimals { get; private set; }

        /// <summary>最多有效数字（0 = 不限）：3 时为 "1.23K" / "12.3K" / "123K"，仍受 <see cref="MaxDecimals"/> 约束。</summary>
        public int MaxSignificantDigits { get; private set; }

        /// <summary>是否去掉小数尾部的 0（"1.0K" → "1K"）。</summary>
        public bool TrimTrailingZeros { get; private set; }

        /// <summary>小数点。</summary>
        public char DecimalSeparator { get; private set; }

        /// <summary>整数部分的千分位分组符，'\0' 表示不分组。</summary>
        public char GroupSeparator { get; private set; }

        /// <summary>第 index 个单位的除数。</summary>
        public long GetDivisor(int index)
        {
            return m_Divisors[index];
        }

        /// <summary>第 index 个单位的后缀。</summary>
        public string GetSuffix(int index)
        {
            return m_Suffixes[index];
        }

        /// <summary>返回改了门槛的新格式（如 100000：十万以下显示完整数字）。</summary>
        public CompactNumberFormat WithThreshold(long threshold)
        {
            CompactNumberFormat copy = new CompactNumberFormat(this);
            copy.Threshold = threshold;
            copy.Validate();
            return copy;
        }

        /// <summary>返回改了小数规则的新格式。</summary>
        public CompactNumberFormat WithDecimals(int maxDecimals, int maxSignificantDigits = 0)
        {
            CompactNumberFormat copy = new CompactNumberFormat(this);
            copy.MaxDecimals = maxDecimals;
            copy.MaxSignificantDigits = maxSignificantDigits;
            copy.Validate();
            return copy;
        }

        /// <summary>返回改了去尾零规则的新格式。</summary>
        public CompactNumberFormat WithTrimTrailingZeros(bool trimTrailingZeros)
        {
            CompactNumberFormat copy = new CompactNumberFormat(this);
            copy.TrimTrailingZeros = trimTrailingZeros;
            return copy;
        }

        /// <summary>返回改了小数点 / 分组符的新格式（groupSeparator 为 '\0' 不分组）。</summary>
        public CompactNumberFormat WithSeparators(char decimalSeparator, char groupSeparator)
        {
            CompactNumberFormat copy = new CompactNumberFormat(this);
            copy.DecimalSeparator = decimalSeparator;
            copy.GroupSeparator = groupSeparator;
            return copy;
        }

        private void Validate()
        {
            if (MaxDecimals < 0 || MaxDecimals > 6)
            {
                throw new FrameworkException("CompactNumberFormat: MaxDecimals must be 0-6, got " + MaxDecimals + ".");
            }

            if (MaxSignificantDigits < 0 || MaxSignificantDigits > 19)
            {
                throw new FrameworkException("CompactNumberFormat: MaxSignificantDigits must be 0-19, got " + MaxSignificantDigits + ".");
            }

            if (Threshold < m_Divisors[0])
            {
                throw new FrameworkException("CompactNumberFormat: Threshold " + Threshold + " must be >= the first divisor " + m_Divisors[0] + ".");
            }

            // 小数部分按 remainder * 10^decimals / divisor 计算，remainder < divisor，须保证乘积不溢出 ulong。
            ulong limit = ulong.MaxValue / TextFormat.Pow10(MaxDecimals);
            if ((ulong)m_Divisors[m_Divisors.Length - 1] > limit)
            {
                throw new FrameworkException("CompactNumberFormat: the largest divisor is too large for " + MaxDecimals + " decimal(s).");
            }
        }
    }

    /// <summary>时长样式，见 <see cref="TextFormat.TryFormatDuration"/>。</summary>
    public enum DurationStyle
    {
        /// <summary>"m:ss"，满 1 小时后 "h:mm:ss"（倒计时最常用）。</summary>
        Clock,

        /// <summary>"mm:ss"，分钟至少两位且不进位到小时（对局计时）。</summary>
        MinutesSeconds,

        /// <summary>"hh:mm:ss"，始终带小时。</summary>
        HoursMinutesSeconds,

        /// <summary>"1d 2h" / "3m 5s"，单位文字由 <see cref="DurationUnits"/> 决定。</summary>
        Compact,
    }

    /// <summary>小数秒取整方式。</summary>
    public enum DurationRounding
    {
        /// <summary>向下取整（已用时长）。</summary>
        Floor,

        /// <summary>向上取整（倒计时：剩 0.2 秒仍显示 0:01，归零时才显示 0:00）。</summary>
        Ceiling,
    }

    /// <summary>
    /// Compact 时长的单位文字。不可变，可在多线程共享；本地化时用翻译后的文字构造一份即可。
    /// </summary>
    public sealed class DurationUnits
    {
        /// <summary>"1d 2h" / "3m 5s"。</summary>
        public static readonly DurationUnits English = new DurationUnits("d", "h", "m", "s", " ");

        /// <summary>"1天2小时" / "3分5秒"。</summary>
        public static readonly DurationUnits Chinese = new DurationUnits("天", "小时", "分", "秒", string.Empty);

        /// <summary>
        /// 构造单位文字。
        /// </summary>
        /// <param name="day">天。</param>
        /// <param name="hour">小时。</param>
        /// <param name="minute">分钟。</param>
        /// <param name="second">秒。</param>
        /// <param name="separator">两段之间的分隔。</param>
        /// <param name="maxParts">最多输出几个相邻单位（1-4）。</param>
        public DurationUnits(string day, string hour, string minute, string second, string separator, int maxParts = 2)
        {
            if (day == null || hour == null || minute == null || second == null || separator == null)
            {
                throw new FrameworkException("DurationUnits: unit texts and separator must not be null.");
            }

            if (maxParts < 1 || maxParts > 4)
            {
                throw new FrameworkException("DurationUnits: maxParts must be 1-4, got " + maxParts + ".");
            }

            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            Separator = separator;
            MaxParts = maxParts;
        }

        /// <summary>天。</summary>
        public string Day { get; private set; }

        /// <summary>小时。</summary>
        public string Hour { get; private set; }

        /// <summary>分钟。</summary>
        public string Minute { get; private set; }

        /// <summary>秒。</summary>
        public string Second { get; private set; }

        /// <summary>两段之间的分隔。</summary>
        public string Separator { get; private set; }

        /// <summary>最多输出几个相邻单位。</summary>
        public int MaxParts { get; private set; }
    }
}
