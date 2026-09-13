//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// 小端（little-endian）二进制写入器，作用于一段可增长的内部字节缓冲。
    ///
    /// 设计要点：
    /// - 所有多字节基础类型均以小端序写入，与 <see cref="NetReader"/> 严格对称、可精确往返。
    /// - 缓冲容量不足时按 2 倍增长（doubling），尽量减少分配次数。
    /// - <see cref="WriteString"/> 与 <see cref="WriteBytes"/> 采用长度前缀编码；字符串前缀为 ushort，
    ///   并用哨兵值 <see cref="NullLengthSentinel"/>（0xFFFF）表示 null，从而区分 null 与空串/空数组。
    /// - <see cref="Reset"/> 可在不释放底层缓冲的前提下复用本写入器，避免重复分配。
    ///
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class NetWriter
    {
        /// <summary>
        /// 字符串/字节长度前缀的 null 哨兵值（ushort 最大值）。该值表示被写入的引用为 null。
        /// </summary>
        public const ushort NullLengthSentinel = ushort.MaxValue;

        // 缓冲容量硬上限（32 MB）：单次写入若需要超过此容量则视为异常，抛出而非无界增长。
        private const int MaxBufferCapacity = 32 * 1024 * 1024;

        private byte[] m_Buffer;
        private int m_Length;

        /// <summary>
        /// 构造写入器。
        /// </summary>
        /// <param name="initialCapacity">底层缓冲的初始容量（字节）；小于 1 时回退为 1。</param>
        public NetWriter(int initialCapacity = 64)
        {
            if (initialCapacity < 1)
            {
                initialCapacity = 1;
            }

            m_Buffer = new byte[initialCapacity];
            m_Length = 0;
        }

        /// <summary>
        /// 当前已写入的字节数。
        /// </summary>
        public int Length
        {
            get { return m_Length; }
        }

        /// <summary>
        /// 写入 1 字节。
        /// </summary>
        public void WriteByte(byte v)
        {
            EnsureCapacity(1);
            m_Buffer[m_Length++] = v;
        }

        /// <summary>
        /// 写入布尔值，编码为 1 字节（true => 1，false => 0）。
        /// </summary>
        public void WriteBool(bool v)
        {
            WriteByte(v ? (byte)1 : (byte)0);
        }

        /// <summary>
        /// 小端写入 16 位有符号整数。
        /// </summary>
        public void WriteShort(short v)
        {
            WriteUShort(unchecked((ushort)v));
        }

        /// <summary>
        /// 小端写入 16 位无符号整数。
        /// </summary>
        public void WriteUShort(ushort v)
        {
            EnsureCapacity(2);
            m_Buffer[m_Length++] = (byte)(v & 0xFF);
            m_Buffer[m_Length++] = (byte)((v >> 8) & 0xFF);
        }

        /// <summary>
        /// 小端写入 32 位有符号整数。
        /// </summary>
        public void WriteInt(int v)
        {
            WriteUInt(unchecked((uint)v));
        }

        /// <summary>
        /// 小端写入 32 位无符号整数。
        /// </summary>
        public void WriteUInt(uint v)
        {
            EnsureCapacity(4);
            m_Buffer[m_Length++] = (byte)(v & 0xFF);
            m_Buffer[m_Length++] = (byte)((v >> 8) & 0xFF);
            m_Buffer[m_Length++] = (byte)((v >> 16) & 0xFF);
            m_Buffer[m_Length++] = (byte)((v >> 24) & 0xFF);
        }

        /// <summary>
        /// 小端写入 64 位有符号整数。
        /// </summary>
        public void WriteLong(long v)
        {
            ulong u = unchecked((ulong)v);
            EnsureCapacity(8);
            m_Buffer[m_Length++] = (byte)(u & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 8) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 16) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 24) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 32) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 40) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 48) & 0xFF);
            m_Buffer[m_Length++] = (byte)((u >> 56) & 0xFF);
        }

        /// <summary>
        /// 小端写入 32 位 IEEE-754 单精度浮点数（按其位模式以 uint 落盘）。
        /// </summary>
        public void WriteFloat(float v)
        {
            WriteUInt(FloatBits.SingleToUInt32(v));
        }

        /// <summary>
        /// 以「ushort 长度前缀 + UTF-8 字节」写入字符串。
        /// null 写入哨兵长度 <see cref="NullLengthSentinel"/>；空串写入长度 0。
        /// </summary>
        /// <param name="v">待写入字符串，允许为 null。</param>
        /// <exception cref="ArgumentException">当 UTF-8 字节长度超过 <see cref="ushort.MaxValue"/> - 1 时抛出。</exception>
        public void WriteString(string v)
        {
            if (v == null)
            {
                WriteUShort(NullLengthSentinel);
                return;
            }

            if (v.Length == 0)
            {
                WriteUShort(0);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(v);
            if (bytes.Length >= NullLengthSentinel)
            {
                throw new ArgumentException(
                    "字符串 UTF-8 字节长度过大，无法用 ushort 长度前缀编码：" + bytes.Length, nameof(v));
            }

            WriteUShort((ushort)bytes.Length);
            WriteRaw(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// 以「int 长度前缀 + 原始字节」写入字节数组。
        /// null 写入长度 -1；空数组写入长度 0。
        /// </summary>
        /// <param name="data">待写入字节数组，允许为 null。</param>
        public void WriteBytes(byte[] data)
        {
            if (data == null)
            {
                WriteInt(-1);
                return;
            }

            WriteInt(data.Length);
            if (data.Length > 0)
            {
                WriteRaw(data, 0, data.Length);
            }
        }

        /// <summary>
        /// 将已写入内容拷贝为一个新的、长度精确的 <see cref="byte"/> 数组。
        /// </summary>
        public byte[] ToArray()
        {
            byte[] result = new byte[m_Length];
            Buffer.BlockCopy(m_Buffer, 0, result, 0, m_Length);
            return result;
        }

        /// <summary>
        /// 以零拷贝方式返回当前已写入内容的视图（共享底层缓冲，<see cref="Reset"/> 或后续写入会令其失效）。
        /// </summary>
        public ArraySegment<byte> AsSegment()
        {
            return new ArraySegment<byte>(m_Buffer, 0, m_Length);
        }

        /// <summary>
        /// 重置写入位置以复用本写入器（不释放底层缓冲）。
        /// </summary>
        public void Reset()
        {
            m_Length = 0;
        }

        /// <summary>
        /// 将一段原始字节直接追加到缓冲（无长度前缀）。
        /// </summary>
        private void WriteRaw(byte[] data, int offset, int count)
        {
            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, m_Buffer, m_Length, count);
            m_Length += count;
        }

        /// <summary>
        /// 确保底层缓冲至少还能容纳 <paramref name="additional"/> 字节，不足时按 2 倍增长。
        /// 增长按 2 倍进行，但防御 <see cref="int"/> 溢出（不让 <c>*= 2</c> 回绕为负），
        /// 并以 <see cref="MaxBufferCapacity"/> 为硬上限；单次写入若仍越限则抛出 <see cref="InvalidOperationException"/>。
        /// </summary>
        /// <exception cref="InvalidOperationException">所需容量超过 <see cref="MaxBufferCapacity"/>（含 <paramref name="additional"/> 为负或 required 溢出的情形）。</exception>
        private void EnsureCapacity(int additional)
        {
            // additional 理论上恒为正；防御性处理负值 / 溢出导致的 required 回绕。
            long requiredLong = (long)m_Length + additional;
            if (requiredLong < 0 || requiredLong > MaxBufferCapacity)
            {
                throw new InvalidOperationException(
                    "NetWriter 所需容量超过硬上限（" + MaxBufferCapacity + " 字节）：" + requiredLong);
            }

            int required = (int)requiredLong;
            if (required <= m_Buffer.Length)
            {
                return;
            }

            int newCapacity = m_Buffer.Length;
            while (newCapacity < required)
            {
                // 防御溢出：翻倍前若已过半 int.MaxValue，则直接钳到所需容量（不让 *= 2 回绕为负）。
                if (newCapacity > int.MaxValue / 2)
                {
                    newCapacity = required;
                    break;
                }

                newCapacity *= 2;
            }

            // 翻倍可能略超硬上限：再钳一次（此时 required 已 <= 上限，故安全）。
            if (newCapacity > MaxBufferCapacity)
            {
                newCapacity = MaxBufferCapacity;
            }

            byte[] newBuffer = new byte[newCapacity];
            Buffer.BlockCopy(m_Buffer, 0, newBuffer, 0, m_Length);
            m_Buffer = newBuffer;
        }
    }
}
