//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 把 T 写成字符的零分配格式化器。<see cref="TempText.Append{T}(T)"/> 与 <see cref="TempText.AppendFormat{T0}(string, T0)"/>
    /// 按类型取用它，值类型参数全程不装箱。
    /// </summary>
    /// <typeparam name="T">被格式化的类型。</typeparam>
    public interface ITextFormatter<T>
    {
        /// <summary>
        /// 尝试把 value 写入 destination。空间不足必须返回 false（调用方会扩容后重试），不得部分成功。
        /// </summary>
        /// <param name="value">值。</param>
        /// <param name="destination">目标缓冲。</param>
        /// <param name="charsWritten">写入的字符数。</param>
        /// <param name="format">格式串（复合格式里 ':' 之后的部分，可为空）。</param>
        /// <returns>写入成功返回 true。</returns>
        bool TryFormat(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format);
    }

    /// <summary>
    /// <see cref="ITextFormatter{T}"/> 的登记表：每个类型一个格式化器，经静态泛型缓存解析，查找是一次静态字段读取。
    ///
    /// 内置（零分配，不变文化）：sbyte / byte / short / ushort / int / uint / long / ulong / float / double / decimal /
    /// bool / char / string / DateTime / DateTimeOffset / TimeSpan / Guid，以及所有枚举（名字缓存；未定义值、Flags 组合与
    /// 带格式串时退回 BCL 并分配）。其他类型默认退回 IFormattable / ToString（会分配，开发版首次使用时打一条 Debug 日志提示），
    /// 可用 <see cref="Register{T}"/> 注册自定义格式化器变成零分配（如项目里高频显示的 Vector3）。
    ///
    /// 设计借鉴 FPSSample 的 StringFormatter（单例同时实现多个 IConverter&lt;T&gt;、按泛型接口转换分派），
    /// 改为"静态泛型缓存 + 可注册"，数值格式化交给 BCL 的 TryFormat，保证与 ToString 逐字一致。
    ///
    /// 线程契约：解析与格式化任意线程安全；<see cref="Register{T}"/> 应在启动期调用（引用赋值原子，可随时生效）。
    /// </summary>
    public static class TextFormatter
    {
        /// <summary>
        /// 为类型 T 注册格式化器；传 null 恢复默认。
        /// </summary>
        /// <typeparam name="T">类型。</typeparam>
        /// <param name="formatter">格式化器。</param>
        public static void Register<T>(ITextFormatter<T> formatter)
        {
            Cache<T>.Formatter = formatter ?? Cache<T>.CreateDefault();
        }

        /// <summary>
        /// 类型 T 的当前格式化器是否零分配（非 ToString 退回路径）。枚举的未定义值 / 带格式串仍可能分配。
        /// </summary>
        /// <typeparam name="T">类型。</typeparam>
        /// <returns>非退回路径返回 true。</returns>
        public static bool IsAllocationFree<T>()
        {
            return !(Cache<T>.Formatter is FallbackTextFormatter<T>);
        }

        /// <summary>
        /// 用类型 T 的格式化器写入 destination。
        /// </summary>
        /// <typeparam name="T">类型。</typeparam>
        /// <param name="value">值。</param>
        /// <param name="destination">目标缓冲。</param>
        /// <param name="charsWritten">写入的字符数。</param>
        /// <param name="format">格式串，可为空。</param>
        /// <returns>空间足够并写入返回 true。</returns>
        public static bool TryFormat<T>(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format = default(ReadOnlySpan<char>))
        {
            return Cache<T>.Formatter.TryFormat(value, destination, out charsWritten, format);
        }

        internal static ITextFormatter<T> Get<T>()
        {
            return Cache<T>.Formatter;
        }

        internal static bool TryCopy(string text, Span<char> destination, out int charsWritten)
        {
            if (text == null)
            {
                charsWritten = 0;
                return true;
            }

            if (text.Length > destination.Length)
            {
                charsWritten = 0;
                return false;
            }

            text.AsSpan().CopyTo(destination);
            charsWritten = text.Length;
            return true;
        }

        private static class Cache<T>
        {
            internal static volatile ITextFormatter<T> Formatter = CreateDefault();

            internal static ITextFormatter<T> CreateDefault()
            {
                ITextFormatter<T> builtin = BuiltinTextFormatters.Instance as ITextFormatter<T>;
                if (builtin != null)
                {
                    return builtin;
                }

                if (typeof(T).IsEnum)
                {
                    return new EnumTextFormatter<T>();
                }

                return new FallbackTextFormatter<T>();
            }
        }
    }

    /// <summary>
    /// 内置格式化器：一个单例同时实现各基础类型的 <see cref="ITextFormatter{T}"/>，全部走不变文化的 BCL TryFormat。
    /// </summary>
    internal sealed class BuiltinTextFormatters :
        ITextFormatter<sbyte>, ITextFormatter<byte>, ITextFormatter<short>, ITextFormatter<ushort>,
        ITextFormatter<int>, ITextFormatter<uint>, ITextFormatter<long>, ITextFormatter<ulong>,
        ITextFormatter<float>, ITextFormatter<double>, ITextFormatter<decimal>,
        ITextFormatter<bool>, ITextFormatter<char>, ITextFormatter<string>,
        ITextFormatter<DateTime>, ITextFormatter<DateTimeOffset>, ITextFormatter<TimeSpan>, ITextFormatter<Guid>
    {
        internal static readonly BuiltinTextFormatters Instance = new BuiltinTextFormatters();

        private static readonly CultureInfo s_Invariant = CultureInfo.InvariantCulture;

        public bool TryFormat(sbyte value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(byte value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(short value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(ushort value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(int value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(uint value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(long value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(ulong value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(float value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(double value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(decimal value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(bool value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return TextFormatter.TryCopy(value ? "True" : "False", destination, out charsWritten);
        }

        public bool TryFormat(char value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            if (destination.Length < 1)
            {
                charsWritten = 0;
                return false;
            }

            destination[0] = value;
            charsWritten = 1;
            return true;
        }

        public bool TryFormat(string value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return TextFormatter.TryCopy(value, destination, out charsWritten);
        }

        public bool TryFormat(DateTime value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(DateTimeOffset value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(TimeSpan value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format, s_Invariant);
        }

        public bool TryFormat(Guid value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            return value.TryFormat(destination, out charsWritten, format);
        }
    }

    /// <summary>
    /// 枚举格式化器：构造时缓存每个已定义值的名字（与 Enum.ToString 相同），之后按值查名零分配。
    /// 未定义值、Flags 组合与带格式串（"D"/"X" 等）退回 Enum.ToString(format)（装箱 + 分配）。
    /// 名字表构造后只读，可多线程并发使用。
    /// </summary>
    internal sealed class EnumTextFormatter<T> : ITextFormatter<T>
    {
        private readonly Dictionary<T, string> m_Names;

        internal EnumTextFormatter()
        {
            Array values = Enum.GetValues(typeof(T));
            m_Names = new Dictionary<T, string>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                T value = (T)values.GetValue(i);
                if (!m_Names.ContainsKey(value))
                {
                    m_Names.Add(value, value.ToString());
                }
            }
        }

        public bool TryFormat(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            string text;
            if (format.Length != 0 || !m_Names.TryGetValue(value, out text))
            {
                text = ((Enum)(object)value).ToString(format.Length == 0 ? null : format.ToString());
            }

            return TextFormatter.TryCopy(text, destination, out charsWritten);
        }
    }

    /// <summary>
    /// 退回路径：IFormattable.ToString(format, InvariantCulture) 或 ToString()。每次都会分配（值类型还会装箱）。
    /// 开发版首次用到某类型时打一条 Debug 日志，提示为它注册 <see cref="ITextFormatter{T}"/>。
    /// </summary>
    internal sealed class FallbackTextFormatter<T> : ITextFormatter<T>
    {
        private static bool s_Reported;

        public bool TryFormat(T value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            if (value == null)
            {
                charsWritten = 0;
                return true;
            }

            if (!s_Reported)
            {
                s_Reported = true;
                FrameworkLog.Debug("TextFormatter: '{0}' has no zero-allocation formatter and falls back to ToString(); " +
                                   "call TextFormatter.Register<{0}>(...) to format it without allocating.", typeof(T).FullName);
            }

            IFormattable formattable = value as IFormattable;
            string text = formattable != null
                ? formattable.ToString(format.Length == 0 ? null : format.ToString(), CultureInfo.InvariantCulture)
                : value.ToString();
            return TextFormatter.TryCopy(text, destination, out charsWritten);
        }
    }
}
