//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Serialization;

namespace EjoyFramework.Core.Streaming
{
    /// <summary>
    /// 流式世界状态存储：按单元 (layer, cx, cz) 保存"离开时的增量"（被拾取的物品、被击杀的怪、被打开的门……），
    /// 单元再次加载时恢复。这是开放世界"世界会记住你做过什么"的底座。
    ///
    /// 设计：
    ///   • 每个单元一条记录：字节负载来自 <see cref="BufferPool{T}"/>（按记录长度租借），记录更新时复用或换更大的租借块；
    ///     不为每次写入分配托管数组。
    ///   • 全局段（<see cref="SetGlobal"/>）给与单元无关的世界状态（昼夜、天气种子、全局旗标）。
    ///   • 脏标记：<see cref="IsDirty"/> 自上次 <see cref="MarkClean"/>（通常在成功写盘后）以来是否有变更，供自动存档节流。
    ///   • 序列化：<see cref="WriteTo"/> / <see cref="ReadFrom"/>（ByteBuffer，带魔数与格式版本），与 Save 模块组合：
    ///     业务把 <see cref="ToArray"/> 的字节放进自己的存档结构，或用 <see cref="WorldStateSaveData"/> 直接 WriteSlot。
    ///
    /// 线程契约：仅主线程。
    /// </summary>
    public sealed class WorldStateStore
    {
        private const uint Magic = 0x53575745;   // 'EWWS'
        private const int FormatVersion = 1;

        private sealed class Record
        {
            public byte[] Buffer;
            public int Length;
        }

        private readonly Dictionary<long, Record> m_Cells = new Dictionary<long, Record>();
        private readonly Dictionary<int, Record> m_Globals = new Dictionary<int, Record>();
        private readonly Stack<Record> m_RecordPool = new Stack<Record>();
        private long m_TotalBytes;
        private bool m_Dirty;

        /// <summary>单元记录数。</summary>
        public int CellCount { get { return m_Cells.Count; } }

        /// <summary>全局段数。</summary>
        public int GlobalCount { get { return m_Globals.Count; } }

        /// <summary>全部记录的负载字节总和。</summary>
        public long TotalBytes { get { return m_TotalBytes; } }

        /// <summary>自上次 MarkClean 以来是否有变更。</summary>
        public bool IsDirty { get { return m_Dirty; } }

        /// <summary>标记已持久化。</summary>
        public void MarkClean() { m_Dirty = false; }

        // ================================================================
        //  单元记录
        // ================================================================

        /// <summary>写入/覆盖单元记录（拷贝 <paramref name="count"/> 字节）。count = 0 等价于 <see cref="RemoveCell"/>。</summary>
        public void SetCell(int layer, int cx, int cz, byte[] source, int offset, int count)
        {
            Set(m_Cells, CellKey(layer, cx, cz), source, offset, count);
        }

        /// <summary>写入单元记录：负载取自 <paramref name="source"/> 的 [0, Length)。</summary>
        public void SetCell(int layer, int cx, int cz, ByteBuffer source)
        {
            if (source == null) throw new FrameworkException("WorldStateStore.SetCell：source 不能为 null。");
            Set(m_Cells, CellKey(layer, cx, cz), source.RawBuffer, 0, source.Length);
        }

        /// <summary>读取单元记录：返回内部缓冲（长度 ≥ length，只读，不得保存引用）。</summary>
        public bool TryGetCell(int layer, int cx, int cz, out byte[] buffer, out int length)
        {
            Record r;
            if (m_Cells.TryGetValue(CellKey(layer, cx, cz), out r))
            {
                buffer = r.Buffer;
                length = r.Length;
                return true;
            }

            buffer = null;
            length = 0;
            return false;
        }

        /// <summary>把单元记录写进 <paramref name="destination"/>（先 Reset）。不存在返回 false。</summary>
        public bool TryReadCell(int layer, int cx, int cz, ByteBuffer destination)
        {
            if (destination == null) throw new FrameworkException("WorldStateStore.TryReadCell：destination 不能为 null。");
            Record r;
            if (!m_Cells.TryGetValue(CellKey(layer, cx, cz), out r)) return false;
            destination.Reset();
            destination.WriteRawBytes(r.Buffer, 0, r.Length);
            return true;
        }

        /// <summary>是否有单元记录。</summary>
        public bool HasCell(int layer, int cx, int cz)
        {
            return m_Cells.ContainsKey(CellKey(layer, cx, cz));
        }

