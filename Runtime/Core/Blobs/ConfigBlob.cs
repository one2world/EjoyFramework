//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Threading;

namespace EjoyFramework.Core.Blobs
{
    /// <summary>
    /// 零解析二进制配置容器的运行期访问器。加载 = 拿到一段只读内存并校验文件头，之后所有读取都是
    /// "按偏移取值"，不解析、不建对象、不分配。
    ///
    /// 二进制格式（小端，全部 4 字节对齐）：
    /// <code>
    /// Header(32B)  : magic 'EJCB'(4B) | formatVersion int | schemaHash ulong | tableCount int | reserved(12B)
    /// TableDir[N]  : { nameHash ulong | tableOffset int | tableSize int }  每项 16B，按 nameHash 升序
    /// Table        : { rowCount int | rowSize int | pkIndexOffset int | rowsOffset int } 表头 16B，
    ///                两个偏移相对表头起点
    ///                PK 索引区：升序 int 主键数组[rowCount] + int 行号数组[rowCount]
    ///                行数据区：rowSize 定长记录连续排列，行内变长字段存"相对 blob 起点的 int 绝对偏移"，0 表示空
    /// StringPool   : 位于文件尾部，每串 { int utf8ByteLen | utf8 bytes }，4 字节对齐，构建期全局去重
    /// </code>
    ///
    /// 设计理由：
    ///   • 相对偏移而非指针，使整块数据可以原样落盘、原样映射，加载路径没有"修复指针"这一步。
    ///   • 表目录按 nameHash 升序，查表是二分而非线性；表名不落字符串，避免运行期字符串比较。
    ///   • 主键索引与行数据分离，二分只在紧凑的 int 数组上进行，缓存友好。
    ///   • 只读不可变：本类不提供任何写入接口，数据在整个生命周期内不变，因此天然线程安全。
    ///
    /// 关于边界检查：所有读原语的边界检查<b>常编译</b>，Release 下同样生效。越界读的是进程内任意内存，
    /// 属于内存安全问题而非逻辑断言，一次损坏的数据文件或一处 codegen 偏移错误就可能变成随机崩溃或
    /// 信息泄露；相较之下一次比较的开销可以忽略。若将来确有热点，应在 profile 之后针对具体调用点优化，
    /// 而不是全局关掉检查。
    ///
    /// 线程契约：
    ///   • <see cref="Open"/>/<see cref="Retain"/>/<see cref="Release"/> 在主线程调用。
    ///   • 打开之后到引用计数归零之前，所有读取接口（含 <see cref="ConfigTableView"/>、
    ///     <see cref="BlobStringHandle"/>）可被任意线程并发调用。
    /// </summary>
    public sealed unsafe class ConfigBlob
    {
        /// <summary>文件魔数 'E','J','C','B' 的小端 uint 形式。</summary>
        public const uint Magic = 0x42434A45;

        /// <summary>当前支持的格式版本。</summary>
        public const int FormatVersion = 1;

        /// <summary>文件头长度。</summary>
        public const int HeaderSize = 32;

        /// <summary>表目录每项长度。</summary>
        public const int TableDirectoryEntrySize = 16;

        /// <summary>单张表表头长度。</summary>
        public const int TableHeaderSize = 16;

        /// <summary>
        /// 底层字节来源。<b>这个强引用是防止来源被 GC 提前回收的结构保证，不得移除或改成弱引用。</b>
        /// <see cref="NativeAllocBlobSource"/> 带终结器，一旦它变得不可达，GC 就可能在本对象仍在使用
        /// <see cref="m_Pointer"/> 的情况下终结它并 FreeHGlobal —— 那是典型的 use-after-free，
        /// 且因为读的是已归还（可能已被别的分配复用）的内存，症状是随机错值而不是崩溃，极难定位。
        /// </summary>
        private IConfigBlobSource m_Source;
        private byte* m_Pointer;
        private int m_Length;
        private ulong m_SchemaHash;
        private int m_TableCount;
        private int m_RefCount;

        private ConfigBlob(IConfigBlobSource source)
        {
            m_Source = source;
            m_Pointer = source.Pointer;
            m_Length = source.Length;
            m_RefCount = 1;
        }

        // ================================================================
        //  生命周期
        // ================================================================

