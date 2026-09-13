//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// 指向 blob 字符串池中一条 UTF-8 字符串的只读句柄。
    ///
    /// 池内每条记录的布局：<c>{ int utf8ByteLen | utf8 bytes }</c>，4 字节对齐；构建期全局去重，
    /// 因此内容相同的字符串在所有表中共享同一份字节与同一个偏移（可用 <see cref="Offset"/> 做 O(1) 相等判断）。
    ///
    /// 设计理由：
    ///   • 配置里的字符串绝大多数只是"被比较、被传给 UI 一次"，把它们在加载期全部实例化成 string 对象
    ///     是纯粹的浪费（几万个小对象常驻托管堆）。句柄让读取推迟到真正需要时，且大多数场景可以
    ///     用 <see cref="GetChars"/> 解码进栈上缓冲，全程零分配。
    ///   • <see cref="ToString"/> 保留为唯一的"分配出口"，命名上就提醒调用方这一步会产生 GC；
    ///     热路径应优先用 <see cref="GetChars"/> 或 <see cref="Utf8Bytes"/>。
    ///   • 偏移 0 约定为空串：字段槽默认值即为空，构建期无需为空串写任何池数据。
    ///
    /// 线程契约：只读，可任意线程并发使用；不得在所属 <see cref="ConfigBlob"/> 释放之后继续使用。
    /// </summary>
    public readonly struct BlobStringHandle : IEquatable<BlobStringHandle>
    {
        private readonly ConfigBlob m_Blob;
        private readonly int m_Offset;

        internal BlobStringHandle(ConfigBlob blob, int offset)
        {
            if (offset < 0)
            {
                throw new FrameworkException(Utility.Text.Format("BlobStringHandle：字符串偏移非法（{0}），文件已损坏。", offset));
            }

            m_Blob = blob;
            m_Offset = offset;
        }

        /// <summary>字符串记录在 blob 中的绝对偏移；0 表示空串。相同内容的字符串偏移必然相同（构建期去重）。</summary>
        public int Offset
        {
            get { return m_Offset; }
        }

        /// <summary>是否为空串（含未设置的字段）。</summary>
        public bool IsEmpty
        {
            get { return m_Blob == null || m_Offset == 0 || Utf8Length == 0; }
        }

        /// <summary>UTF-8 编码后的字节数。</summary>
        public int Utf8Length
        {
            get
            {
                if (m_Blob == null || m_Offset == 0)
                {
                    return 0;
                }

                int length = m_Blob.ReadInt32(m_Offset);
                if (length < 0)
                {
                    throw new FrameworkException(Utility.Text.Format("BlobStringHandle：字符串长度非法（{0}），文件已损坏。", length));
                }

                return length;
            }
        }

        /// <summary>
        /// UTF-8 原始字节的只读视图。适合直接做字节级比较或喂给已支持 UTF-8 的下游，完全零分配。
        /// </summary>
        public ReadOnlySpan<byte> Utf8Bytes
        {
            get
            {
                int length = Utf8Length;
                if (length == 0)
                {
                    return ReadOnlySpan<byte>.Empty;
                }

                return m_Blob.Slice(m_Offset + 4, length);
            }
        }

        /// <summary>
        /// 解码后的 char 数量（用于给 <see cref="GetChars"/> 预留缓冲）。
        /// </summary>
        public int CharCount
        {
            get
            {
                ReadOnlySpan<byte> bytes = Utf8Bytes;
                if (bytes.Length == 0)
                {
                    return 0;
                }

                return Encoding.UTF8.GetCharCount(bytes);
            }
        }

        /// <summary>
        /// 零分配解码到调用方提供的缓冲（通常是 <c>stackalloc</c> 或池化数组）。
        /// </summary>
        /// <param name="destination">目标缓冲，长度需不小于 <see cref="CharCount"/>。</param>
        /// <returns>写入的 char 数量。</returns>
        public int GetChars(Span<char> destination)
        {
            ReadOnlySpan<byte> bytes = Utf8Bytes;
            if (bytes.Length == 0)
            {
                return 0;
            }

            int required = Encoding.UTF8.GetCharCount(bytes);
            if (destination.Length < required)
            {
                throw new FrameworkException(Utility.Text.Format("BlobStringHandle.GetChars：目标缓冲过小，需要 {0} 个 char，实际 {1}。", required, destination.Length));
            }

            return Encoding.UTF8.GetChars(bytes, destination);
        }

        /// <summary>
        /// 与给定字符串比较内容（按 UTF-8 解码后逐 char 比较，不分配）。
        ///
        /// null 语义：<paramref name="value"/> 为 null 时，仅当本句柄是"未设置"（<see cref="Offset"/> 为 0）
        /// 才返回 true。这与写侧一致——<see cref="ConfigBlobWriter.SetString"/> 把 null 和空串一律写成 0 偏移，
        /// 二者在 blob 中不可区分，因此 <c>ContentEquals(null)</c> 与 <c>ContentEquals("")</c> 结果相同。
        /// 若调用方需要区分"没配"和"配了空串"，应在 schema 层面另加标志位，不要指望本方法。
        /// </summary>
        /// <param name="value">待比较字符串，可为 null。</param>
        /// <returns>内容相同返回 true。</returns>
        public bool ContentEquals(string value)
        {
            if (value == null)
            {
                return m_Offset == 0;
            }

            ReadOnlySpan<byte> bytes = Utf8Bytes;
            if (bytes.Length == 0)
            {
                return value.Length == 0;
            }

            int byteIndex = 0;
            int charIndex = 0;
            while (byteIndex < bytes.Length)
            {
                if (!TryDecodeUtf8Scalar(bytes, ref byteIndex, out uint scalar))
                {
                    return false;
                }

                if (scalar <= 0xFFFF)
                {
                    if (charIndex >= value.Length || value[charIndex] != (char)scalar)
                    {
                        return false;
                    }

                    charIndex++;
                    continue;
                }

                scalar -= 0x10000;
                if (charIndex + 1 >= value.Length
                    || value[charIndex] != (char)(0xD800 + (scalar >> 10))
                    || value[charIndex + 1] != (char)(0xDC00 + (scalar & 0x3FF)))
                {
                    return false;
                }

                charIndex += 2;
            }

            return charIndex == value.Length;
        }

        private static bool TryDecodeUtf8Scalar(ReadOnlySpan<byte> bytes, ref int index, out uint scalar)
        {
            byte first = bytes[index++];
            if (first < 0x80)
            {
                scalar = first;
                return true;
            }

            if (first >= 0xC2 && first <= 0xDF && index < bytes.Length)
            {
                byte second = bytes[index++];
                if (IsContinuation(second))
                {
                    scalar = (uint)(((first & 0x1F) << 6) | (second & 0x3F));
                    return true;
                }
            }
            else if (first >= 0xE0 && first <= 0xEF && index + 1 < bytes.Length)
            {
                byte second = bytes[index++];
                byte third = bytes[index++];
                bool validSecond = IsContinuation(second)
                    && (first != 0xE0 || second >= 0xA0)
                    && (first != 0xED || second <= 0x9F);
                if (validSecond && IsContinuation(third))
                {
                    scalar = (uint)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F));
                    return true;
                }
            }
            else if (first >= 0xF0 && first <= 0xF4 && index + 2 < bytes.Length)
            {
                byte second = bytes[index++];
                byte third = bytes[index++];
                byte fourth = bytes[index++];
                bool validSecond = IsContinuation(second)
                    && (first != 0xF0 || second >= 0x90)
                    && (first != 0xF4 || second <= 0x8F);
                if (validSecond && IsContinuation(third) && IsContinuation(fourth))
                {
                    scalar = (uint)(((first & 0x07) << 18)
                        | ((second & 0x3F) << 12)
                        | ((third & 0x3F) << 6)
                        | (fourth & 0x3F));
                    return true;
                }
            }

            scalar = 0;
            return false;
        }

        private static bool IsContinuation(byte value)
        {
            return (value & 0xC0) == 0x80;
        }

        /// <summary>
        /// 实例化为 string。<b>这是本类型唯一会产生 GC 分配的接口</b>，热路径请改用
        /// <see cref="GetChars"/> 或 <see cref="Utf8Bytes"/>。
        /// </summary>
        /// <returns>解码后的字符串；空句柄返回 <see cref="string.Empty"/>。</returns>
        public override string ToString()
        {
            ReadOnlySpan<byte> bytes = Utf8Bytes;
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 句柄相等性：同一个 blob 且偏移相同。由于构建期做了全局去重，这等价于内容相等。
        /// </summary>
        /// <param name="other">另一个句柄。</param>
        /// <returns>相等返回 true。</returns>
        public bool Equals(BlobStringHandle other)
        {
            return ReferenceEquals(m_Blob, other.m_Blob) && m_Offset == other.m_Offset;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is BlobStringHandle && Equals((BlobStringHandle)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_Offset;
        }
    }
}
