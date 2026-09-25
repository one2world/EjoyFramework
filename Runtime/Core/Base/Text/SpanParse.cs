//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 直接从字符切片解析数值 / 布尔，零分配（配合 <see cref="TextLineEnumerator"/> / <see cref="TextFieldReader"/>
    /// 解析数据表、配置、本地化文本时不再为每个字段生成 string）。全部使用不变文化。
    ///
    /// 语义：
    ///   - 整数：同 NumberStyles.Integer —— 允许首尾空白（U+0009-U+000D、U+0020）与一个前导 +/-，只认 ASCII 数字，
    ///     溢出返回 false。手写实现（比 BCL 少了文化与样式分派），与 int.TryParse(Integer, Invariant) 结果一致
    ///     （唯一差异：不接受 BCL 为兼容保留的尾随 '\0'）。
    ///   - 浮点：交给 BCL 的 float/double.TryParse(span, NumberStyles.Float, InvariantCulture)，舍入与 BCL 完全相同。
    ///   - 布尔（宽松）：首尾空白后不区分大小写地接受 1/0、true/false、yes/no（配置表里 1/0 很常见）。
    /// 失败时 value 为 0 / false。纯函数，任意线程安全。
    /// </summary>
    public static class SpanParse
    {
        /// <summary>解析 32 位整数。</summary>
        public static bool TryParseInt32(ReadOnlySpan<char> text, out int value)
        {
            long parsed;
            if (TryParseInteger(text, int.MinValue, int.MaxValue, out parsed))
            {
                value = (int)parsed;
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>解析 64 位整数。</summary>
        public static bool TryParseInt64(ReadOnlySpan<char> text, out long value)
        {
            return TryParseInteger(text, long.MinValue, long.MaxValue, out value);
        }

        /// <summary>解析单精度浮点（NumberStyles.Float，不变文化）。</summary>
        public static bool TryParseSingle(ReadOnlySpan<char> text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>解析双精度浮点（NumberStyles.Float，不变文化）。</summary>
        public static bool TryParseDouble(ReadOnlySpan<char> text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>宽松解析布尔：1/0、true/false、yes/no，不区分大小写，允许首尾空白。</summary>
        public static bool TryParseBoolean(ReadOnlySpan<char> text, out bool value)
        {
            ReadOnlySpan<char> s = text.Trim();
            if (s.Length == 1)
            {
                if (s[0] == '1')
                {
                    value = true;
                    return true;
                }

                if (s[0] == '0')
                {
                    value = false;
                    return true;
                }
            }
            else if (EqualsAsciiIgnoreCase(s, "true") || EqualsAsciiIgnoreCase(s, "yes"))
            {
                value = true;
                return true;
            }
            else if (EqualsAsciiIgnoreCase(s, "false") || EqualsAsciiIgnoreCase(s, "no"))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        /// <summary>不区分 ASCII 大小写比较；lowerAscii 必须是全小写 ASCII。</summary>
        private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<char> text, string lowerAscii)
        {
            if (lowerAscii == null || text.Length != lowerAscii.Length)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 'A' && c <= 'Z')
                {
                    c = (char)(c + 32);
                }

                if (c != lowerAscii[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseInteger(ReadOnlySpan<char> text, long min, long max, out long value)
        {
            value = 0L;
            int start = 0;
            int end = text.Length;
            while (start < end && IsNumberWhite(text[start]))
            {
                start++;
            }

            while (end > start && IsNumberWhite(text[end - 1]))
            {
                end--;
            }

            if (start >= end)
            {
                return false;
            }

            bool negative = false;
            char sign = text[start];
            if (sign == '-' || sign == '+')
            {
                negative = sign == '-';
                start++;
                if (start >= end)
                {
                    return false;
                }
            }

            // 以"量值上限"判断溢出：负数允许到 |min|（比 max 大 1）。
            ulong limit = negative ? (ulong)(-(min + 1)) + 1UL : (ulong)max;
            ulong accumulator = 0UL;
            for (int i = start; i < end; i++)
            {
                uint digit = (uint)(text[i] - '0');
                if (digit > 9u)
                {
                    return false;
                }

                if (accumulator > (limit - digit) / 10UL)
                {
                    return false;
                }

                accumulator = accumulator * 10UL + digit;
            }

            value = negative ? unchecked(-(long)accumulator) : (long)accumulator;
            return true;
        }

        private static bool IsNumberWhite(char c)
        {
            return c == ' ' || (c >= '\t' && c <= '\r');
        }
    }
}