        /// <summary>删除单元记录。</summary>
        public bool RemoveCell(int layer, int cx, int cz)
        {
            return Remove(m_Cells, CellKey(layer, cx, cz));
        }

        // ================================================================
        //  全局段
        // ================================================================

        public void SetGlobal(int sectionId, byte[] source, int offset, int count)
        {
            Set(m_Globals, sectionId, source, offset, count);
        }

        public void SetGlobal(int sectionId, ByteBuffer source)
        {
            if (source == null) throw new FrameworkException("WorldStateStore.SetGlobal：source 不能为 null。");
            Set(m_Globals, sectionId, source.RawBuffer, 0, source.Length);
        }

        public bool TryGetGlobal(int sectionId, out byte[] buffer, out int length)
        {
            Record r;
            if (m_Globals.TryGetValue(sectionId, out r))
            {
                buffer = r.Buffer;
                length = r.Length;
                return true;
            }

            buffer = null;
            length = 0;
            return false;
        }

        public bool RemoveGlobal(int sectionId)
        {
            return Remove(m_Globals, sectionId);
        }

        /// <summary>清空全部记录（新游戏 / 换存档）。</summary>
        public void Clear()
        {
            foreach (KeyValuePair<long, Record> kv in m_Cells) ReleaseRecord(kv.Value);
            foreach (KeyValuePair<int, Record> kv in m_Globals) ReleaseRecord(kv.Value);
            m_Cells.Clear();
            m_Globals.Clear();
            m_TotalBytes = 0;
            m_Dirty = true;
        }

        // ================================================================
        //  序列化
        // ================================================================

        /// <summary>
        /// 格式：magic | version | cellCount | { layer, cx, cz, len, bytes }* | globalCount | { sectionId, len, bytes }*
        /// </summary>
        public void WriteTo(ByteBuffer buffer)
        {
            if (buffer == null) throw new FrameworkException("WorldStateStore.WriteTo：buffer 不能为 null。");
            buffer.WriteUInt(Magic);
            buffer.WriteInt(FormatVersion);
            buffer.WriteInt(m_Cells.Count);
            foreach (KeyValuePair<long, Record> kv in m_Cells)
            {
                int layer, cx, cz;
                UnpackKey(kv.Key, out layer, out cx, out cz);
                buffer.WriteInt(layer);
                buffer.WriteInt(cx);
                buffer.WriteInt(cz);
                buffer.WriteInt(kv.Value.Length);
                buffer.WriteRawBytes(kv.Value.Buffer, 0, kv.Value.Length);
            }

            buffer.WriteInt(m_Globals.Count);
            foreach (KeyValuePair<int, Record> kv in m_Globals)
            {
                buffer.WriteInt(kv.Key);
                buffer.WriteInt(kv.Value.Length);
                buffer.WriteRawBytes(kv.Value.Buffer, 0, kv.Value.Length);
            }
        }

        /// <summary>从缓冲读入（先 Clear）。格式错误抛出 FrameworkException 且存储保持为空。</summary>
        public void ReadFrom(ByteBuffer buffer)
        {
            if (buffer == null) throw new FrameworkException("WorldStateStore.ReadFrom：buffer 不能为 null。");
            Clear();
            try
            {
                ReadFromCore(buffer);
            }
            catch
            {
                Clear();   // 半读入的记录不得残留
                throw;
            }

            m_Dirty = false;
        }

        private void ReadFromCore(ByteBuffer buffer)
        {
            if (buffer.ReadUInt() != Magic) throw new FrameworkException("WorldStateStore.ReadFrom：魔数不匹配，不是世界状态数据。");
            int version = buffer.ReadInt();
            if (version != FormatVersion) throw new FrameworkException(Utility.Text.Format("WorldStateStore.ReadFrom：格式版本 {0} 不支持（当前 {1}）。", version, FormatVersion));

            int cellCount = buffer.ReadInt();
            if (cellCount < 0) throw new FrameworkException("WorldStateStore.ReadFrom：单元数非法。");
            for (int i = 0; i < cellCount; i++)
            {
                int layer = buffer.ReadInt();
                int cx = buffer.ReadInt();
                int cz = buffer.ReadInt();
                int len = buffer.ReadInt();
                if (len < 0 || len > buffer.Remaining) throw new FrameworkException("WorldStateStore.ReadFrom：单元记录长度非法。");
                Record r = AcquireRecord(len);
                buffer.ReadRawBytes(r.Buffer, 0, len);
                r.Length = len;
                m_Cells[CellKey(layer, cx, cz)] = r;
                m_TotalBytes += len;
            }

            int globalCount = buffer.ReadInt();
            if (globalCount < 0) throw new FrameworkException("WorldStateStore.ReadFrom：全局段数非法。");
            for (int i = 0; i < globalCount; i++)
            {
                int id = buffer.ReadInt();
                int len = buffer.ReadInt();
                if (len < 0 || len > buffer.Remaining) throw new FrameworkException("WorldStateStore.ReadFrom：全局段长度非法。");
                Record r = AcquireRecord(len);
                buffer.ReadRawBytes(r.Buffer, 0, len);
                r.Length = len;
                m_Globals[id] = r;
                m_TotalBytes += len;
            }
        }

