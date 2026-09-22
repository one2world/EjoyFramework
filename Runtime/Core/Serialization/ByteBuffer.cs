//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;

namespace EjoyFramework.Core.Serialization
{
    /// <summary>
    /// 高性能二进制读写缓冲（小端）。代码生成序列化的底座。
    ///
    /// 设计要点（高性能）：
    ///   • 单一可增长 <c>byte[]</c> + 独立读/写游标；所有数值原语用<b>位移</b>读写，
    ///     不经 <c>BinaryWriter</c>、不装箱、不产生临时数组。
    ///   • 实现 <see cref="IReference"/>，经 <see cref="ReferencePool"/> 复用，序列化热路径零 GC。
    ///   • 字符串写入用 <c>Encoding.UTF8.GetBytes(string,int,int,byte[],int)</c> 直接写进内部缓冲（写路径零分配）；
    ///     读取必然分配结果 string（语义所需）。
    ///
    /// 典型用法：
    /// <code>
    /// var buf = ByteBuffer.Acquire();
    /// playerState.Serialize(buf);            // 生成代码逐字段写入
    /// byte[] bytes = buf.ToArray();
    /// buf.Release();
    ///
    /// var rbuf = ByteBuffer.Acquire(bytes);  // 包裹已有字节
    /// var restored = new PlayerState();
    /// restored.Deserialize(rbuf);
    /// rbuf.Release();
    /// </code>
    /// </summary>
    public sealed class ByteBuffer : IReference
    {
        private const int DefaultCapacity = 1024;
        // Release/Clear 时若缓冲超过此阈值则收缩，避免池中长期持有超大数组。
        private const int MaxPooledCapacity = 64 * 1024;

        private byte[] m_Buffer;
        private int m_WritePos;
        private int m_ReadPos;
        // false 表示 m_Buffer 是外部 Wrap 进来的数组（本对象不拥有），Clear 时需丢弃以免后续写入污染外部数据。
        private bool m_Owned;

        public ByteBuffer()
        {
            m_Buffer = BufferPool<byte>.Rent(DefaultCapacity);
            m_Owned = true;
        }

        // ================================================================
        //  池工厂
        // ================================================================

        /// <summary>从引用池取一个空缓冲（用于写入）。</summary>
        public static ByteBuffer Acquire()
        {
            return ReferencePool.Acquire<ByteBuffer>();
        }

        /// <summary>从引用池取一个缓冲并包裹已有字节（用于读取）。不拷贝 <paramref name="data"/>。</summary>
        public static ByteBuffer Acquire(byte[] data)
        {
            ByteBuffer buffer = ReferencePool.Acquire<ByteBuffer>();
            buffer.Wrap(data);
            return buffer;
        }

        /// <summary>归还到引用池（等价于 <c>ReferencePool.Release(this)</c>）。</summary>
        public void Release()
        {
            ReferencePool.Release(this);
        }

        // ================================================================
        //  状态
        // ================================================================

        /// <summary>已写入的字节数。</summary>
        public int Length { get { return m_WritePos; } }

        /// <summary>当前读游标位置。</summary>
        public int ReadPosition { get { return m_ReadPos; } }

        /// <summary>底层缓冲容量。</summary>
        public int Capacity { get { return m_Buffer.Length; } }

        /// <summary>尚未读取的字节数（= Length - ReadPosition）。</summary>
        public int Remaining { get { return m_WritePos - m_ReadPos; } }

        /// <summary>底层缓冲（只读用途，长度可能大于 Length；有效数据为 [0, Length)）。</summary>
        public byte[] RawBuffer { get { return m_Buffer; } }

        // ================================================================
        //  生命周期
        // ================================================================

        /// <summary>IReference：Release 时由引用池调用。重置游标；外部 Wrap 的数组被丢弃，超大缓冲被收缩。</summary>
        public void Clear()
        {
            m_WritePos = 0;
            m_ReadPos = 0;
            if (!m_Owned || m_Buffer.Length > MaxPooledCapacity)
            {
                // 超大缓冲交还 BufferPool（Wrap 的外部数组不归我们，不能还）；默认容量从池里拿。
                if (m_Owned) BufferPool<byte>.Return(m_Buffer);
                m_Buffer = BufferPool<byte>.Rent(DefaultCapacity);
                m_Owned = true;
            }
        }

        /// <summary>重置读写游标，保留底层容量（不释放数组）。用于复用同一缓冲做多轮写入。</summary>
        public void Reset()
        {
            m_WritePos = 0;
            m_ReadPos = 0;
        }

        /// <summary>把读游标拨回起点，用于重读已写入的数据。</summary>
        public void RewindRead()
        {
            m_ReadPos = 0;
        }

