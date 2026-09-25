//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// TempText 的格式化扩展：复合格式（AppendFormat）、泛型追加、紧凑数字 / 时长 / 百分比。
    /// 典型用法：
    /// <code>
    /// using (var t = TempText.Rent())
    /// {
    ///     t.AppendFormat("{0}/{1}  {2:F1}%", hp, maxHp, ratio * 100f);   // 值类型参数不装箱、零分配
    ///     t.Append(' ').AppendCompact(gold);                              // 1.2K / 3.4M
    ///     t.Append(' ').AppendDuration(remaining, DurationStyle.Clock, DurationRounding.Ceiling);
    ///     label.SetText(t.AsSpan());
    /// }
    /// </code>
    /// 复合格式语法与 string.Format 一致：<c>{index[,alignment][:format]}</c>，<c>{{</c> / <c>}}</c> 为字面花括号；
    /// 格式串部分不支持转义花括号（遇到 '{' 报错）。数值一律用不变文化（与 TempText 其余 Append 一致），
    /// 因此输出与 <c>string.Format(CultureInfo.InvariantCulture, ...)</c> 逐字相同。
    /// 参数按类型走 <see cref="TextFormatter"/>：基础类型 / 枚举 / 已注册类型零分配，其余退回 ToString。
    /// 非法格式串：AppendFormat 抛出 <see cref="FrameworkException"/>（说明位置与原因），TryAppendFormat 返回 false；
    /// 两者都把已写入的半截内容回滚，不留半状态。
    /// </summary>
    public readonly ref partial struct TempText
    {
        /// <summary>复合格式里对齐宽度的上限。</summary>
        public const int MaxAlignment = 1 << 14;

        private const int MaxArgumentIndex = 1000000;

        /// <summary>未使用的泛型参数占位（少参数重载复用同一个 6 参数实现）。</summary>
        private struct NoArg
        {
        }

        /// <summary>
        /// 按类型格式化追加任意值：基础类型、枚举与已注册 <see cref="ITextFormatter{T}"/> 的类型零分配且不装箱。
        /// </summary>
        /// <typeparam name="T">值类型。</typeparam>
        /// <param name="value">值；null 追加为空。</param>
        /// <param name="format">可选格式串。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append<T>(T value, string format = null)
        {
            CheckUsable();
            AppendValue(value, format.AsSpan());
            return this;
        }

        /// <summary>
        /// 追加字符数组（显式重载：避免 char[] 落到泛型 <see cref="Append{T}(T, string)"/> 而被格式化成类型名）。
        /// </summary>
        /// <param name="value">字符数组，null 视为空。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(char[] value)
        {
            return Append(new ReadOnlySpan<char>(value));
        }

        /// <summary>
        /// 追加 repeatCount 个相同字符（填充、进度条）。
        /// </summary>
        /// <param name="value">字符。</param>
        /// <param name="repeatCount">个数，不能为负。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText Append(char value, int repeatCount)
        {
            CheckUsable();
            if (repeatCount < 0 || repeatCount > MaxGrowCapacity)
            {
                throw new FrameworkException("TempText.Append: repeatCount " + repeatCount + " is out of range.");
            }

            if (repeatCount == 0)
            {
                return this;
            }

            EnsureCapacity(m_State.Length + repeatCount);
            new Span<char>(m_State.Buffer, m_State.Length, repeatCount).Fill(value);
            m_State.Length += repeatCount;
            return this;
        }

        /// <summary>按复合格式追加（1 个参数）。</summary>
        public TempText AppendFormat<T0>(string format, T0 arg0)
        {
            FormatCore(format, 1, arg0, default(NoArg), default(NoArg), default(NoArg), default(NoArg), default(NoArg), true);
            return this;
        }

        /// <summary>按复合格式追加（2 个参数）。</summary>
        public TempText AppendFormat<T0, T1>(string format, T0 arg0, T1 arg1)
        {
            FormatCore(format, 2, arg0, arg1, default(NoArg), default(NoArg), default(NoArg), default(NoArg), true);
            return this;
        }

        /// <summary>按复合格式追加（3 个参数）。</summary>
        public TempText AppendFormat<T0, T1, T2>(string format, T0 arg0, T1 arg1, T2 arg2)
        {
            FormatCore(format, 3, arg0, arg1, arg2, default(NoArg), default(NoArg), default(NoArg), true);
            return this;
        }

        /// <summary>按复合格式追加（4 个参数）。</summary>
        public TempText AppendFormat<T0, T1, T2, T3>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            FormatCore(format, 4, arg0, arg1, arg2, arg3, default(NoArg), default(NoArg), true);
            return this;
        }

        /// <summary>按复合格式追加（5 个参数）。</summary>
        public TempText AppendFormat<T0, T1, T2, T3, T4>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            FormatCore(format, 5, arg0, arg1, arg2, arg3, arg4, default(NoArg), true);
            return this;
        }

        /// <summary>按复合格式追加（6 个参数）。</summary>
        public TempText AppendFormat<T0, T1, T2, T3, T4, T5>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            FormatCore(format, 6, arg0, arg1, arg2, arg3, arg4, arg5, true);
            return this;
        }

        /// <summary>
        /// 宽松版：格式串非法（常见于翻译稿里的模板）时返回 false 且不追加任何内容，由调用方决定退回策略。
        /// </summary>
        public bool TryAppendFormat<T0>(string format, T0 arg0)
        {
            return FormatCore(format, 1, arg0, default(NoArg), default(NoArg), default(NoArg), default(NoArg), default(NoArg), false);
        }

        /// <summary>宽松版，见 <see cref="TryAppendFormat{T0}(string, T0)"/>。</summary>
        public bool TryAppendFormat<T0, T1>(string format, T0 arg0, T1 arg1)
        {
            return FormatCore(format, 2, arg0, arg1, default(NoArg), default(NoArg), default(NoArg), default(NoArg), false);
        }

        /// <summary>宽松版，见 <see cref="TryAppendFormat{T0}(string, T0)"/>。</summary>
        public bool TryAppendFormat<T0, T1, T2>(string format, T0 arg0, T1 arg1, T2 arg2)
        {
            return FormatCore(format, 3, arg0, arg1, arg2, default(NoArg), default(NoArg), default(NoArg), false);
        }

        /// <summary>宽松版，见 <see cref="TryAppendFormat{T0}(string, T0)"/>。</summary>
        public bool TryAppendFormat<T0, T1, T2, T3>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            return FormatCore(format, 4, arg0, arg1, arg2, arg3, default(NoArg), default(NoArg), false);
        }

        /// <summary>宽松版，见 <see cref="TryAppendFormat{T0}(string, T0)"/>。</summary>
        public bool TryAppendFormat<T0, T1, T2, T3, T4>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            return FormatCore(format, 5, arg0, arg1, arg2, arg3, arg4, default(NoArg), false);
        }

        /// <summary>宽松版，见 <see cref="TryAppendFormat{T0}(string, T0)"/>。</summary>
        public bool TryAppendFormat<T0, T1, T2, T3, T4, T5>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            return FormatCore(format, 6, arg0, arg1, arg2, arg3, arg4, arg5, false);
        }

        /// <summary>
        /// 追加紧凑数字（1.2K / 3.4M / 1.2万 / 3.4亿），截断不进位，见 <see cref="TextFormat.TryFormatCompact"/>。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <param name="format">紧凑格式，null 为 <see cref="CompactNumberFormat.Western"/>。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText AppendCompact(long value, CompactNumberFormat format = null)
        {
            CheckUsable();
            int written;
            while (!TextFormat.TryFormatCompact(value, FreeSpan(), out written, format))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加时长（整秒），见 <see cref="TextFormat.TryFormatDuration"/>。
        /// </summary>
        /// <param name="totalSeconds">总秒数，负数输出前导 '-'。</param>
        /// <param name="style">样式。</param>
        /// <param name="units">Compact 样式的单位文字，null 为 <see cref="DurationUnits.English"/>。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText AppendDuration(long totalSeconds, DurationStyle style = DurationStyle.Clock, DurationUnits units = null)
        {
            CheckUsable();
            int written;
            while (!TextFormat.TryFormatDuration(totalSeconds, FreeSpan(), out written, style, units))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        /// <summary>
        /// 追加时长（小数秒，按 rounding 取整：倒计时用 Ceiling，保证归零前不显示 0）。
        /// </summary>
        /// <param name="seconds">秒数，NaN 按 0。</param>
        /// <param name="style">样式。</param>
        /// <param name="rounding">取整方式。</param>
        /// <param name="units">Compact 样式的单位文字。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText AppendDuration(double seconds, DurationStyle style, DurationRounding rounding, DurationUnits units = null)
        {
            return AppendDuration(TextFormat.ToWholeSeconds(seconds, rounding), style, units);
        }

        /// <summary>
        /// 追加百分比（ratio 0.5 → "50%"），见 <see cref="TextFormat.TryFormatPercent"/>：ratio &lt; 1 时绝不显示 100%。
        /// </summary>
        /// <param name="ratio">比例。</param>
        /// <param name="decimals">小数位 0-6。</param>
        /// <returns>自身，用于链式调用。</returns>
        public TempText AppendPercent(double ratio, int decimals = 0)
        {
            CheckUsable();
            int written;
            while (!TextFormat.TryFormatPercent(ratio, FreeSpan(), out written, decimals))
            {
                Grow();
            }

            m_State.Length += written;
            return this;
        }

        private void AppendValue<T>(T value, ReadOnlySpan<char> format)
        {
            ITextFormatter<T> formatter = TextFormatter.Get<T>();
            int written;
            while (!formatter.TryFormat(value, FreeSpan(), out written, format))
            {
                Grow();
            }

            m_State.Length += written;
        }

        private bool FormatCore<T0, T1, T2, T3, T4, T5>(string format, int argCount, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, bool throwOnError)
        {
            CheckUsable();
            int start = m_State.Length;
            if (format == null)
            {
                return Fail(start, "(null)", 0, "format is null", throwOnError);
            }

            int length = format.Length;
            int pos = 0;
            while (pos < length)
            {
                char c = format[pos];
                if (c == '{')
                {
                    if (pos + 1 < length && format[pos + 1] == '{')
                    {
                        Append('{');
                        pos += 2;
                        continue;
                    }

                    pos++;
                    int index = 0;
                    int indexDigits = 0;
                    while (pos < length && IsDigit(format[pos]))
                    {
                        if (index < MaxArgumentIndex)
                        {
                            index = index * 10 + (format[pos] - '0');
                        }

                        pos++;
                        indexDigits++;
                    }

                    if (indexDigits == 0)
                    {
                        return Fail(start, format, pos, "expected an argument index after '{'", throwOnError);
                    }

                    pos = SkipSpaces(format, pos);
                    int alignment = 0;
                    if (pos < length && format[pos] == ',')
                    {
                        pos = SkipSpaces(format, pos + 1);
                        bool leftAlign = false;
                        if (pos < length && format[pos] == '-')
                        {
                            leftAlign = true;
                            pos++;
                        }

                        int alignDigits = 0;
                        while (pos < length && IsDigit(format[pos]))
                        {
                            alignment = alignment * 10 + (format[pos] - '0');
                            if (alignment > MaxAlignment)
                            {
                                return Fail(start, format, pos, "alignment exceeds " + MaxAlignment, throwOnError);
                            }

                            pos++;
                            alignDigits++;
                        }

                        if (alignDigits == 0)
                        {
                            return Fail(start, format, pos, "expected an alignment after ','", throwOnError);
                        }

                        if (leftAlign)
                        {
                            alignment = -alignment;
                        }

                        pos = SkipSpaces(format, pos);
                    }

                    int specStart = pos;
                    int specLength = 0;
                    if (pos < length && format[pos] == ':')
                    {
                        pos++;
                        specStart = pos;
                        while (pos < length && format[pos] != '}')
                        {
                            if (format[pos] == '{')
                            {
                                return Fail(start, format, pos, "'{' is not allowed inside a format specifier", throwOnError);
                            }

                            pos++;
                        }

                        specLength = pos - specStart;
                    }

                    if (pos >= length || format[pos] != '}')
                    {
                        return Fail(start, format, pos, "missing closing '}' for the format item", throwOnError);
                    }

                    pos++;
                    if (index >= argCount)
                    {
                        return Fail(start, format, pos - 1, "argument index " + index + " is out of range (" + argCount + " argument(s) supplied)", throwOnError);
                    }

                    ReadOnlySpan<char> spec = format.AsSpan(specStart, specLength);
                    int itemBegin = m_State.Length;
                    try
                    {
                        switch (index)
                        {
                            case 0: AppendValue(arg0, spec); break;
                            case 1: AppendValue(arg1, spec); break;
                            case 2: AppendValue(arg2, spec); break;
                            case 3: AppendValue(arg3, spec); break;
                            case 4: AppendValue(arg4, spec); break;
                            default: AppendValue(arg5, spec); break;
                        }
                    }
                    catch (FormatException e)
                    {
                        // 参数自身的格式串非法（如 int 的 {0:Q}）时 BCL 抛 FormatException：同样回滚并按模式抛出或返回 false。
                        return Fail(start, format, specStart, "invalid format specifier for argument " + index, throwOnError, e);
                    }

                    if (alignment != 0)
                    {
                        Align(itemBegin, alignment);
                    }

                    continue;
                }

                if (c == '}')
                {
                    if (pos + 1 < length && format[pos + 1] == '}')
                    {
                        Append('}');
                        pos += 2;
                        continue;
                    }

                    return Fail(start, format, pos, "unescaped '}' (write '}}' for a literal brace)", throwOnError);
                }

                int literalEnd = pos + 1;
                while (literalEnd < length && format[literalEnd] != '{' && format[literalEnd] != '}')
                {
                    literalEnd++;
                }

                Append(format.AsSpan(pos, literalEnd - pos));
                pos = literalEnd;
            }

            return true;
        }

        private void Align(int itemBegin, int alignment)
        {
            int width = m_State.Length - itemBegin;
            int total = alignment < 0 ? -alignment : alignment;
            int pad = total - width;
            if (pad <= 0)
            {
                return;
            }

            EnsureCapacity(m_State.Length + pad);
            char[] buffer = m_State.Buffer;
            if (alignment > 0)
            {
                // 右对齐：已写入的值整体后移，前面补空格（Array.Copy 对重叠区间按 memmove 语义处理）。
                Array.Copy(buffer, itemBegin, buffer, itemBegin + pad, width);
                new Span<char>(buffer, itemBegin, pad).Fill(' ');
            }
            else
            {
                new Span<char>(buffer, m_State.Length, pad).Fill(' ');
            }

            m_State.Length += pad;
        }

        private bool Fail(int rollbackLength, string format, int position, string reason, bool throwOnError, Exception inner = null)
        {
            m_State.Length = rollbackLength;
            if (!throwOnError)
            {
                return false;
            }

            string message = "TempText.AppendFormat: invalid format string \"" + format + "\" at position " + position +
                             ": " + reason + ". Format items are {index[,alignment][:format]}; escape literal braces as '{{' and '}}'.";
            throw inner != null ? new FrameworkException(message, inner) : new FrameworkException(message);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static int SkipSpaces(string format, int pos)
        {
            while (pos < format.Length && format[pos] == ' ')
            {
                pos++;
            }

            return pos;
        }
    }
}
