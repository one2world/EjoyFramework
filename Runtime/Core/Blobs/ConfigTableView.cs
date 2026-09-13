//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// blob 中单张表的只读视图。本身是 <c>readonly struct</c>，只持有 blob 引用与表头偏移，
    /// 取表、遍历、按主键查找的整条链路都不产生任何分配。
    ///
    /// 表内布局（偏移相对表头起点）：
    /// <code>
    /// 0  : rowCount int
    /// 4  : rowSize int
    /// 8  : pkIndexOffset int   → 升序 int 主键数组[rowCount] 紧跟 int 行号数组[rowCount]
    /// 12 : rowsOffset int      → rowCount 条定长记录
    /// </code>
    ///
    /// 设计理由：
    ///   • 主键与行号分成两个平坦 int 数组，而不是 {pk,row} 交错：二分只触碰主键数组，
    ///     同一条缓存行能装下更多候选键，查找的 cache miss 明显更少。
    ///   • 查找返回的是"行在 blob 中的绝对偏移"而不是行对象。生成代码据此直接按字段偏移读值，
    ///     整条路径不产生中间对象——这是零分配的关键，也是本视图不提供任何"取行结构体"接口的原因。
    ///
    /// 线程契约：只读，可任意线程并发使用；不得在所属 <see cref="ConfigBlob"/> 释放之后继续使用。
    /// </summary>
    public readonly struct ConfigTableView
    {
        private readonly ConfigBlob m_Blob;
        private readonly int m_TableOffset;
        private readonly int m_RowCount;
        private readonly int m_RowSize;
        private readonly int m_PkIndexOffset;
        private readonly int m_RowsOffset;

        internal ConfigTableView(ConfigBlob blob, int tableOffset, int tableSize)
        {
            m_Blob = blob;
            m_TableOffset = tableOffset;

            m_RowCount = blob.ReadInt32(tableOffset);
            m_RowSize = blob.ReadInt32(tableOffset + 4);
            int pkIndexRelative = blob.ReadInt32(tableOffset + 8);
            int rowsRelative = blob.ReadInt32(tableOffset + 12);

            if (m_RowCount < 0 || m_RowSize <= 0)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigTableView：表头非法（rowCount={0}，rowSize={1}），文件已损坏，请重新构建配置数据。", m_RowCount, m_RowSize));
            }

            if (tableSize < ConfigBlob.TableHeaderSize)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigTableView：表长度非法（tableSize={0}，小于表头 {1} 字节），文件已损坏，请重新构建配置数据。", tableSize, ConfigBlob.TableHeaderSize));
            }

            // 两个相对偏移必须越过表头、且各自的区域完整落在本表声明的 tableSize 之内。
            // 全程用 long 运算：损坏文件里的 rowCount/rowSize 可以是 int.MaxValue，
            // 乘法在 int 上会回绕成小的甚至负的值，从而"通过"任何用 int 写的检查。
            if (pkIndexRelative < ConfigBlob.TableHeaderSize || rowsRelative < ConfigBlob.TableHeaderSize)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigTableView：区域偏移非法（pkIndexOffset={0}，rowsOffset={1}，均须不小于表头 {2}），文件已损坏，请重新构建配置数据。", pkIndexRelative, rowsRelative, ConfigBlob.TableHeaderSize));
            }

            long pkIndexBytes = (long)m_RowCount * 8L;
            long rowBytes = (long)m_RowCount * (long)m_RowSize;
            if (pkIndexRelative + pkIndexBytes > tableSize || rowsRelative + rowBytes > tableSize)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigTableView：表内区域越界（rowCount={0}，rowSize={1}，pkIndexOffset={2}，rowsOffset={3}，tableSize={4}），文件已损坏，请重新构建配置数据。",
                    m_RowCount, m_RowSize, pkIndexRelative, rowsRelative, tableSize));
            }

            m_PkIndexOffset = tableOffset + pkIndexRelative;
            m_RowsOffset = tableOffset + rowsRelative;

            // 表区域已确认落在 tableSize 内，而 tableSize 由 ConfigBlob 校验过落在 blob 内，
            // 此处再验一次是纵深防御；两处长度均已确认不会溢出 int。
            blob.CheckRange(m_PkIndexOffset, (int)pkIndexBytes);
            blob.CheckRange(m_RowsOffset, (int)rowBytes);
        }

        /// <summary>该视图是否有效（默认构造的 struct 为无效）。</summary>
        public bool IsValid
        {
            get { return m_Blob != null; }
        }

        /// <summary>所属 blob。</summary>
        public ConfigBlob Blob
        {
            get { return m_Blob; }
        }

        /// <summary>行数。</summary>
        public int RowCount
        {
            get { return m_RowCount; }
        }

        /// <summary>单行字节数。</summary>
        public int RowSize
        {
            get { return m_RowSize; }
        }

        /// <summary>表头在 blob 中的绝对偏移。</summary>
        public int TableOffset
        {
            get { return m_TableOffset; }
        }

        /// <summary>
        /// 按主键查找，返回该行在 blob 中的绝对偏移；未找到返回 -1。
        /// </summary>
        /// <param name="primaryKey">主键。</param>
        /// <returns>行的绝对偏移，或 -1。</returns>
        public int FindRowOffset(int primaryKey)
        {
            int rowIndex = FindRowIndex(primaryKey);
            return rowIndex >= 0 ? m_RowsOffset + rowIndex * m_RowSize : -1;
        }

        /// <summary>
        /// 按主键查找，返回行号（存储顺序下标）；未找到返回 -1。
        /// </summary>
        /// <param name="primaryKey">主键。</param>
        /// <returns>行号，或 -1。</returns>
        public int FindRowIndex(int primaryKey)
        {
            CheckValid();

            int low = 0;
            int high = m_RowCount - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int key = m_Blob.ReadInt32(m_PkIndexOffset + mid * 4);
                if (key == primaryKey)
                {
                    // 主键数组之后紧跟等长的行号数组，同一个下标 mid 对应同一条记录。
                    int rowIndex = m_Blob.ReadInt32(m_PkIndexOffset + m_RowCount * 4 + mid * 4);
                    if (rowIndex < 0 || rowIndex >= m_RowCount)
                    {
                        // 索引区被篡改/损坏时，这个值会被直接乘进行偏移。不校验的话越界读要等到
                        // ConfigBlob.CheckRange 才可能被发现，而只要 rowIndex 落在别的合法区域内就永远发现不了，
                        // 会静默读到错误的行。
                        throw new FrameworkException(Utility.Text.Format("ConfigTableView：主键索引损坏（主键 {0} 指向非法行号 {1}，行数 {2}），请重新构建配置数据。", primaryKey, rowIndex, m_RowCount));
                    }

                    return rowIndex;
                }

                if (key < primaryKey)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return -1;
        }

        /// <summary>
        /// 是否存在该主键。
        /// </summary>
        /// <param name="primaryKey">主键。</param>
        /// <returns>存在返回 true。</returns>
        public bool ContainsKey(int primaryKey)
        {
            return FindRowIndex(primaryKey) >= 0;
        }

        /// <summary>
        /// 按行号取行的绝对偏移。
        /// </summary>
        /// <param name="rowIndex">行号，[0, RowCount)。</param>
        /// <returns>行的绝对偏移。</returns>
        public int GetRowOffsetByIndex(int rowIndex)
        {
            CheckValid();
            if (rowIndex < 0 || rowIndex >= m_RowCount)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigTableView.GetRowOffsetByIndex：行号 {0} 越界，行数为 {1}。", rowIndex, m_RowCount));
            }

            return m_RowsOffset + rowIndex * m_RowSize;
        }

        /// <summary>
        /// 按索引下标取主键（索引区按主键升序，可用于有序遍历主键）。
        /// </summary>
        /// <param name="indexPosition">索引位置，[0, RowCount)。</param>
        /// <returns>主键。</returns>
        public int GetPrimaryKeyAt(int indexPosition)
        {
            CheckValid();
            if (indexPosition < 0 || indexPosition >= m_RowCount)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigTableView.GetPrimaryKeyAt：索引位置 {0} 越界，行数为 {1}。", indexPosition, m_RowCount));
            }

            return m_Blob.ReadInt32(m_PkIndexOffset + indexPosition * 4);
        }

        /// <summary>
        /// 按存储顺序遍历所有行的绝对偏移，零分配。
        /// </summary>
        /// <returns>结构体枚举器。</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        private void CheckValid()
        {
            if (m_Blob == null)
            {
                throw new FrameworkException("ConfigTableView：视图无效（未经 ConfigBlob.TryGetTable 获取）。");
            }
        }

        /// <summary>
        /// 行偏移枚举器。刻意实现为 struct 且不实现 IEnumerator&lt;T&gt;，
        /// 让 foreach 走编译器的模式匹配路径，避免装箱与迭代器对象分配。
        /// </summary>
        public struct Enumerator
        {
            private readonly ConfigTableView m_Table;
            private int m_Index;

            internal Enumerator(ConfigTableView table)
            {
                m_Table = table;
                m_Index = -1;
            }

            /// <summary>当前行在 blob 中的绝对偏移。</summary>
            public int Current
            {
                get { return m_Table.GetRowOffsetByIndex(m_Index); }
            }

            /// <summary>前进一行。</summary>
            /// <returns>还有行返回 true。</returns>
            public bool MoveNext()
            {
                if (m_Index + 1 >= m_Table.RowCount)
                {
                    return false;
                }

                m_Index++;
                return true;
            }

            /// <summary>重置到起点。</summary>
            public void Reset()
            {
                m_Index = -1;
            }
        }
    }
}