        /// <summary>
        /// 校验并打开一个 blob。校验失败会 Dispose <paramref name="source"/> 并抛出 <see cref="FrameworkException"/>。
        /// 返回的实例引用计数为 1。
        /// </summary>
        /// <param name="source">字节来源，所有权转移给返回的 ConfigBlob。</param>
        /// <param name="expectedSchemaHash">生成代码侧编译进去的 schemaHash，用于确认代码与数据同源。</param>
        /// <returns>可用的 ConfigBlob。</returns>
        public static ConfigBlob Open(IConfigBlobSource source, ulong expectedSchemaHash)
        {
            if (source == null)
            {
                throw new FrameworkException("ConfigBlob.Open：source 不能为 null。");
            }

            ConfigBlob blob = new ConfigBlob(source);
            try
            {
                blob.ValidateHeader(expectedSchemaHash);
                return blob;
            }
            catch
            {
                source.Dispose();
                blob.m_Source = null;
                blob.m_Pointer = null;
                blob.m_Length = 0;
                blob.m_RefCount = 0;
                throw;
            }
        }

        private void ValidateHeader(ulong expectedSchemaHash)
        {
            if (m_Pointer == null)
            {
                throw new FrameworkException("ConfigBlob.Open：来源返回了空指针。");
            }

            if (m_Length < HeaderSize)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.Open：数据长度 {0} 不足以容纳文件头（{1} 字节），文件已损坏或被截断。", m_Length, HeaderSize));
            }

            uint magic = (uint)ReadInt32Unchecked(0);
            if (magic != Magic)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.Open：魔数不匹配（期望 0x{0:X8}，实际 0x{1:X8}），这不是一个 ConfigBlob 文件。", Magic, magic));
            }