        /// <summary>包裹一段已有字节用于读取。不拷贝；本对象在 Clear 前不应再写入。</summary>
        public void Wrap(byte[] data)
        {
            if (data == null) throw new FrameworkException("ByteBuffer.Wrap data is null.");
            m_Buffer = data;
            m_WritePos = data.Length;
            m_ReadPos = 0;
            m_Owned = false;
        }

        /// <summary>把有效数据 [0, Length) 拷贝成新数组返回。</summary>
        public byte[] ToArray()
        {
            byte[] result = new byte[m_WritePos];
            Array.Copy(m_Buffer, 0, result, 0, m_WritePos);
            return result;
        }

        // ================================================================
        //  写入（小端，位移，零分配）
        // ================================================================

        public void WriteByte(byte value)
        {
            EnsureWritable(1);
            m_Buffer[m_WritePos++] = value;
        }

        public void WriteSByte(sbyte value)
        {
            WriteByte((byte)value);
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)1 : (byte)0);
        }

        public void WriteShort(short value)
        {
            WriteUShort((ushort)value);
        }

        public void WriteUShort(ushort value)
        {
            EnsureWritable(2);
            byte[] b = m_Buffer;
            int p = m_WritePos;
            b[p]     = (byte)value;
            b[p + 1] = (byte)(value >> 8);
            m_WritePos = p + 2;
        }

        public void WriteInt(int value)
        {
            WriteUInt((uint)value);
        }

        public void WriteUInt(uint value)
        {
            EnsureWritable(4);
            byte[] b = m_Buffer;
            int p = m_WritePos;
            b[p]     = (byte)value;
            b[p + 1] = (byte)(value >> 8);
            b[p + 2] = (byte)(value >> 16);
            b[p + 3] = (byte)(value >> 24);
            m_WritePos = p + 4;
        }

        public void WriteLong(long value)
        {
            WriteULong((ulong)value);
        }

        public void WriteULong(ulong value)
        {
            EnsureWritable(8);
            byte[] b = m_Buffer;
            int p = m_WritePos;
            b[p]     = (byte)value;
            b[p + 1] = (byte)(value >> 8);
            b[p + 2] = (byte)(value >> 16);
            b[p + 3] = (byte)(value >> 24);
            b[p + 4] = (byte)(value >> 32);
            b[p + 5] = (byte)(value >> 40);
            b[p + 6] = (byte)(value >> 48);
            b[p + 7] = (byte)(value >> 56);
            m_WritePos = p + 8;
        }

        public void WriteFloat(float value)
        {
            // SingleToInt32Bits 无分配（.NET Standard 2.1）；避免 BitConverter.GetBytes 的临时数组。
            WriteUInt((uint)BitConverter.SingleToInt32Bits(value));
        }

        public void WriteDouble(double value)
        {
            WriteULong((ulong)BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>写字符串：Int32 长度前缀（-1 表示 null）+ UTF8 字节。写路径零分配。</summary>
        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteInt(-1);
                return;
            }
            if (value.Length == 0)
            {
                WriteInt(0);
                return;
            }
            int byteCount = Encoding.UTF8.GetByteCount(value);
            WriteInt(byteCount);
            EnsureWritable(byteCount);
            int written = Encoding.UTF8.GetBytes(value, 0, value.Length, m_Buffer, m_WritePos);
            m_WritePos += written;
        }

        /// <summary>写原始字节段（无长度前缀）。</summary>
        public void WriteRawBytes(byte[] value, int offset, int count)
        {
            if (value == null) throw new FrameworkException("WriteRawBytes value is null.");
            if (count <= 0) return;
            EnsureWritable(count);
            Array.Copy(value, offset, m_Buffer, m_WritePos, count);
            m_WritePos += count;
        }

        /// <summary>写字节数组：Int32 长度前缀（-1 表示 null）+ 内容。</summary>
        public void WriteBytes(byte[] value)
        {
            if (value == null)
            {
                WriteInt(-1);
                return;
            }
            WriteInt(value.Length);
            WriteRawBytes(value, 0, value.Length);
        }

        // ================================================================
        //  读取（小端，位移）
        // ================================================================

        public byte ReadByte()
        {
            EnsureReadable(1);
            return m_Buffer[m_ReadPos++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public bool ReadBool()
        {
            return ReadByte() != 0;
        }

        public short ReadShort()
        {
            return (short)ReadUShort();
        }

        public ushort ReadUShort()
        {
            EnsureReadable(2);
            byte[] b = m_Buffer;
            int p = m_ReadPos;
            ushort value = (ushort)(b[p] | (b[p + 1] << 8));
            m_ReadPos = p + 2;
            return value;
        }

        public int ReadInt()
        {
            return (int)ReadUInt();
        }

        public uint ReadUInt()
        {
            EnsureReadable(4);
            byte[] b = m_Buffer;
            int p = m_ReadPos;
            uint value = (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            m_ReadPos = p + 4;
            return value;
        }

        public long ReadLong()
        {
            return (long)ReadULong();
        }

        public ulong ReadULong()
        {
            EnsureReadable(8);
            byte[] b = m_Buffer;
            int p = m_ReadPos;
            uint lo = (uint)(b[p]     | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
            uint hi = (uint)(b[p + 4] | (b[p + 5] << 8) | (b[p + 6] << 16) | (b[p + 7] << 24));
            m_ReadPos = p + 8;
            return ((ulong)hi << 32) | lo;
        }

        public float ReadFloat()
        {
            return BitConverter.Int32BitsToSingle((int)ReadUInt());
        }

        public double ReadDouble()
        {
            return BitConverter.Int64BitsToDouble((long)ReadULong());
        }

        /// <summary>读字符串（与 <see cref="WriteString"/> 对应）。返回值可能为 null。</summary>
        public string ReadString()
        {
            int byteCount = ReadInt();
            if (byteCount < 0) return null;
            if (byteCount == 0) return string.Empty;
            EnsureReadable(byteCount);
            string value = Encoding.UTF8.GetString(m_Buffer, m_ReadPos, byteCount);
            m_ReadPos += byteCount;
            return value;
        }

        /// <summary>读原始字节段到调用方数组（无长度前缀，与 <see cref="WriteRawBytes"/> 对应），零分配。</summary>
        public void ReadRawBytes(byte[] destination, int offset, int count)
        {
            if (destination == null) throw new FrameworkException("ReadRawBytes destination is null.");
            if (count <= 0) return;
            if (offset < 0 || (long)offset + count > destination.Length) throw new FrameworkException("ReadRawBytes destination range is invalid.");
            EnsureReadable(count);
            Array.Copy(m_Buffer, m_ReadPos, destination, offset, count);
            m_ReadPos += count;
        }

        /// <summary>跳过 count 个字节。</summary>
        public void Skip(int count)
        {
            if (count <= 0) return;
            EnsureReadable(count);
            m_ReadPos += count;
        }

        /// <summary>读字节数组（与 <see cref="WriteBytes"/> 对应）。返回值可能为 null。</summary>
        public byte[] ReadBytes()
        {
            int count = ReadInt();
            if (count < 0) return null;
            if (count == 0) return Array.Empty<byte>();
            EnsureReadable(count);
            byte[] result = new byte[count];
            Array.Copy(m_Buffer, m_ReadPos, result, 0, count);
            m_ReadPos += count;
            return result;
        }

        // ================================================================
        //  内部
        // ================================================================

        // byte[] 的 .NET 最大长度（略小于 int.MaxValue）。
        private const int MaxCapacity = 0x7FFFFFC7;

        private void EnsureWritable(int count)
        {
            if (count < 0) throw new FrameworkException("ByteBuffer write count is negative.");
            // 用 long 计算避免 m_WritePos + count 整型溢出转负后绕过容量检查。
            long required = (long)m_WritePos + count;
            if (required <= m_Buffer.Length) return;
            if (required > MaxCapacity)
                throw new FrameworkException("ByteBuffer required capacity exceeds max byte[] length: " + required);

            long newCapacity = m_Buffer.Length > 0 ? m_Buffer.Length : DefaultCapacity;
            while (newCapacity < required) newCapacity <<= 1;   // long 累乘，不会溢出到负
            if (newCapacity > MaxCapacity) newCapacity = MaxCapacity;
            // 扩容走 BufferPool：容量按 2 的幂增长，正好命中桶尺寸；旧的自有数组归还复用。
            byte[] grown = BufferPool<byte>.Rent((int)newCapacity);
            Array.Copy(m_Buffer, 0, grown, 0, m_WritePos);
            if (m_Owned) BufferPool<byte>.Return(m_Buffer);
            m_Buffer = grown;
            m_Owned = true;
        }

        private void EnsureReadable(int count)
        {
            // 用 long 比较并显式拒绝负数：count 可能来自不可信报文的长度前缀（ReadString/ReadBytes），
            // 若用 int 相加，极大的 count 会溢出回绕为负、绕过越界检查，进而造成 OOB 读取（崩溃/内存泄露）。
            if (count < 0 || (long)m_ReadPos + count > m_WritePos)
            {
                throw new FrameworkException(string.Format(
                    "ByteBuffer read out of range: need {0} byte(s) at pos {1}, but only {2} written.",
                    count, m_ReadPos, m_WritePos));
            }
        }
    }
}
