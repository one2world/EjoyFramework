//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 常用 Unity 值类型的零分配 <see cref="ITextFormatter{T}"/>：输出与各类型自身的 ToString() / ToString(format)
    /// 逐字一致（分量默认格式与 Unity 相同：Vector / Rect 为 F2，Quaternion 为 F5，Color 为 F3，整数向量与 Color32 无格式），
    /// 注册后 <c>t.AppendFormat("pos {0}", transform.position)</c> 不再装箱、不再生成中间字符串。
    /// 运行时在子系统注册阶段自动注册；编辑器下随程序集加载注册（EditMode 工具与测试同样生效）。
    /// </summary>
    public static class UnityTextFormatters
    {
        /// <summary>
        /// 注册全部 Unity 值类型格式化器（幂等）。
        /// </summary>
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            UnityValueFormatters formatters = UnityValueFormatters.Instance;
            TextFormatter.Register<Vector2>(formatters);
            TextFormatter.Register<Vector3>(formatters);
            TextFormatter.Register<Vector4>(formatters);
            TextFormatter.Register<Vector2Int>(formatters);
            TextFormatter.Register<Vector3Int>(formatters);
            TextFormatter.Register<Quaternion>(formatters);
            TextFormatter.Register<Color>(formatters);
            TextFormatter.Register<Color32>(formatters);
            TextFormatter.Register<Rect>(formatters);
        }
    }

    /// <summary>
    /// 一个单例同时实现各 Unity 值类型的 <see cref="ITextFormatter{T}"/>（同 Core 的内置格式化器）。
    /// </summary>
    internal sealed class UnityValueFormatters :
        ITextFormatter<Vector2>, ITextFormatter<Vector3>, ITextFormatter<Vector4>,
        ITextFormatter<Vector2Int>, ITextFormatter<Vector3Int>, ITextFormatter<Quaternion>,
        ITextFormatter<Color>, ITextFormatter<Color32>, ITextFormatter<Rect>
    {
        internal static readonly UnityValueFormatters Instance = new UnityValueFormatters();

        public bool TryFormat(Vector2 value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F2");
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Vector3 value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F2");
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(", ") && w.Number(value.z) &&
                      w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Vector4 value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F2");
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(", ") && w.Number(value.z) &&
                      w.Text(", ") && w.Number(value.w) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Vector2Int value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, null);
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Vector3Int value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, null);
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(", ") && w.Number(value.z) &&
                      w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Quaternion value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F5");
            bool ok = w.Text("(") && w.Number(value.x) && w.Text(", ") && w.Number(value.y) && w.Text(", ") && w.Number(value.z) &&
                      w.Text(", ") && w.Number(value.w) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Color value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F3");
            bool ok = w.Text("RGBA(") && w.Number(value.r) && w.Text(", ") && w.Number(value.g) && w.Text(", ") && w.Number(value.b) &&
                      w.Text(", ") && w.Number(value.a) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Color32 value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, null);
            bool ok = w.Text("RGBA(") && w.Number(value.r) && w.Text(", ") && w.Number(value.g) && w.Text(", ") && w.Number(value.b) &&
                      w.Text(", ") && w.Number(value.a) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        public bool TryFormat(Rect value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
        {
            var w = new ComponentWriter(destination, format, "F2");
            bool ok = w.Text("(x:") && w.Number(value.x) && w.Text(", y:") && w.Number(value.y) && w.Text(", width:") &&
                      w.Number(value.width) && w.Text(", height:") && w.Number(value.height) && w.Text(")");
            return w.Finish(ok, out charsWritten);
        }

        /// <summary>顺序写入分量；任何一步空间不足都返回 false，由调用方扩容后整体重来。</summary>
        private ref struct ComponentWriter
        {
            private readonly Span<char> m_Destination;
            private readonly ReadOnlySpan<char> m_Format;
            private int m_Position;

            public ComponentWriter(Span<char> destination, ReadOnlySpan<char> format, string defaultFormat)
            {
                m_Destination = destination;
                m_Format = format.Length != 0 ? format : defaultFormat.AsSpan();
                m_Position = 0;
            }

            public bool Text(string text)
            {
                if (text.Length > m_Destination.Length - m_Position)
                {
                    return false;
                }

                text.AsSpan().CopyTo(m_Destination.Slice(m_Position));
                m_Position += text.Length;
                return true;
            }

            public bool Number(float value)
            {
                int written;
                if (!value.TryFormat(m_Destination.Slice(m_Position), out written, m_Format, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                m_Position += written;
                return true;
            }

            public bool Number(int value)
            {
                int written;
                if (!value.TryFormat(m_Destination.Slice(m_Position), out written, m_Format, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                m_Position += written;
                return true;
            }

            public bool Number(byte value)
            {
                int written;
                if (!value.TryFormat(m_Destination.Slice(m_Position), out written, m_Format, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                m_Position += written;
                return true;
            }

            public bool Finish(bool ok, out int charsWritten)
            {
                charsWritten = ok ? m_Position : 0;
                return ok;
            }
        }
    }
}