        /// <summary>序列化为新数组（用于放进存档结构）。</summary>
        public byte[] ToArray()
        {
            ByteBuffer buffer = ByteBuffer.Acquire();
            try
            {
                WriteTo(buffer);
                return buffer.ToArray();
            }
            finally
            {
                buffer.Release();
            }
        }

        /// <summary>从数组读入。</summary>
        public void Load(byte[] bytes)
        {
            if (bytes == null) throw new FrameworkException("WorldStateStore.Load：bytes 不能为 null。");
            ByteBuffer buffer = ByteBuffer.Acquire(bytes);
            try
            {
                ReadFrom(buffer);
            }
            finally
            {
                buffer.Release();
            }
        }

        // ================================================================
        //  内部
        // ================================================================

        private void Set<TKey>(Dictionary<TKey, Record> table, TKey key, byte[] source, int offset, int count)
        {
            if (count < 0 || (source == null && count > 0) || (source != null && ((long)offset + count > source.Length || offset < 0)))
            {
                throw new FrameworkException("WorldStateStore：写入区间非法。");
            }

            if (count == 0)
            {
                Remove(table, key);
                return;
            }

            Record r;
            if (table.TryGetValue(key, out r))
            {
                m_TotalBytes -= r.Length;
                if (r.Buffer.Length < count)
                {
                    BufferPool<byte>.Return(r.Buffer);
                    r.Buffer = BufferPool<byte>.Rent(count);
                }
            }
            else
            {
                r = AcquireRecord(count);
                table.Add(key, r);
            }

            Array.Copy(source, offset, r.Buffer, 0, count);
            r.Length = count;
            m_TotalBytes += count;
            m_Dirty = true;
        }

        private bool Remove<TKey>(Dictionary<TKey, Record> table, TKey key)
        {
            Record r;
            if (!table.TryGetValue(key, out r)) return false;
            table.Remove(key);
            m_TotalBytes -= r.Length;
            ReleaseRecord(r);
            m_Dirty = true;
            return true;
        }

        private Record AcquireRecord(int minBytes)
        {
            Record r = m_RecordPool.Count > 0 ? m_RecordPool.Pop() : new Record();
            r.Buffer = BufferPool<byte>.Rent(minBytes < 1 ? 1 : minBytes);
            r.Length = 0;
            return r;
        }

        private void ReleaseRecord(Record r)
        {
            if (r.Buffer != null) BufferPool<byte>.Return(r.Buffer);
            r.Buffer = null;
            r.Length = 0;
            m_RecordPool.Push(r);
        }

        /// <summary>layer 8 位 | cx 28 位 | cz 28 位（有符号，各 ±2^27 个单元）。</summary>
        private static long CellKey(int layer, int cx, int cz)
        {
            return ((long)(layer & 0xFF) << 56) | ((long)(cx & 0x0FFFFFFF) << 28) | (long)(cz & 0x0FFFFFFF);
        }

        private static void UnpackKey(long key, out int layer, out int cx, out int cz)
        {
            layer = (int)((key >> 56) & 0xFF);
            cx = SignExtend28((int)((key >> 28) & 0x0FFFFFFF));
            cz = SignExtend28((int)(key & 0x0FFFFFFF));
        }

        private static int SignExtend28(int v)
        {
            return (v << 4) >> 4;
        }
    }

    /// <summary>可直接经 ISaveManager.WriteSlot 存取的世界状态载体。</summary>
    [Serializable]
    public sealed class WorldStateSaveData
    {
        public byte[] Bytes;
    }
}
