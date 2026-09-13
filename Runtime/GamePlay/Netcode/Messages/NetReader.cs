//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// 小端（little-endian）二进制读取器，作用于一段只读的字节区间，与 <see cref="NetWriter"/> 严格对称。
    ///
    /// 设计要点：
    /// - 所有多字节基础类型均按小端序解析；浮点经 <see cref="FloatBits"/> 由位模式无损还原。
    /// - 任何读取若越过数据末尾（underflow），抛出 <see cref="EndOfStreamException"/> 并附带清晰中文消息，
    ///   且抛出前不推进读取位置（保持区间偏移不变）。
    /// - 字符串/字节读取与写入端的长度前缀约定一致：字符串 ushort 前缀（<see cref="NetWriter.NullLengthSentinel"/>
    ///   表示 null）；字节数组 int 前缀（-1 表示 null）。
    ///
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class NetReader
    {
        private readonly byte[] m_Buffer;
        private readonly int m_Offset;
        private readonly int m_Count;
        private int m_Position;

        /// <summary>
        /// 以整个字节数组构造读取器。
        /// </summary>
        /// <param name="data">源数据，不可为 null。</param>
        public NetReader(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            m_Buffer = data;
            m_Offset = 0;
            m_Count = data.Length;
            m_Position = 0;
        }

        /// <summary>
        /// 以字节区间构造读取器（零拷贝，仅引用底层数组的一段）。
        /// </summary>
        /// <param name="data">源数据区间；其 <see cref="ArraySegment{T}.Array"/> 不可为 null。</param>
        public NetReader(ArraySegment<byte> data)
        {
            if (data.Array == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            m_Buffer = data.Array;
            m_Offset = data.Offset;
            m_Count = data.Count;
            m_Position = 0;
        }

        /// <summary>
        /// 当前相对区间起点的读取位置（已消费字节数）。
        /// </summary>
        public int Position
        {
            get { return m_Position; }
        }

        /// <summary>
        /// 区间内尚未读取的剩余字节数。
        /// </summary>
        public int Remaining
        {
            get { return m_Count - m_Position; }
        }

        /// <summary>
        /// 读取 1 字节。
        /// </summary>
        public byte ReadByte()
        {
            Require(1);
            return m_Buffer[m_Offset + m_Position++];
        }

        /// <summary>
        /// 读取 1 字节并解释为布尔值（非 0 即 true）。
        /// </summary>
        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        /// <summary>
        /// 小端读取 16 位有符号整数。
        /// </summary>
        public short ReadShort()
        {
            return unchecked((short)ReadUShort());
        }

        /// <summary>
        /// 小端读取 16 位无符号整数。
        /// </summary>
        public ushort ReadUShort()
        {
            Require(2);
            int baseIndex = m_Offset + m_Position;
            ushort value = (ushort)(
                m_Buffer[baseIndex] |
                (m_Buffer[baseIndex + 1] << 8));
            m_Position += 2;
            return value;
        }

        /// <summary>
        /// 小端读取 32 位有符号整数。
        /// </summary>
        public int ReadInt()
        {
            return unchecked((int)ReadUInt());
        }

        /// <summary>
        /// 小端读取 32 位无符号整数。
        /// </summary>
        public uint ReadUInt()
        {
            Require(4);
            int baseIndex = m_Offset + m_Position;
            uint value =
                (uint)m_Buffer[baseIndex] |
                ((uint)m_Buffer[baseIndex + 1] << 8) |
                ((uint)m_Buffer[baseIndex + 2] << 16) |
                ((uint)m_Buffer[baseIndex + 3] << 24);
            m_Position += 4;
            return value;
        }

        /// <summary>
        /// 小端读取 64 位有符号整数。
        /// </summary>
        public long ReadLong()
        {
            Require(8);
            int baseIndex = m_Offset + m_Position;
            ulong value =
                (ulong)m_Buffer[baseIndex] |
                ((ulong)m_Buffer[baseIndex + 1] << 8) |
                ((ulong)m_Buffer[baseIndex + 2] << 16) |
                ((ulong)m_Buffer[baseIndex + 3] << 24) |
                ((ulong)m_Buffer[baseIndex + 4] << 32) |
                ((ulong)m_Buffer[baseIndex + 5] << 40) |
                ((ulong)m_Buffer[baseIndex + 6] << 48) |
                ((ulong)m_Buffer[baseIndex + 7] << 56);
            m_Position += 8;
            return unchecked((long)value);
        }

        /// <summary>
        /// 小端读取 32 位 IEEE-754 单精度浮点数。
        /// </summary>
        public float ReadFloat()
        {
            return FloatBits.UInt32ToSingle(ReadUInt());
        }

        /// <summary>
        /// 读取「ushort 长度前缀 + UTF-8 字节」编码的字符串。
        /// 前缀为 <see cref="NetWriter.NullLengthSentinel"/> 时返回 null；前缀为 0 时返回空串。
        /// </summary>
        public string ReadString()
        {
            ushort length = ReadUShort();
            if (length == NetWriter.NullLengthSentinel)
            {
                return null;
            }

            if (length == 0)
            {
                return string.Empty;
            }

            Require(length);
            string value = Encoding.UTF8.GetString(m_Buffer, m_Offset + m_Position, length);
            m_Position += length;
            return value;
        }

        /// <summary>
        /// 读取「int 长度前缀 + 原始字节」编码的字节数组。
        /// 前缀为 -1 时返回 null；前缀为 0 时返回空数组。
        /// </summary>
        /// <exception cref="EndOfStreamException">当长度前缀为非法负值（小于 -1）时抛出。</exception>
        public byte[] ReadBytes()
        {
            int length = ReadInt();
            if (length == -1)
            {
                return null;
            }

            if (length < 0)
            {
                throw new EndOfStreamException("读取字节数组时遇到非法的负长度前缀：" + length);
            }

            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            Require(length);
            byte[] result = new byte[length];
            Buffer.BlockCopy(m_Buffer, m_Offset + m_Position, result, 0, length);
            m_Position += length;
            return result;
        }

        /// <summary>
        /// 校验区间内至少还剩 <paramref name="needed"/> 字节，不足则在不推进位置的前提下抛出。
        /// </summary>
        private void Require(int needed)
        {
            if (Remaining < needed)
            {
                throw new EndOfStreamException(
                    "读取越界：还需 " + needed + " 字节，但仅剩 " + Remaining + " 字节（位置 " + m_Position +
                    "，区间长度 " + m_Count + "）。");
            }
        }
    }
}
