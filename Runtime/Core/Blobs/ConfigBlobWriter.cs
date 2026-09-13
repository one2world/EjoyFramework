//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// ConfigBlob 的构建器。放在 Runtime 程序集内只是为了与读侧共用同一份格式常量与哈希实现，
    /// <b>仅供构建期（Editor 代码生成/导表）使用</b>，运行期不应引用。
    ///
    /// 典型用法：
    /// <code>
    /// var writer = new ConfigBlobWriter(schemaHash);
    /// writer.BeginTable("Monsters", rowSize: 24);
    /// int row = writer.AddRow(primaryKey: 1001);
    /// writer.SetInt32(row, 0, 1001);
    /// writer.SetSingle(row, 4, 12.5f);
    /// writer.SetString(row, 8, "小妖");
    /// writer.EndTable();
    /// byte[] blob = writer.Build();
    /// </code>
    ///
    /// 设计理由：
    ///   • "先收集、后布局"两阶段：行数据先各自攒在表内缓冲里，字符串字段先只记录一条待回填记录；
    ///     等所有表收集完才计算字符串池的起始位置并统一回填。这样字符串池能放在文件尾部（读侧顺序更友好），
    ///     又不需要预先知道总长度。
    ///   • 字符串在构建期全局去重（内容 → 池内偏移的字典）：配置里大量重复的类型名、图标名、描述前缀
    ///     只存一份，且相同内容天然得到相同偏移，读侧可以直接用偏移做 O(1) 相等判断。
    ///   • 表目录按 nameHash 升序落盘，读侧才能二分查表；排序在 <see cref="Build"/> 里完成，
    ///     调用方无需关心 BeginTable 的顺序。
    ///   • 构建期把能查的都查掉（重复表名、哈希碰撞、重复主键、字段越界、rowSize 不对齐），
    ///     宁可构建失败也不要把坏数据带到运行期——运行期只有边界检查兜底，报错信息远不如这里精确。
    ///
    /// 线程契约：非线程安全，单线程构建使用。
    /// </summary>
    public sealed class ConfigBlobWriter
    {
        private sealed class TableBuilder
        {
            public string Name;
            public ulong NameHash;
            public int RowSize;
            public int RowCount;
            public byte[] RowBytes;
            public readonly List<int> PrimaryKeys = new List<int>();
            public readonly HashSet<int> PrimaryKeySet = new HashSet<int>();

            // 待回填的字符串字段：key = (行号 << 32 | 行内字段偏移)，value = 字符串编号。
            // 字符串池位置在最后才确定，收集阶段只能先记账。
            // 用字典而不是列表：同一字段被重复 SetString 时必须"后写覆盖先写"，
            // 列表会让先写的记录在回填阶段再次生效（尤其"先写非空、后写空串"会把 0 覆盖回旧偏移）。
            public readonly Dictionary<long, int> StringFixups = new Dictionary<long, int>();

            public int TableOffset;
            public int TableSize;
        }

        /// <summary>把 (行号, 行内字段偏移) 组合成字符串回填表的唯一键。</summary>
        private static long FixupKey(int rowIndex, int fieldOffset)
        {
            return ((long)rowIndex << 32) | (uint)fieldOffset;
        }

        /// <summary>
        /// 表数据整体的对齐粒度。取 8 让每张表的行区起点落在 8 字节边界上。
        /// 注意这<b>不</b>保证行内 64 位字段自然对齐——只要 rowSize 不是 8 的倍数，
        /// 第 1 行起就会错位。读取的正确性不依赖对齐：ConfigBlob 的读原语一律逐字节组装，
        /// 与对齐无关。这里取 8 只是让常见情形（rowSize 为 8 倍数）顺带拿到对齐收益。
        /// </summary>
        private const int TableAlignment = 8;

        /// <summary>字符串池内每条记录的对齐粒度。</summary>
        private const int StringAlignment = 4;

        private readonly ulong m_SchemaHash;
        private readonly List<TableBuilder> m_Tables = new List<TableBuilder>();
        private readonly Dictionary<string, ulong> m_TableNameHashes = new Dictionary<string, ulong>();

        // 字符串去重池：内容 → 编号；编号即 m_Strings 中的下标。
        private readonly Dictionary<string, int> m_StringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<byte[]> m_StringBytes = new List<byte[]>();

        private TableBuilder m_Current;
        private bool m_Built;

        /// <summary>
        /// 构造构建器。
        /// </summary>
        /// <param name="schemaHash">与生成代码一致的 schemaHash，见 <see cref="ConfigBlobHash.ComputeSchemaHash"/>。</param>
        public ConfigBlobWriter(ulong schemaHash)
        {
            m_SchemaHash = schemaHash;
        }

        /// <summary>已开始收集的表数量。</summary>
        public int TableCount
        {
            get { return m_Tables.Count; }
        }

        // ================================================================
        //  表
        // ================================================================

        /// <summary>
        /// 开始一张表。必须与 <see cref="EndTable"/> 成对使用。
        /// </summary>
        /// <param name="name">表名（决定 nameHash，读侧据此查表）。</param>
        /// <param name="rowSize">单行字节数，必须为 4 的倍数（codegen 应在行尾补齐 padding）。</param>
        public void BeginTable(string name, int rowSize)
        {
            CheckNotBuilt();
            if (m_Current != null)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.BeginTable：表 \"{0}\" 尚未 EndTable。", m_Current.Name));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new FrameworkException("ConfigBlobWriter.BeginTable：表名不能为空。");
            }

            if (rowSize <= 0 || rowSize % 4 != 0)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.BeginTable：表 \"{0}\" 的 rowSize 必须为正且是 4 的倍数（当前 {1}）。代码生成应把行尾补齐到 4 字节。", name, rowSize));
            }

            ulong nameHash = ConfigBlobHash.Compute(name);
            if (m_TableNameHashes.ContainsKey(name))
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.BeginTable：表名 \"{0}\" 重复。", name));
            }

            // 哈希碰撞必须遍历检测：读侧只按 nameHash 查表，两张不同名的表撞哈希会导致静默串表。
            foreach (KeyValuePair<string, ulong> pair in m_TableNameHashes)
            {
                if (pair.Value == nameHash)
                {
                    throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.BeginTable：表名 \"{0}\" 与 \"{1}\" 的 FNV1a-64 哈希碰撞，请改名其中之一。", name, pair.Key));
                }
            }

            m_TableNameHashes.Add(name, nameHash);
            m_Current = new TableBuilder
            {
                Name = name,
                NameHash = nameHash,
                RowSize = rowSize,
                RowBytes = new byte[rowSize * 16],
            };
        }

        /// <summary>
        /// 结束当前表。
        /// </summary>
        public void EndTable()
        {
            CheckNotBuilt();
            if (m_Current == null)
            {
                throw new FrameworkException("ConfigBlobWriter.EndTable：当前没有正在收集的表。");
            }

            m_Tables.Add(m_Current);
            m_Current = null;
        }

        /// <summary>
        /// 向当前表追加一行（字段初值全为 0），返回行号。
        /// </summary>
        /// <param name="primaryKey">该行主键，表内唯一。</param>
        /// <returns>行号，用于后续 SetXxx。</returns>
        public int AddRow(int primaryKey)
        {
            TableBuilder table = RequireCurrent();
            if (!table.PrimaryKeySet.Add(primaryKey))
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.AddRow：表 \"{0}\" 存在重复主键 {1}。", table.Name, primaryKey));
            }

            int rowIndex = table.RowCount;
            int required = (rowIndex + 1) * table.RowSize;
            if (required > table.RowBytes.Length)
            {
                int capacity = table.RowBytes.Length;
                while (capacity < required)
                {
                    capacity <<= 1;
                }

                Array.Resize(ref table.RowBytes, capacity);
            }

            table.PrimaryKeys.Add(primaryKey);
            table.RowCount = rowIndex + 1;
            return rowIndex;
        }

        // ================================================================
        //  字段写入
        // ================================================================

        /// <summary>写 1 字节。</summary>
        public void SetByte(int rowIndex, int fieldOffset, byte value)
        {
            int at = FieldPosition(rowIndex, fieldOffset, 1);
            m_Current.RowBytes[at] = value;
        }

        /// <summary>写有符号 1 字节。</summary>
        public void SetSByte(int rowIndex, int fieldOffset, sbyte value)
        {
            SetByte(rowIndex, fieldOffset, (byte)value);
        }

        /// <summary>写 1 字节布尔。</summary>
        public void SetBoolean(int rowIndex, int fieldOffset, bool value)
        {
            SetByte(rowIndex, fieldOffset, value ? (byte)1 : (byte)0);
        }

        /// <summary>写 16 位有符号整数。</summary>
        public void SetInt16(int rowIndex, int fieldOffset, short value)
        {
            SetUInt16(rowIndex, fieldOffset, (ushort)value);
        }

        /// <summary>写 16 位无符号整数。</summary>
        public void SetUInt16(int rowIndex, int fieldOffset, ushort value)
        {
            int at = FieldPosition(rowIndex, fieldOffset, 2);
            byte[] buffer = m_Current.RowBytes;
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
        }

        /// <summary>写 32 位有符号整数。</summary>
        public void SetInt32(int rowIndex, int fieldOffset, int value)
        {
            int at = FieldPosition(rowIndex, fieldOffset, 4);
            WriteInt32(m_Current.RowBytes, at, value);
        }

        /// <summary>写 32 位无符号整数。</summary>
        public void SetUInt32(int rowIndex, int fieldOffset, uint value)
        {
            SetInt32(rowIndex, fieldOffset, (int)value);
        }

        /// <summary>写 64 位有符号整数。</summary>
        public void SetInt64(int rowIndex, int fieldOffset, long value)
        {
            int at = FieldPosition(rowIndex, fieldOffset, 8);
            WriteInt64(m_Current.RowBytes, at, value);
        }

        /// <summary>写 64 位无符号整数。</summary>
        public void SetUInt64(int rowIndex, int fieldOffset, ulong value)
        {
            SetInt64(rowIndex, fieldOffset, (long)value);
        }

        /// <summary>写单精度浮点。</summary>
        public void SetSingle(int rowIndex, int fieldOffset, float value)
        {
            SetInt32(rowIndex, fieldOffset, BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>写双精度浮点。</summary>
        public void SetDouble(int rowIndex, int fieldOffset, double value)
        {
            SetInt64(rowIndex, fieldOffset, BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>
        /// 写字符串字段（占 4 字节槽）。null 与空串都写 0 偏移，不占用字符串池；
        /// 内容相同的字符串在整个 blob 内只存一份。
        /// </summary>
        /// <param name="rowIndex">行号。</param>
        /// <param name="fieldOffset">行内字段偏移。</param>
        /// <param name="value">字符串内容。</param>
        public void SetString(int rowIndex, int fieldOffset, string value)
        {
            int at = FieldPosition(rowIndex, fieldOffset, 4);
            long key = FixupKey(rowIndex, fieldOffset);
            if (string.IsNullOrEmpty(value))
            {
                // 必须同时撤销此前登记的回填，否则 Build 阶段旧记录会把这里刚写下的 0 覆盖回去。
                m_Current.StringFixups.Remove(key);
                WriteInt32(m_Current.RowBytes, at, 0);
                return;
            }

            m_Current.StringFixups[key] = InternString(value);
        }

        private int InternString(string value)
        {
            int id;
            if (m_StringIds.TryGetValue(value, out id))
            {
                return id;
            }

            id = m_StringBytes.Count;
            m_StringIds.Add(value, id);
            m_StringBytes.Add(Encoding.UTF8.GetBytes(value));
            return id;
        }

        // ================================================================
        //  产出
        // ================================================================

        /// <summary>
        /// 完成构建，产出完整 blob 字节。只能调用一次。
        /// </summary>
        /// <returns>可直接落盘的 blob 字节。</returns>
        public byte[] Build()
        {
            CheckNotBuilt();
            if (m_Current != null)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter.Build：表 \"{0}\" 尚未 EndTable。", m_Current.Name));
            }

            m_Built = true;

            // 表目录必须按 nameHash 升序，读侧才能二分。
            m_Tables.Sort((a, b) => a.NameHash.CompareTo(b.NameHash));

            // 第一遍：排定各表位置（此时还不需要知道字符串池在哪）。
            int cursor = Align(ConfigBlob.HeaderSize + m_Tables.Count * ConfigBlob.TableDirectoryEntrySize, TableAlignment);
            for (int i = 0; i < m_Tables.Count; i++)
            {
                TableBuilder table = m_Tables[i];
                table.TableOffset = cursor;
                table.TableSize = ConfigBlob.TableHeaderSize + table.RowCount * 8 + table.RowCount * table.RowSize;
                cursor = Align(cursor + table.TableSize, TableAlignment);
            }

            // 第二遍：表区之后即字符串池，逐条排定池内偏移。
            int poolStart = Align(cursor, StringAlignment);
            int[] stringOffsets = new int[m_StringBytes.Count];
            int poolCursor = poolStart;
            for (int i = 0; i < m_StringBytes.Count; i++)
            {
                stringOffsets[i] = poolCursor;
                poolCursor = Align(poolCursor + 4 + m_StringBytes[i].Length, StringAlignment);
            }

            byte[] blob = new byte[poolCursor];

            // 文件头。
            WriteInt32(blob, 0, unchecked((int)ConfigBlob.Magic));
            WriteInt32(blob, 4, ConfigBlob.FormatVersion);
            WriteInt64(blob, 8, unchecked((long)m_SchemaHash));
            WriteInt32(blob, 16, m_Tables.Count);
            // 20..31 为 reserved，保持 0。

            // 表目录。
            for (int i = 0; i < m_Tables.Count; i++)
            {
                TableBuilder table = m_Tables[i];
                int entry = ConfigBlob.HeaderSize + i * ConfigBlob.TableDirectoryEntrySize;
                WriteInt64(blob, entry, unchecked((long)table.NameHash));
                WriteInt32(blob, entry + 8, table.TableOffset);
                WriteInt32(blob, entry + 12, table.TableSize);
            }

            // 各表。
            for (int i = 0; i < m_Tables.Count; i++)
            {
                WriteTable(blob, m_Tables[i], stringOffsets);
            }

            // 字符串池。
            for (int i = 0; i < m_StringBytes.Count; i++)
            {
                byte[] bytes = m_StringBytes[i];
                int at = stringOffsets[i];
                WriteInt32(blob, at, bytes.Length);
                Buffer.BlockCopy(bytes, 0, blob, at + 4, bytes.Length);
            }

            return blob;
        }

        private static void WriteTable(byte[] blob, TableBuilder table, int[] stringOffsets)
        {
            int pkIndexRelative = ConfigBlob.TableHeaderSize;
            int rowsRelative = pkIndexRelative + table.RowCount * 8;

            WriteInt32(blob, table.TableOffset, table.RowCount);
            WriteInt32(blob, table.TableOffset + 4, table.RowSize);
            WriteInt32(blob, table.TableOffset + 8, pkIndexRelative);
            WriteInt32(blob, table.TableOffset + 12, rowsRelative);

            int rowsAbsolute = table.TableOffset + rowsRelative;

            // 字符串字段回填：此时池内偏移已确定，直接写进行缓冲，随后整块拷贝。
            foreach (KeyValuePair<long, int> fixup in table.StringFixups)
            {
                int rowIndex = (int)(fixup.Key >> 32);
                int fieldOffset = (int)(fixup.Key & 0xFFFFFFFFL);
                WriteInt32(table.RowBytes, rowIndex * table.RowSize + fieldOffset, stringOffsets[fixup.Value]);
            }

            if (table.RowCount > 0)
            {
                Buffer.BlockCopy(table.RowBytes, 0, blob, rowsAbsolute, table.RowCount * table.RowSize);
            }

            // 主键索引：按主键升序排序，值为对应的原始行号。行数据本身保持插入顺序不动，
            // 这样导表产出的行顺序（通常也是策划表里的顺序）可用于稳定遍历。
            int[] order = new int[table.RowCount];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            List<int> keys = table.PrimaryKeys;
            Array.Sort(order, (a, b) => keys[a].CompareTo(keys[b]));

            int pkAbsolute = table.TableOffset + pkIndexRelative;
            for (int i = 0; i < order.Length; i++)
            {
                WriteInt32(blob, pkAbsolute + i * 4, keys[order[i]]);
                WriteInt32(blob, pkAbsolute + table.RowCount * 4 + i * 4, order[i]);
            }
        }

        // ================================================================
        //  内部
        // ================================================================

        private TableBuilder RequireCurrent()
        {
            CheckNotBuilt();
            if (m_Current == null)
            {
                throw new FrameworkException("ConfigBlobWriter：当前没有正在收集的表，请先调用 BeginTable。");
            }

            return m_Current;
        }

        private int FieldPosition(int rowIndex, int fieldOffset, int size)
        {
            TableBuilder table = RequireCurrent();
            if (rowIndex < 0 || rowIndex >= table.RowCount)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter：表 \"{0}\" 行号 {1} 越界，当前行数 {2}。", table.Name, rowIndex, table.RowCount));
            }

            if (fieldOffset < 0 || fieldOffset + size > table.RowSize)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlobWriter：表 \"{0}\" 字段越界，fieldOffset={1}，字段大小={2}，rowSize={3}。", table.Name, fieldOffset, size, table.RowSize));
            }

            return rowIndex * table.RowSize + fieldOffset;
        }

        private void CheckNotBuilt()
        {
            if (m_Built)
            {
                throw new FrameworkException("ConfigBlobWriter：已经 Build 过，实例不可复用。");
            }
        }

        private static int Align(int value, int alignment)
        {
            int remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt64(byte[] buffer, int offset, long value)
        {
            WriteInt32(buffer, offset, (int)value);
            WriteInt32(buffer, offset + 4, (int)(value >> 32));
        }
    }
}
