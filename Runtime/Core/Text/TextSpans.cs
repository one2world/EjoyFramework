//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 按行枚举字符切片，零分配。语义与 <c>text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)</c> 完全相同：
    /// N 个换行产生 N+1 行（末尾换行之后还有一个空行，空文本是一行空行），因此行号与旧的 Split 实现一致。
    /// <code>
    /// foreach (ReadOnlySpan&lt;char&gt; line in new TextLineEnumerator(text.AsSpan())) { ... }
    /// </code>
    /// 需要行号时手动 MoveNext 并读 <see cref="LineNumber"/>。
    /// （命名避开 .NET 6+ 的 System.Text.SpanLineEnumerator，将来切换运行时也不会引用冲突。）
    /// </summary>
    public ref struct TextLineEnumerator
    {
        private ReadOnlySpan<char> m_Remaining;
        private ReadOnlySpan<char> m_Current;
        private int m_LineNumber;
        private bool m_Finished;

        /// <summary>
        /// 构造行枚举器。
        /// </summary>
        /// <param name="text">待枚举的文本。</param>
        public TextLineEnumerator(ReadOnlySpan<char> text)
        {
            m_Remaining = text;
            m_Current = default(ReadOnlySpan<char>);
            m_LineNumber = 0;
            m_Finished = false;
        }

        /// <summary>当前行（不含换行符）。</summary>
        public ReadOnlySpan<char> Current
        {
            get { return m_Current; }
        }

        /// <summary>当前行的行号（从 1 开始）。</summary>
        public int LineNumber
        {
            get { return m_LineNumber; }
        }

        /// <summary>支持 foreach。</summary>
        /// <returns>自身。</returns>
        public TextLineEnumerator GetEnumerator()
        {
            return this;
        }

        /// <summary>前进到下一行。</summary>
        /// <returns>还有行返回 true。</returns>
        public bool MoveNext()
        {
            if (m_Finished)
            {
                return false;
            }

            m_LineNumber++;
            int index = m_Remaining.IndexOfAny('\r', '\n');
            if (index < 0)
            {
                m_Current = m_Remaining;
                m_Remaining = default(ReadOnlySpan<char>);
                m_Finished = true;
                return true;
            }

            m_Current = m_Remaining.Slice(0, index);
            int separatorLength = m_Remaining[index] == '\r' && index + 1 < m_Remaining.Length && m_Remaining[index + 1] == '\n' ? 2 : 1;
            m_Remaining = m_Remaining.Slice(index + separatorLength);
            return true;
        }
    }

    /// <summary>
    /// 顺序读取分隔字段的游标：数据行解析的主路径。数值 / 布尔字段直接从切片解析（<see cref="SpanParse"/>），
    /// 只有字符串字段分配。任何一次读取失败（字段不足或格式错误）返回 false，调用方应整行放弃。
    /// <code>
    /// var reader = new TextFieldReader(line, '\t');
    /// int id; string name; float speed;
    /// if (!reader.TryReadInt32(out id) || !reader.TryReadString(out name) || !reader.TryReadSingle(out speed)) return false;
    /// </code>
    /// 字段内容原样交给解析（数值解析允许首尾空白，字符串不做 Trim），与 <c>line.Split(separator)</c> 的逐列语义一致。
    /// </summary>
    public ref struct TextFieldReader
    {
        private ReadOnlySpan<char> m_Remaining;
        private readonly char m_Separator;
        private int m_FieldIndex;
        private bool m_Exhausted;

        /// <summary>
        /// 构造读取游标。
        /// </summary>
        /// <param name="text">一行文本。</param>
        /// <param name="separator">分隔符，默认 TAB。</param>
        public TextFieldReader(ReadOnlySpan<char> text, char separator = '\t')
        {
            m_Remaining = text;
            m_Separator = separator;
            m_FieldIndex = 0;
            m_Exhausted = false;
        }

        /// <summary>下一个待读字段的序号（从 0 开始）；也等于已读字段数。</summary>
        public int FieldIndex
        {
            get { return m_FieldIndex; }
        }

        /// <summary>是否还有未读字段。</summary>
        public bool HasMore
        {
            get { return !m_Exhausted; }
        }

        /// <summary>
        /// 统计一行的字段数（分隔符个数 + 1，与 Split 相同）。
        /// </summary>
        /// <param name="text">一行文本。</param>
        /// <param name="separator">分隔符。</param>
        /// <returns>字段数。</returns>
        public static int CountFields(ReadOnlySpan<char> text, char separator)
        {
            int count = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == separator)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>读取下一个字段的原始切片。</summary>
        /// <param name="field">字段切片。</param>
        /// <returns>还有字段返回 true。</returns>
        public bool TryReadField(out ReadOnlySpan<char> field)
        {
            if (m_Exhausted)
            {
                field = default(ReadOnlySpan<char>);
                return false;
            }

            m_FieldIndex++;
            int index = m_Remaining.IndexOf(m_Separator);
            if (index < 0)
            {
                field = m_Remaining;
                m_Remaining = default(ReadOnlySpan<char>);
                m_Exhausted = true;
                return true;
            }

            field = m_Remaining.Slice(0, index);
            m_Remaining = m_Remaining.Slice(index + 1);
            return true;
        }

        /// <summary>跳过 count 个字段。</summary>
        /// <param name="count">个数。</param>
        /// <returns>字段足够返回 true。</returns>
        public bool TrySkip(int count = 1)
        {
            ReadOnlySpan<char> ignored;
            for (int i = 0; i < count; i++)
            {
                if (!TryReadField(out ignored))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>读取字符串字段（分配；空字段返回 <see cref="string.Empty"/>）。</summary>
        public bool TryReadString(out string value)
        {
            ReadOnlySpan<char> field;
            if (!TryReadField(out field))
            {
                value = null;
                return false;
            }

            value = field.Length == 0 ? string.Empty : field.ToString();
            return true;
        }

        /// <summary>读取 32 位整数字段。</summary>
        public bool TryReadInt32(out int value)
        {
            ReadOnlySpan<char> field;
            if (TryReadField(out field))
            {
                return SpanParse.TryParseInt32(field, out value);
            }

            value = 0;
            return false;
        }

        /// <summary>读取 64 位整数字段。</summary>
        public bool TryReadInt64(out long value)
        {
            ReadOnlySpan<char> field;
            if (TryReadField(out field))
            {
                return SpanParse.TryParseInt64(field, out value);
            }

            value = 0L;
            return false;
        }

        /// <summary>读取单精度浮点字段。</summary>
        public bool TryReadSingle(out float value)
        {
            ReadOnlySpan<char> field;
            if (TryReadField(out field))
            {
                return SpanParse.TryParseSingle(field, out value);
            }

            value = 0f;
            return false;
        }

        /// <summary>读取双精度浮点字段。</summary>
        public bool TryReadDouble(out double value)
        {
            ReadOnlySpan<char> field;
            if (TryReadField(out field))
            {
                return SpanParse.TryParseDouble(field, out value);
            }

            value = 0.0;
            return false;
        }

        /// <summary>读取布尔字段（宽松：1/0、true/false、yes/no）。</summary>
        public bool TryReadBoolean(out bool value)
        {
            ReadOnlySpan<char> field;
            if (TryReadField(out field))
            {
                return SpanParse.TryParseBoolean(field, out value);
            }

            value = false;
            return false;
        }
    }
}