            int version = ReadInt32Unchecked(4);
            if (version != FormatVersion)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.Open：格式版本不匹配（期望 {0}，实际 {1}），请用当前版本的工具重新构建配置数据。", FormatVersion, version));
            }

            m_SchemaHash = (ulong)ReadInt64Unchecked(8);
            if (m_SchemaHash != expectedSchemaHash)
            {
                throw new FrameworkException(Utility.Text.Format(
                    "ConfigBlob.Open：schemaHash 不一致（生成代码 0x{0:X16}，数据文件 0x{1:X16}）——生成代码与数据版本不一致，请重新构建配置（先跑配置导表/代码生成，再出包）。",
                    expectedSchemaHash, m_SchemaHash));
            }

            m_TableCount = ReadInt32Unchecked(16);
            if (m_TableCount < 0)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.Open：表数量非法（{0}），文件已损坏。", m_TableCount));
            }

            long directoryEnd = (long)HeaderSize + (long)m_TableCount * TableDirectoryEntrySize;
            if (directoryEnd > m_Length)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.Open：表目录越界（需要 {0} 字节，实际 {1} 字节），文件已损坏或被截断。", directoryEnd, m_Length));
            }
        }

        /// <summary>
        /// 增加一次引用。用于同一份配置被多个模块共享时避免任一方提前释放。
        /// </summary>
        public void Retain()
        {
            // 用 CAS 循环而不是 Interlocked.Increment：需要在"已归零"的状态上拒绝复活，
            // 而 Increment 会先把 0 变成 1 再检查，那一瞬间别的线程可能误以为对象还活着。
            while (true)
            {
                int current = Volatile.Read(ref m_RefCount);
                if (current <= 0)
                {
                    throw new FrameworkException("ConfigBlob.Retain：对象已释放，不能再次 Retain。");
                }

                if (Interlocked.CompareExchange(ref m_RefCount, current + 1, current) == current)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 减少一次引用，归零时释放底层来源。释放后任何读取都会抛异常。
        /// </summary>
        /// <returns>归零并真正释放返回 true。</returns>
        public bool Release()
        {
            // 引用计数必须是原子的：类头承诺"打开之后可任意线程并发读"，而 Release 归零会释放底层内存。
            // 裸的 -- 在两个线程同时释放时可能都读到 1、都判定自己不是最后一个（内存永不释放），
            // 或都判定是（double free + 其他线程 use-after-free）。
            int remaining = Interlocked.Decrement(ref m_RefCount);
            if (remaining > 0)
            {
                return false;
            }

            if (remaining < 0)
            {
                // 拨回去，避免重复的错误调用把计数越推越负而掩盖首次问题。
                Interlocked.Increment(ref m_RefCount);
                throw new FrameworkException("ConfigBlob.Release：引用计数已为 0，存在重复释放。");
            }

            if (m_Source != null)
            {
                m_Source.Dispose();
                m_Source = null;
            }

            m_Pointer = null;
            m_Length = 0;
            m_TableCount = 0;
            return true;
        }

        /// <summary>当前引用计数。</summary>
        public int ReferenceCount
        {
            get { return Volatile.Read(ref m_RefCount); }
        }

        /// <summary>数据总字节数。</summary>
        public int Length
        {
            get { return m_Length; }
        }

        /// <summary>数据文件中的 schemaHash。</summary>
        public ulong SchemaHash
        {
            get { return m_SchemaHash; }
        }

        /// <summary>表数量。</summary>
        public int TableCount
        {
            get { return m_TableCount; }
        }

        // ================================================================
        //  表查找
        // ================================================================

        /// <summary>
        /// 按表名哈希查表。表目录按哈希升序排列，此处为二分查找。
        /// </summary>
        /// <param name="nameHash">表名的 FNV1a-64 哈希，见 <see cref="ConfigBlobHash.Compute(string)"/>。</param>
        /// <param name="table">输出：表视图。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetTable(ulong nameHash, out ConfigTableView table)
        {
            CheckAlive();

            int low = 0;
            int high = m_TableCount - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int entry = HeaderSize + mid * TableDirectoryEntrySize;
                ulong hash = (ulong)ReadInt64Unchecked(entry);
                if (hash == nameHash)
                {
                    int tableOffset = ReadInt32Unchecked(entry + 8);
                    int tableSize = ReadInt32Unchecked(entry + 12);
                    if (tableOffset < 0 || tableSize < 0 || (long)tableOffset + tableSize > m_Length)
                    {
                        throw new FrameworkException(Utility.Text.Format("ConfigBlob：表目录项越界（tableOffset={0}，tableSize={1}，总长度={2}），文件已损坏。", tableOffset, tableSize, m_Length));
                    }

                    table = new ConfigTableView(this, tableOffset, tableSize);
                    return true;
                }

                if (hash < nameHash)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            table = default(ConfigTableView);
            return false;
        }

        /// <summary>
        /// 按表名查表（内部先算哈希）。热路径应缓存哈希后调用
        /// <see cref="TryGetTable(ulong, out ConfigTableView)"/>。
        /// </summary>
        /// <param name="name">表名。</param>
        /// <param name="table">输出：表视图。</param>
        /// <returns>找到返回 true。</returns>
        public bool TryGetTable(string name, out ConfigTableView table)
        {
            return TryGetTable(ConfigBlobHash.Compute(name), out table);
        }

        /// <summary>
        /// 按顺序取第 <paramref name="index"/> 张表的名字哈希，用于调试与工具。
        /// </summary>
        /// <param name="index">表序号，[0, TableCount)。</param>
        /// <returns>表名哈希。</returns>
        public ulong GetTableNameHash(int index)
        {
            CheckAlive();
            if (index < 0 || index >= m_TableCount)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob.GetTableNameHash：表序号 {0} 越界，表数量为 {1}。", index, m_TableCount));
            }

            return (ulong)ReadInt64Unchecked(HeaderSize + index * TableDirectoryEntrySize);
        }

        // ================================================================
        //  读原语（边界检查常编译，见类头说明）
        // ================================================================

        /// <summary>读 1 字节。</summary>
        public byte ReadByte(int offset)
        {
            CheckRange(offset, 1);
            return m_Pointer[offset];
        }

        /// <summary>读有符号 1 字节。</summary>
        public sbyte ReadSByte(int offset)
        {
            CheckRange(offset, 1);
            return (sbyte)m_Pointer[offset];
        }

        /// <summary>读 1 字节布尔（0 为 false）。</summary>
        public bool ReadBoolean(int offset)
        {
            CheckRange(offset, 1);
            return m_Pointer[offset] != 0;
        }

        /// <summary>读 16 位有符号整数。</summary>
        public short ReadInt16(int offset)
        {
            CheckRange(offset, 2);
            return (short)ReadUInt16Unchecked(offset);
        }

        /// <summary>读 16 位无符号整数。</summary>
        public ushort ReadUInt16(int offset)
        {
            CheckRange(offset, 2);
            return ReadUInt16Unchecked(offset);
        }

        /// <summary>读 32 位有符号整数。</summary>
        public int ReadInt32(int offset)
        {
            CheckRange(offset, 4);
            return ReadInt32Unchecked(offset);
        }

        /// <summary>读 32 位无符号整数。</summary>
        public uint ReadUInt32(int offset)
        {
            CheckRange(offset, 4);
            return (uint)ReadInt32Unchecked(offset);
        }

        /// <summary>读 64 位有符号整数。</summary>
        public long ReadInt64(int offset)
        {
            CheckRange(offset, 8);
            return ReadInt64Unchecked(offset);
        }

        /// <summary>读 64 位无符号整数。</summary>
        public ulong ReadUInt64(int offset)
        {
            CheckRange(offset, 8);
            return (ulong)ReadInt64Unchecked(offset);
        }

        /// <summary>读单精度浮点。</summary>
        public float ReadSingle(int offset)
        {
            CheckRange(offset, 4);
            return BitConverter.Int32BitsToSingle(ReadInt32Unchecked(offset));
        }

        /// <summary>读双精度浮点。</summary>
        public double ReadDouble(int offset)
        {
            CheckRange(offset, 8);
            return BitConverter.Int64BitsToDouble(ReadInt64Unchecked(offset));
        }

        /// <summary>
        /// 读一个字符串字段：字段槽内是相对 blob 起点的绝对偏移，0 表示空串。
        /// </summary>
        /// <param name="offset">字符串字段槽（4 字节）在 blob 中的绝对偏移。</param>
        /// <returns>字符串句柄，零分配。</returns>
        public BlobStringHandle ReadString(int offset)
        {
            int poolOffset = ReadInt32(offset);
            return new BlobStringHandle(this, poolOffset);
        }

        /// <summary>
        /// 取 blob 内一段字节的只读视图。用于变长数据（字符串 UTF-8 字节、数组等）的零分配访问。
        ///
        /// <b>危险：返回的 Span 直接指向底层非托管内存，不持有任何引用，因此不会阻止本对象被释放。</b>
        /// 一旦引用计数归零（<see cref="Release"/>）、或底层来源被 Dispose，该 Span 立即变成悬垂指针，
        /// 继续读取是 use-after-free —— 不会抛异常，只会读到已归还甚至已被别的分配复用的内存。
        /// 因此 Span 只能在当前调用栈内即用即弃，绝不可存进字段、闭包、集合，或跨越任何可能触发
        /// Release 的调用。需要跨生命周期保存请改用 <see cref="BlobStringHandle.ToString"/> 拷贝一份。
        ///
        /// 还有一条更隐蔽的要求：<b>使用该 Span 期间必须保证本 ConfigBlob 实例本身可达</b>。
        /// Span 只是裸指针，不持有任何托管引用，因此它无法让本对象保持存活；若调用方在读 Span 的过程中
        /// 不再引用本对象（例如只把 Span 传给了下游），GC 就可以回收本对象，进而终结底层来源、释放内存，
        /// 于是 Span 当场悬垂。把本对象一直握在局部变量里直到用完 Span 是最简单的保证。
        /// </summary>
        /// <param name="offset">起始绝对偏移。</param>
        /// <param name="length">字节数。</param>
        /// <returns>只读字节视图，生命周期严格短于本对象。</returns>
        public ReadOnlySpan<byte> Slice(int offset, int length)
        {
            CheckRange(offset, length);
            return new ReadOnlySpan<byte>(m_Pointer + offset, length);
        }

        // ================================================================
        //  内部
        // ================================================================

        /// <summary>
        /// 边界检查。常编译，理由见类头。校验 offset/length 非负、不溢出、且落在数据范围内，
        /// 同时拦住"对象已释放后继续读"。
        /// </summary>
        internal void CheckRange(int offset, int length)
        {
            if (m_Pointer == null)
            {
                throw new FrameworkException("ConfigBlob：对象已释放（引用计数归零），不能再读取数据。");
            }

            if (offset < 0 || length < 0 || (long)offset + length > m_Length)
            {
                throw new FrameworkException(Utility.Text.Format("ConfigBlob：读取越界，offset={0}，length={1}，数据总长度={2}。可能是数据文件损坏或生成代码的字段偏移与数据不匹配。", offset, length, m_Length));
            }
        }

        private void CheckAlive()
        {
            if (m_Pointer == null)
            {
                throw new FrameworkException("ConfigBlob：对象已释放（引用计数归零），不能再访问。");
            }
        }

        // 以下 Unchecked 系列只在已经确认过范围的位置使用（文件头校验、二分查表、以及公开原语的检查之后）。
        // 一律用位移逐字节组装而不是 *(int*)p：blob 内 8 字节字段虽由 codegen 保证 8 字节对齐，
        // 但一旦某处偏移计算失误，非对齐解引用在部分 ARM 设备上是硬件异常/静默错值，位移组装则与对齐无关，
        // 且在实测中被 JIT/AOT 优化得足够好。

        private ushort ReadUInt16Unchecked(int offset)
        {
            byte* p = m_Pointer + offset;
            return (ushort)(p[0] | (p[1] << 8));
        }

        private int ReadInt32Unchecked(int offset)
        {
            byte* p = m_Pointer + offset;
            return p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24);
        }

        private long ReadInt64Unchecked(int offset)
        {
            byte* p = m_Pointer + offset;
            uint low = (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
            uint high = (uint)(p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24));
            return (long)(((ulong)high << 32) | low);
        }
    }
}
