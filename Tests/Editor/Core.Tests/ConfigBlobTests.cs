//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.Blobs;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// 验证零解析二进制配置容器 ConfigBlob：
    /// 写入-读回一致性、文件头校验（魔数/版本/schemaHash）、表查找、主键二分、行遍历、
    /// 字符串池去重与解码、边界检查、以及 zip 条目定位与 mmap 来源。
    /// </summary>
    public class ConfigBlobTests
    {
        private const ulong TestSchemaHash = 0x0123456789ABCDEFUL;

        private static readonly ulong MonsterTableHash = ConfigBlobHash.Compute("Monsters");
        private static readonly ulong StageTableHash = ConfigBlobHash.Compute("Stages");

        // Monsters 行布局：0 int Id | 4 float Speed | 8 int Name(字符串槽) | 12 int Icon(字符串槽)
        private const int MonsterRowSize = 16;

        // Stages 行布局：0 long Score | 8 int Id | 12 int Desc(字符串槽) | 16 short Level | 18 byte Flag | 19 padding
        private const int StageRowSize = 20;

        /// <summary>
        /// 构建一份测试用 blob：两张表，含重复字符串（"火"在两表出现）、空串与 null 串。
        /// </summary>
        private static byte[] BuildSampleBlob()
        {
            ConfigBlobWriter writer = new ConfigBlobWriter(TestSchemaHash);

            // 故意以"非升序表名哈希"的顺序 BeginTable，验证 Build 会自行排序表目录。
            writer.BeginTable("Stages", StageRowSize);
            {
                int r0 = writer.AddRow(500);
                writer.SetInt64(r0, 0, 9000000000L);
                writer.SetInt32(r0, 8, 500);
                writer.SetString(r0, 12, "第一关");
                writer.SetInt16(r0, 16, -3);
                writer.SetBoolean(r0, 18, true);

                int r1 = writer.AddRow(100);
                writer.SetInt64(r1, 0, -1L);
                writer.SetInt32(r1, 8, 100);
                writer.SetString(r1, 12, "火");
                writer.SetInt16(r1, 16, short.MaxValue);
                writer.SetBoolean(r1, 18, false);
            }
            writer.EndTable();

            writer.BeginTable("Monsters", MonsterRowSize);
            {
                int r0 = writer.AddRow(1001);
                writer.SetInt32(r0, 0, 1001);
                writer.SetSingle(r0, 4, 12.5f);
                writer.SetString(r0, 8, "火");         // 与 Stages 中的 "火" 应共享同一份池数据
                writer.SetString(r0, 12, string.Empty); // 空串 → 0 偏移

                int r1 = writer.AddRow(1003);
                writer.SetInt32(r1, 0, 1003);
                writer.SetSingle(r1, 4, -0.25f);
                writer.SetString(r1, 8, "水");
                writer.SetString(r1, 12, null);         // null → 0 偏移

                int r2 = writer.AddRow(1002);
                writer.SetInt32(r2, 0, 1002);
                writer.SetSingle(r2, 4, 0f);
                writer.SetString(r2, 8, "火");
                writer.SetString(r2, 12, "icon_fire");
            }
            writer.EndTable();

            return writer.Build();
        }

        private static ConfigBlob OpenSample()
        {
            return ConfigBlob.Open(new NativeAllocBlobSource(BuildSampleBlob()), TestSchemaHash);
        }

        // ================================================================
        //  文件头校验
        // ================================================================

        [Test]
        public void Open_ValidBlob_Succeeds()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                Assert.AreEqual(2, blob.TableCount);
                Assert.AreEqual(TestSchemaHash, blob.SchemaHash);
                Assert.AreEqual(1, blob.ReferenceCount);
                Assert.Greater(blob.Length, ConfigBlob.HeaderSize);
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void Open_TableDirectoryIsSortedByNameHash()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ulong previous = 0UL;
                for (int i = 0; i < blob.TableCount; i++)
                {
                    ulong hash = blob.GetTableNameHash(i);
                    Assert.Less(previous, hash, "表目录必须按 nameHash 升序，否则读侧二分会失效。");
                    previous = hash;
                }
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void Open_BadMagic_Throws()
        {
            byte[] bytes = BuildSampleBlob();
            bytes[0] = 0x00;
            Assert.Throws<FrameworkException>(() => ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash));
        }

        [Test]
        public void Open_BadFormatVersion_Throws()
        {
            byte[] bytes = BuildSampleBlob();
            bytes[4] = 99;
            Assert.Throws<FrameworkException>(() => ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash));
        }

        [Test]
        public void Open_SchemaHashMismatch_Throws()
        {
            byte[] bytes = BuildSampleBlob();
            FrameworkException e = Assert.Throws<FrameworkException>(() => ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash + 1UL));
            StringAssert.Contains("重新构建配置", e.Message);
        }

        [Test]
        public void Open_TruncatedBlob_Throws()
        {
            byte[] bytes = new byte[ConfigBlob.HeaderSize - 1];
            Assert.Throws<FrameworkException>(() => ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash));
        }

        // ================================================================
        //  表查找
        // ================================================================

        [Test]
        public void TryGetTable_HitAndMiss()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                Assert.IsTrue(blob.TryGetTable(MonsterTableHash, out monsters));
                Assert.IsTrue(monsters.IsValid);
                Assert.AreEqual(3, monsters.RowCount);
                Assert.AreEqual(MonsterRowSize, monsters.RowSize);

                ConfigTableView stages;
                Assert.IsTrue(blob.TryGetTable("Stages", out stages));
                Assert.AreEqual(2, stages.RowCount);

                ConfigTableView missing;
                Assert.IsFalse(blob.TryGetTable("NotExist", out missing));
                Assert.IsFalse(missing.IsValid);
            }
            finally
            {
                blob.Release();
            }
        }

        // ================================================================
        //  主键二分与行访问
        // ================================================================

        [Test]
        public void FindRowOffset_FirstMiddleLastAndMissing()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                Assert.IsTrue(blob.TryGetTable(MonsterTableHash, out monsters));

                // 主键升序为 1001/1002/1003；行数据保持插入顺序 1001/1003/1002。
                int first = monsters.FindRowOffset(1001);
                int middle = monsters.FindRowOffset(1002);
                int last = monsters.FindRowOffset(1003);

                Assert.AreNotEqual(-1, first);
                Assert.AreNotEqual(-1, middle);
                Assert.AreNotEqual(-1, last);
                Assert.AreEqual(1001, blob.ReadInt32(first));
                Assert.AreEqual(1002, blob.ReadInt32(middle));
                Assert.AreEqual(1003, blob.ReadInt32(last));

                Assert.AreEqual(-1, monsters.FindRowOffset(9999));
                Assert.AreEqual(-1, monsters.FindRowOffset(0));
                Assert.AreEqual(-1, monsters.FindRowOffset(int.MinValue));
                Assert.IsTrue(monsters.ContainsKey(1002));
                Assert.IsFalse(monsters.ContainsKey(1004));

                // 行号索引与插入顺序一致。
                Assert.AreEqual(0, monsters.FindRowIndex(1001));
                Assert.AreEqual(1, monsters.FindRowIndex(1003));
                Assert.AreEqual(2, monsters.FindRowIndex(1002));

                // 索引区按主键升序。
                Assert.AreEqual(1001, monsters.GetPrimaryKeyAt(0));
                Assert.AreEqual(1002, monsters.GetPrimaryKeyAt(1));
                Assert.AreEqual(1003, monsters.GetPrimaryKeyAt(2));
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void FieldValues_RoundTrip()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);

                int row = monsters.FindRowOffset(1001);
                Assert.AreEqual(1001, blob.ReadInt32(row));
                Assert.AreEqual(12.5f, blob.ReadSingle(row + 4));

                int row3 = monsters.FindRowOffset(1003);
                Assert.AreEqual(-0.25f, blob.ReadSingle(row3 + 4));

                ConfigTableView stages;
                blob.TryGetTable(StageTableHash, out stages);

                int stage500 = stages.FindRowOffset(500);
                Assert.AreEqual(9000000000L, blob.ReadInt64(stage500));
                Assert.AreEqual(500, blob.ReadInt32(stage500 + 8));
                Assert.AreEqual(-3, blob.ReadInt16(stage500 + 16));
                Assert.IsTrue(blob.ReadBoolean(stage500 + 18));

                int stage100 = stages.FindRowOffset(100);
                Assert.AreEqual(-1L, blob.ReadInt64(stage100));
                Assert.AreEqual(ulong.MaxValue, blob.ReadUInt64(stage100));
                Assert.AreEqual(short.MaxValue, blob.ReadInt16(stage100 + 16));
                Assert.IsFalse(blob.ReadBoolean(stage100 + 18));
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void Enumerator_CoversAllRowsInStorageOrder()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);

                List<int> ids = new List<int>();
                foreach (int rowOffset in monsters)
                {
                    ids.Add(blob.ReadInt32(rowOffset));
                }

                CollectionAssert.AreEqual(new[] { 1001, 1003, 1002 }, ids);
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void GetRowOffsetByIndex_OutOfRange_Throws()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);
                Assert.Throws<FrameworkException>(() => monsters.GetRowOffsetByIndex(-1));
                Assert.Throws<FrameworkException>(() => monsters.GetRowOffsetByIndex(3));
            }
            finally
            {
                blob.Release();
            }
        }

        // ================================================================
        //  字符串池
        // ================================================================

        [Test]
        public void BlobString_DecodesAndDeduplicates()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);
                ConfigTableView stages;
                blob.TryGetTable(StageTableHash, out stages);

                BlobStringHandle fireInMonster = blob.ReadString(monsters.FindRowOffset(1001) + 8);
                BlobStringHandle fireInMonster2 = blob.ReadString(monsters.FindRowOffset(1002) + 8);
                BlobStringHandle fireInStage = blob.ReadString(stages.FindRowOffset(100) + 12);

                Assert.AreEqual("火", fireInMonster.ToString());
                Assert.AreEqual("火", fireInStage.ToString());

                // 全局去重：同一内容在不同表中共享同一段池数据。
                Assert.AreNotEqual(0, fireInMonster.Offset);
                Assert.AreEqual(fireInMonster.Offset, fireInMonster2.Offset);
                Assert.AreEqual(fireInMonster.Offset, fireInStage.Offset);
                Assert.IsTrue(fireInMonster.Equals(fireInStage));

                BlobStringHandle water = blob.ReadString(monsters.FindRowOffset(1003) + 8);
                Assert.AreNotEqual(fireInMonster.Offset, water.Offset);
                Assert.AreEqual("水", water.ToString());

                // UTF-8 长度与 char 数不同的多字节内容。
                Assert.AreEqual(3, fireInMonster.Utf8Length);
                Assert.AreEqual(1, fireInMonster.CharCount);

                Span<char> buffer = stackalloc char[8];
                int written = fireInMonster.GetChars(buffer);
                Assert.AreEqual(1, written);
                Assert.AreEqual('火', buffer[0]);

                Assert.IsTrue(fireInMonster.ContentEquals("火"));
                Assert.IsFalse(fireInMonster.ContentEquals("水"));

                BlobStringHandle icon = blob.ReadString(monsters.FindRowOffset(1002) + 12);
                Assert.AreEqual("icon_fire", icon.ToString());
                Assert.AreEqual(9, icon.Utf8Length);

                BlobStringHandle description = blob.ReadString(stages.FindRowOffset(500) + 12);
                Assert.AreEqual("第一关", description.ToString());
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void BlobString_EmptyAndNull_AreZeroOffset()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);

                BlobStringHandle empty = blob.ReadString(monsters.FindRowOffset(1001) + 12);
                BlobStringHandle fromNull = blob.ReadString(monsters.FindRowOffset(1003) + 12);

                Assert.AreEqual(0, empty.Offset);
                Assert.AreEqual(0, fromNull.Offset);
                Assert.IsTrue(empty.IsEmpty);
                Assert.IsTrue(fromNull.IsEmpty);
                Assert.AreEqual(0, empty.Utf8Length);
                Assert.AreEqual(string.Empty, fromNull.ToString());
                Assert.AreEqual(0, empty.GetChars(stackalloc char[4]));
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void BlobString_GetChars_BufferTooSmall_Throws()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                ConfigTableView monsters;
                blob.TryGetTable(MonsterTableHash, out monsters);
                BlobStringHandle icon = blob.ReadString(monsters.FindRowOffset(1002) + 12);
                Assert.Throws<FrameworkException>(() =>
                {
                    char[] tiny = new char[2];
                    icon.GetChars(tiny);
                });
            }
            finally
            {
                blob.Release();
            }
        }

        // ================================================================
        //  边界检查与生命周期
        // ================================================================

        [Test]
        public void Read_OutOfRange_Throws()
        {
            ConfigBlob blob = OpenSample();
            try
            {
                Assert.Throws<FrameworkException>(() => blob.ReadInt32(blob.Length));
                Assert.Throws<FrameworkException>(() => blob.ReadInt32(blob.Length - 3));
                Assert.Throws<FrameworkException>(() => blob.ReadInt64(blob.Length - 4));
                Assert.Throws<FrameworkException>(() => blob.ReadByte(-1));
                Assert.Throws<FrameworkException>(() => blob.Slice(0, blob.Length + 1));
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void RetainRelease_RefCounting()
        {
            ConfigBlob blob = OpenSample();

            blob.Retain();
            Assert.AreEqual(2, blob.ReferenceCount);
            Assert.IsFalse(blob.Release());
            Assert.AreEqual(1, blob.ReferenceCount);
            Assert.IsTrue(blob.Release());
            Assert.AreEqual(0, blob.ReferenceCount);

            // 释放后再读必须报错而不是访问已释放内存。
            Assert.Throws<FrameworkException>(() => blob.ReadInt32(0));
            Assert.Throws<FrameworkException>(() => blob.Release());
            Assert.Throws<FrameworkException>(() => blob.Retain());
        }

        // ================================================================
        //  Writer 的构建期校验
        // ================================================================

        [Test]
        public void Writer_RejectsBadUsage()
        {
            ConfigBlobWriter writer = new ConfigBlobWriter(TestSchemaHash);

            Assert.Throws<FrameworkException>(() => writer.BeginTable("Bad", 6), "rowSize 必须是 4 的倍数。");
            Assert.Throws<FrameworkException>(() => writer.AddRow(1), "未 BeginTable 就 AddRow 应报错。");

            writer.BeginTable("A", 8);
            Assert.Throws<FrameworkException>(() => writer.BeginTable("B", 8), "嵌套 BeginTable 应报错。");
            int row = writer.AddRow(1);
            Assert.Throws<FrameworkException>(() => writer.AddRow(1), "重复主键应报错。");
            Assert.Throws<FrameworkException>(() => writer.SetInt32(row, 6, 0), "字段越界应报错。");
            Assert.Throws<FrameworkException>(() => writer.SetInt32(5, 0, 0), "行号越界应报错。");
            writer.EndTable();

            Assert.Throws<FrameworkException>(() => writer.BeginTable("A", 8), "表名重复应报错。");
            Assert.Throws<FrameworkException>(() => writer.EndTable(), "多余的 EndTable 应报错。");

            writer.Build();
            Assert.Throws<FrameworkException>(() => writer.Build(), "重复 Build 应报错。");
        }

        [Test]
        public void Writer_EmptyTable_IsUsable()
        {
            ConfigBlobWriter writer = new ConfigBlobWriter(TestSchemaHash);
            writer.BeginTable("Empty", 4);
            writer.EndTable();

            ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(writer.Build()), TestSchemaHash);
            try
            {
                ConfigTableView table;
                Assert.IsTrue(blob.TryGetTable("Empty", out table));
                Assert.AreEqual(0, table.RowCount);
                Assert.AreEqual(-1, table.FindRowOffset(1));

                int count = 0;
                foreach (int unused in table)
                {
                    count++;
                }

                Assert.AreEqual(0, count);
            }
            finally
            {
                blob.Release();
            }
        }

        [Test]
        public void Hash_IsStableAndOrderSensitive()
        {
            Assert.AreEqual(ConfigBlobHash.Compute("Monsters"), ConfigBlobHash.Compute("Monsters"));
            Assert.AreNotEqual(ConfigBlobHash.Compute("Monsters"), ConfigBlobHash.Compute("Stages"));
            Assert.AreEqual(ConfigBlobHash.Compute("怪物"), ConfigBlobHash.Compute("怪物"));

            // FNV1a-64 的已知向量，防止实现被无意改动后与 codegen 侧不一致。
            Assert.AreEqual(0xAF63DC4C8601EC8CUL, ConfigBlobHash.Compute("a"));
            Assert.AreEqual(0xCBF29CE484222325UL, ConfigBlobHash.Compute(string.Empty));

            Assert.AreNotEqual(ConfigBlobHash.ComputeSchemaHash("ab", "c"), ConfigBlobHash.ComputeSchemaHash("a", "bc"));
        }

        // ================================================================
        //  Mmap 来源
        // ================================================================

        [Test]
        public void MmapBlobSource_ReadsRealFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());

                ConfigBlob blob = ConfigBlob.Open(new MmapBlobSource(path), TestSchemaHash);
                try
                {
                    ConfigTableView monsters;
                    Assert.IsTrue(blob.TryGetTable(MonsterTableHash, out monsters));
                    Assert.AreEqual(3, monsters.RowCount);
                    Assert.AreEqual(12.5f, blob.ReadSingle(monsters.FindRowOffset(1001) + 4));
                    Assert.AreEqual("icon_fire", blob.ReadString(monsters.FindRowOffset(1002) + 12).ToString());
                }
                finally
                {
                    blob.Release();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void NativeAllocBlobSource_ReadsRealFile()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());

                ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(path), TestSchemaHash);
                try
                {
                    ConfigTableView stages;
                    Assert.IsTrue(blob.TryGetTable(StageTableHash, out stages));
                    Assert.AreEqual(9000000000L, blob.ReadInt64(stages.FindRowOffset(500)));
                }
                finally
                {
                    blob.Release();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ================================================================
        //  zip 条目定位
        // ================================================================

        [Test]
        public void ZipEntryLocator_LocatesStoredEntry()
        {
            byte[] payload = Encoding.UTF8.GetBytes("HELLO-CONFIG-BLOB-PAYLOAD");
            byte[] zip = BuildMinimalZip("assets/config.ejcb", payload, stored: true);

            using (MemoryStream stream = new MemoryStream(zip))
            {
                long offset;
                long length;
                Assert.IsTrue(ZipEntryLocator.TryLocate(stream, "assets/config.ejcb", out offset, out length));
                Assert.AreEqual(payload.Length, length);

                byte[] actual = new byte[length];
                Buffer.BlockCopy(zip, (int)offset, actual, 0, (int)length);
                CollectionAssert.AreEqual(payload, actual);
            }
        }

        [Test]
        public void ZipEntryLocator_MissingEntry_ReturnsFalse()
        {
            byte[] zip = BuildMinimalZip("assets/config.ejcb", new byte[] { 1, 2, 3 }, stored: true);
            using (MemoryStream stream = new MemoryStream(zip))
            {
                long offset;
                long length;
                Assert.IsFalse(ZipEntryLocator.TryLocate(stream, "assets/other.ejcb", out offset, out length));
            }
        }

        [Test]
        public void ZipEntryLocator_CompressedEntry_ThrowsWithNoCompressHint()
        {
            byte[] zip = BuildMinimalZip("assets/config.ejcb", new byte[] { 1, 2, 3, 4 }, stored: false);
            using (MemoryStream stream = new MemoryStream(zip))
            {
                long offset;
                long length;
                FrameworkException e = Assert.Throws<FrameworkException>(
                    () => ZipEntryLocator.TryLocate(stream, "assets/config.ejcb", out offset, out length));
                StringAssert.Contains("noCompress", e.Message);
            }
        }

        [Test]
        public void ZipEntryLocator_NotAZip_Throws()
        {
            using (MemoryStream stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
            {
                long offset;
                long length;
                Assert.Throws<FrameworkException>(() => ZipEntryLocator.TryLocate(stream, "x", out offset, out length));
            }
        }

        // ================================================================
        //  损坏/恶意 blob：必须一律 FrameworkException，绝不静默读错或崩溃
        // ================================================================

        /// <summary>定位某张表在表目录中的条目偏移（表目录按 nameHash 升序，需扫描）。</summary>
        private static int FindDirectoryEntry(byte[] blob, ulong nameHash)
        {
            int tableCount = ReadInt32(blob, 16);
            for (int i = 0; i < tableCount; i++)
            {
                int entry = ConfigBlob.HeaderSize + i * ConfigBlob.TableDirectoryEntrySize;
                if ((ulong)ReadInt64(blob, entry) == nameHash)
                {
                    return entry;
                }
            }

            throw new InvalidOperationException("测试构造错误：表目录中找不到该表。");
        }

        private static int ReadInt32(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
        }

        private static long ReadInt64(byte[] b, int o)
        {
            return (uint)ReadInt32(b, o) | ((long)ReadInt32(b, o + 4) << 32);
        }

        private static void WriteInt32(byte[] b, int o, int v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        /// <summary>打开一份被篡改的 blob 并对 Monsters 表做一次典型访问，断言抛 FrameworkException。</summary>
        private static void AssertCorruptionRejected(Action<byte[]> corrupt, string because)
        {
            byte[] bytes = BuildSampleBlob();
            corrupt(bytes);

            Assert.Throws<FrameworkException>(() =>
            {
                ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash);
                try
                {
                    ConfigTableView view;
                    if (blob.TryGetTable(MonsterTableHash, out view))
                    {
                        // 命中则继续做一次真实读取，把损坏逼到读路径上暴露。
                        int rowOffset = view.FindRowOffset(1002);
                        if (rowOffset >= 0)
                        {
                            blob.ReadInt32(rowOffset);
                            blob.ReadString(rowOffset + 12).ToString();
                        }

                        foreach (int offset in view)
                        {
                            blob.ReadInt32(offset);
                        }
                    }
                }
                finally
                {
                    blob.Release();
                }
            }, because);
        }

        [Test]
        public void Corrupt_TableOffsetOutOfRange_Throws()
        {
            AssertCorruptionRejected(
                b => WriteInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8, b.Length - 4),
                "tableOffset 越界必须被拒绝。");
        }

        [Test]
        public void Corrupt_NegativeTableOffset_Throws()
        {
            AssertCorruptionRejected(
                b => WriteInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8, -16),
                "负的 tableOffset 必须被拒绝。");
        }

        [Test]
        public void Corrupt_TableSizeTooSmallForDeclaredRows_Throws()
        {
            // tableSize 缩到只剩表头，但 rowCount 仍是 3：PK 区与行区都会溢出表边界。
            AssertCorruptionRejected(
                b => WriteInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 12, ConfigBlob.TableHeaderSize),
                "tableSize 装不下声明的行数必须被拒绝。");
        }

        [Test]
        public void Corrupt_NegativePkIndexOffset_Throws()
        {
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    WriteInt32(b, tableOffset + 8, -32);
                },
                "负的 pkIndexOffset 必须被拒绝（不得回绕成表外地址）。");
        }

        [Test]
        public void Corrupt_HugeRowCount_ThrowsFrameworkExceptionNotOverflow()
        {
            // rowCount * rowSize 在 int 上会回绕；M-3 要求用 long 预判并抛 FrameworkException，
            // 而不是 checked 的 OverflowException——契约是"损坏一律 FrameworkException + 可执行提示"。
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    WriteInt32(b, tableOffset, int.MaxValue);
                },
                "rowCount=int.MaxValue 必须抛 FrameworkException 而非 OverflowException。");
        }

        [Test]
        public void Corrupt_HugeRowSize_ThrowsFrameworkExceptionNotOverflow()
        {
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    WriteInt32(b, tableOffset + 4, int.MaxValue);
                },
                "rowSize=int.MaxValue 必须抛 FrameworkException 而非 OverflowException。");
        }

        [Test]
        public void Corrupt_ZeroRowSize_Throws()
        {
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    WriteInt32(b, tableOffset + 4, 0);
                },
                "rowSize=0 必须被拒绝。");
        }

        [Test]
        public void Corrupt_PkIndexRowNumberOutOfRange_Throws()
        {
            // M-1：索引区的行号被篡改。不校验的话它会被直接乘进行偏移，
            // 只要落在别的合法区域内就永远发现不了，会静默读到错误的行。
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    int rowCount = ReadInt32(b, tableOffset);
                    int pkIndex = tableOffset + ReadInt32(b, tableOffset + 8);
                    // 行号数组紧跟在主键数组之后；把第 1 项（主键 1002 对应项）改成越界行号。
                    WriteInt32(b, pkIndex + rowCount * 4 + 4, 9999);
                },
                "索引区行号越界必须被拒绝，而不是静默读错行。");
        }

        [Test]
        public void Corrupt_StringSlotPointsOutsidePool_Throws()
        {
            AssertCorruptionRejected(
                b =>
                {
                    int tableOffset = ReadInt32(b, FindDirectoryEntry(b, MonsterTableHash) + 8);
                    int rowSize = ReadInt32(b, tableOffset + 4);
                    int rows = tableOffset + ReadInt32(b, tableOffset + 12);
                    // 把第 2 行（主键 1002，Icon 字段有值）的字符串槽指到文件末尾附近，
                    // 读长度字段时就会越界。
                    WriteInt32(b, rows + 2 * rowSize + 12, b.Length - 2);
                },
                "字符串槽指向池外必须被拒绝。");
        }

        [Test]
        public void Corrupt_StringLengthNegativeOrHuge_Throws()
        {
            // 先拿到一个真实字符串记录的偏移，再分别把它的长度字段改成负值与巨值。
            int stringRecordOffset;
            ConfigBlob probe = OpenSample();
            try
            {
                ConfigTableView view;
                probe.TryGetTable(MonsterTableHash, out view);
                stringRecordOffset = probe.ReadString(view.FindRowOffset(1002) + 12).Offset;
                Assert.AreNotEqual(0, stringRecordOffset);
            }
            finally
            {
                probe.Release();
            }

            foreach (int badLength in new[] { -1, int.MaxValue })
            {
                byte[] bytes = BuildSampleBlob();
                WriteInt32(bytes, stringRecordOffset, badLength);

                ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash);
                try
                {
                    ConfigTableView view;
                    blob.TryGetTable(MonsterTableHash, out view);
                    BlobStringHandle handle = blob.ReadString(view.FindRowOffset(1002) + 12);
                    Assert.Throws<FrameworkException>(() => handle.ToString(),
                        Utility.Text.Format("字符串长度 {0} 必须被拒绝。", badLength));
                }
                finally
                {
                    blob.Release();
                }
            }
        }

        [Test]
        public void Corrupt_TableCountNegativeOrHuge_Throws()
        {
            foreach (int badCount in new[] { -1, int.MaxValue })
            {
                byte[] bytes = BuildSampleBlob();
                WriteInt32(bytes, 16, badCount);
                Assert.Throws<FrameworkException>(
                    () => ConfigBlob.Open(new NativeAllocBlobSource(bytes), TestSchemaHash),
                    Utility.Text.Format("tableCount={0} 必须在 Open 阶段就被拒绝。", badCount));
            }
        }

        // ================================================================
        //  默认构造的视图 / 句柄
        // ================================================================

        [Test]
        public void DefaultTableView_AllOperationsThrow()
        {
            ConfigTableView view = default(ConfigTableView);
            Assert.IsFalse(view.IsValid);
            Assert.IsNull(view.Blob);
            Assert.AreEqual(0, view.RowCount);

            Assert.Throws<FrameworkException>(() => view.FindRowIndex(1));
            Assert.Throws<FrameworkException>(() => view.FindRowOffset(1));
            Assert.Throws<FrameworkException>(() => view.ContainsKey(1));
            Assert.Throws<FrameworkException>(() => view.GetRowOffsetByIndex(0));
            Assert.Throws<FrameworkException>(() => view.GetPrimaryKeyAt(0));
        }

        [Test]
        public void DefaultStringHandle_IsEmpty()
        {
            BlobStringHandle handle = default(BlobStringHandle);
            Assert.IsTrue(handle.IsEmpty);
            Assert.AreEqual(0, handle.Offset);
            Assert.AreEqual(0, handle.Utf8Length);
            Assert.AreEqual(0, handle.CharCount);
            Assert.AreEqual(string.Empty, handle.ToString());
            Assert.IsTrue(handle.ContentEquals(null));
            Assert.IsTrue(handle.ContentEquals(string.Empty));
            Assert.IsFalse(handle.ContentEquals("x"));
        }

        [Test]
        public void BlobStringContentEquals_HandlesAsciiBmpAndSurrogatePairs()
        {
            var writer = new ConfigBlobWriter(TestSchemaHash);
            writer.BeginTable("Strings", 8);
            int asciiRow = writer.AddRow(1);
            writer.SetString(asciiRow, 4, "plain");
            int bmpRow = writer.AddRow(2);
            writer.SetString(bmpRow, 4, "火");
            int supplementaryRow = writer.AddRow(3);
            writer.SetString(supplementaryRow, 4, "A😀B");
            writer.EndTable();

            ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(writer.Build()), TestSchemaHash);
            try
            {
                Assert.IsTrue(blob.TryGetTable("Strings", out ConfigTableView strings));
                Assert.IsTrue(blob.ReadString(strings.FindRowOffset(1) + 4).ContentEquals("plain"));
                Assert.IsTrue(blob.ReadString(strings.FindRowOffset(2) + 4).ContentEquals("火"));
                Assert.IsTrue(blob.ReadString(strings.FindRowOffset(3) + 4).ContentEquals("A😀B"));
                Assert.IsFalse(blob.ReadString(strings.FindRowOffset(3) + 4).ContentEquals("A😀C"));
            }
            finally
            {
                blob.Release();
            }
        }

        // ================================================================
        //  Writer：字符串覆盖写（M-5）
        // ================================================================

        [Test]
        public void Writer_SetStringOverwrite_LastWriteWins()
        {
            ConfigBlobWriter writer = new ConfigBlobWriter(TestSchemaHash);
            writer.BeginTable("T", 12);
            int r = writer.AddRow(1);
            writer.SetInt32(r, 0, 1);

            // 非空 → 空：回填表里的旧记录必须被撤销，否则 Build 会把 0 覆盖回旧偏移。
            writer.SetString(r, 4, "会被清掉");
            writer.SetString(r, 4, string.Empty);

            // 非空 → 另一个非空：必须是后写的生效。
            writer.SetString(r, 8, "旧值");
            writer.SetString(r, 8, "新值");
            writer.EndTable();

            ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(writer.Build()), TestSchemaHash);
            try
            {
                ConfigTableView view;
                blob.TryGetTable("T", out view);
                int row = view.FindRowOffset(1);

                BlobStringHandle cleared = blob.ReadString(row + 4);
                Assert.AreEqual(0, cleared.Offset, "写空串必须真的清成 0 偏移。");
                Assert.IsTrue(cleared.IsEmpty);

                Assert.AreEqual("新值", blob.ReadString(row + 8).ToString());
            }
            finally
            {
                blob.Release();
            }
        }

        // ================================================================
        //  来源：区间重载与 APK 端到端
        // ================================================================

        [Test]
        public void NativeAllocBlobSource_RangeOverload()
        {
            byte[] payload = BuildSampleBlob();
            byte[] padded = new byte[payload.Length + 16];
            Buffer.BlockCopy(payload, 0, padded, 7, payload.Length);

            ConfigBlob blob = ConfigBlob.Open(new NativeAllocBlobSource(padded, 7, payload.Length), TestSchemaHash);
            try
            {
                ConfigTableView view;
                Assert.IsTrue(blob.TryGetTable(MonsterTableHash, out view));
                Assert.AreEqual(3, view.RowCount);
            }
            finally
            {
                blob.Release();
            }

            Assert.Throws<FrameworkException>(() => new NativeAllocBlobSource(padded, 7, padded.Length));
            Assert.Throws<FrameworkException>(() => new NativeAllocBlobSource(padded, -1, 4));
            Assert.Throws<FrameworkException>(() => new NativeAllocBlobSource(padded, 4, int.MaxValue),
                "offset+length 溢出 int 时不得回绕成通过检查。");
        }

        [Test]
        public void ApkOffsetBlobSource_EndToEnd()
        {
            const string entryName = "assets/Configs/config.ejcb";
            byte[] zip = BuildMinimalZip(entryName, BuildSampleBlob(), stored: true, localExtraLength: 6, centralExtraLength: 2);

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, zip);

                ConfigBlob blob = ConfigBlob.Open(ApkOffsetBlobSource.Open(path, entryName), TestSchemaHash);
                try
                {
                    ConfigTableView monsters;
                    Assert.IsTrue(blob.TryGetTable(MonsterTableHash, out monsters));
                    Assert.AreEqual(3, monsters.RowCount);
                    Assert.AreEqual(12.5f, blob.ReadSingle(monsters.FindRowOffset(1001) + 4));
                    Assert.AreEqual("icon_fire", blob.ReadString(monsters.FindRowOffset(1002) + 12).ToString());
                }
                finally
                {
                    blob.Release();
                }

                Assert.Throws<FrameworkException>(() => ApkOffsetBlobSource.Open(path, "assets/nope.ejcb"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void MmapBlobSource_MissingFile_ThrowsFrameworkException()
        {
            string missing = Path.Combine(Path.GetTempPath(), "ejoy_configblob_not_here_" + Guid.NewGuid().ToString("N") + ".bin");
            Assert.Throws<FrameworkException>(() => new MmapBlobSource(missing));
        }

        // ================================================================
        //  zip 定位器的两个经典坑
        // ================================================================

        [Test]
        public void ZipEntryLocator_LocalExtraDiffersFromCentralExtra()
        {
            // 数据起点只能由本地文件头的 extra 长度决定。若实现错用中央目录的长度（这里是 2 而非 11），
            // 定位结果会偏 9 字节，下面的内容比对会失败。
            byte[] payload = Encoding.UTF8.GetBytes("PAYLOAD-AFTER-LOCAL-EXTRA");
            byte[] zip = BuildMinimalZip("assets/config.ejcb", payload, stored: true, localExtraLength: 11, centralExtraLength: 2);

            using (MemoryStream stream = new MemoryStream(zip))
            {
                long offset;
                long length;
                Assert.IsTrue(ZipEntryLocator.TryLocate(stream, "assets/config.ejcb", out offset, out length));
                Assert.AreEqual(payload.Length, length);

                byte[] actual = new byte[length];
                Buffer.BlockCopy(zip, (int)offset, actual, 0, (int)length);
                CollectionAssert.AreEqual(payload, actual, "数据起点必须按本地文件头的 extra 长度计算。");
            }
        }

        [Test]
        public void ZipEntryLocator_EocdCommentContainingFakeSignature()
        {
            // 注释里内嵌一个假的 EOCD 签名：从尾部反向扫描时会先撞上它，
            // 只有"剩余长度与注释长度字段自洽"这一步校验能把它排除掉。
            byte[] comment = new byte[64];
            for (int i = 0; i < comment.Length; i++)
            {
                comment[i] = (byte)'#';
            }

            comment[20] = 0x50;
            comment[21] = 0x4B;
            comment[22] = 0x05;
            comment[23] = 0x06;

            byte[] payload = Encoding.UTF8.GetBytes("COMMENTED-ZIP-PAYLOAD");
            byte[] zip = BuildMinimalZip("assets/config.ejcb", payload, stored: true, zipComment: comment);

            using (MemoryStream stream = new MemoryStream(zip))
            {
                long offset;
                long length;
                Assert.IsTrue(ZipEntryLocator.TryLocate(stream, "assets/config.ejcb", out offset, out length));
                Assert.AreEqual(payload.Length, length);

                byte[] actual = new byte[length];
                Buffer.BlockCopy(zip, (int)offset, actual, 0, (int)length);
                CollectionAssert.AreEqual(payload, actual);
            }
        }

        // ================================================================
        //  ConfigBlobFile（打开门面）
        // ================================================================

        [Test]
        public void ConfigBlobFile_OpenFile_RoundTrip()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());

                ConfigBlob blob = ConfigBlobFile.OpenFile(path, TestSchemaHash);
                try
                {
                    Assert.AreEqual(2, blob.TableCount);
                    Assert.IsTrue(blob.TryGetTable("Monsters", out ConfigTableView monsters));
                    Assert.AreEqual(3, monsters.RowCount);
                    Assert.GreaterOrEqual(monsters.FindRowOffset(1002), 0);
                }
                finally
                {
                    blob.Release();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ConfigBlobFile_OpenFile_BufferedMode_RoundTrip()
        {
            // allowMemoryMapping:false 是编辑器工具链的读法（映射会锁文件），行为必须与映射路径等价。
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());

                ConfigBlob blob = ConfigBlobFile.OpenFile(path, TestSchemaHash, allowMemoryMapping: false);
                try
                {
                    Assert.AreEqual(2, blob.TableCount);
                    Assert.IsTrue(blob.TryGetTable("Monsters", out ConfigTableView monsters));
                    Assert.AreEqual(3, monsters.RowCount);
                }
                finally
                {
                    blob.Release();
                }

                // 整块读入不锁文件：打开期间就应当可以覆盖写（Windows 上映射路径做不到这一点）。
                using (ConfigBlobScope scope = new ConfigBlobScope(ConfigBlobFile.OpenFile(path, TestSchemaHash, allowMemoryMapping: false)))
                {
                    File.WriteAllBytes(path, BuildSampleBlob());
                    Assert.AreEqual(2, scope.Blob.TableCount, "覆盖源文件不应影响已读入内存的 blob。");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>用 using 保证测试断言失败时 blob 也被 Release 的小工具。</summary>
        private readonly struct ConfigBlobScope : IDisposable
        {
            public readonly ConfigBlob Blob;

            public ConfigBlobScope(ConfigBlob blob)
            {
                Blob = blob;
            }

            public void Dispose()
            {
                Blob.Release();
            }
        }

        [Test]
        public void ConfigBlobFile_OpenFile_MissingFile_ThrowsWithExportHint()
        {
            string path = Path.Combine(Path.GetTempPath(), "ejoy-configblob-does-not-exist.ejcb");
            FrameworkException ex = Assert.Throws<FrameworkException>(() => ConfigBlobFile.OpenFile(path, TestSchemaHash));
            StringAssert.Contains("文件不存在", ex.Message);
        }

        [Test]
        public void ConfigBlobFile_OpenFile_WrongSchemaHash_Throws()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());
                Assert.Throws<FrameworkException>(() => ConfigBlobFile.OpenFile(path, TestSchemaHash ^ 0xFFUL));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ConfigBlobFile_OpenZipEntry_RoundTrip()
        {
            byte[] zip = BuildMinimalZip("assets/ConfigTables.ejcb", BuildSampleBlob(), stored: true);
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, zip);

                ConfigBlob blob = ConfigBlobFile.OpenZipEntry(path, "assets/ConfigTables.ejcb", TestSchemaHash);
                try
                {
                    Assert.AreEqual(2, blob.TableCount);
                    Assert.IsTrue(blob.TryGetTable("Stages", out ConfigTableView stages));
                    Assert.AreEqual(2, stages.RowCount);
                }
                finally
                {
                    blob.Release();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ConfigBlobFile_OpenZipEntry_MissingEntry_Throws()
        {
            byte[] zip = BuildMinimalZip("assets/other.ejcb", BuildSampleBlob(), stored: true);
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, zip);
                FrameworkException ex = Assert.Throws<FrameworkException>(
                    () => ConfigBlobFile.OpenZipEntry(path, "assets/ConfigTables.ejcb", TestSchemaHash));
                StringAssert.Contains("不存在条目", ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ConfigBlobFile_OpenZipEntry_BufferedMode_RoundTrip()
        {
            // allowMemoryMapping:false 强制走"按偏移整块读入"——这正是 Android 上映射不可用设备的
            // 回退路径主体，必须与映射路径行为等价，且不能只在故障注入下才可达。
            byte[] zip = BuildMinimalZip("assets/ConfigTables.ejcb", BuildSampleBlob(), stored: true);
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, zip);

                ConfigBlob blob = ConfigBlobFile.OpenZipEntry(path, "assets/ConfigTables.ejcb", TestSchemaHash, allowMemoryMapping: false);
                try
                {
                    Assert.AreEqual(2, blob.TableCount);
                    Assert.IsTrue(blob.TryGetTable("Monsters", out ConfigTableView monsters));
                    Assert.AreEqual(3, monsters.RowCount);
                    Assert.GreaterOrEqual(monsters.FindRowOffset(1003), 0);
                }
                finally
                {
                    blob.Release();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ConfigBlobFile_OpenFile_MmapAndFallbackBothFail_ReportsBothReasons()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Assert.Ignore("依赖 Windows 的强制文件锁语义（FileShare.None 拒绝并发打开），POSIX 上不可复现。");
            }

            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, BuildSampleBlob());

                // 独占句柄让 mmap 与回退读入都无法打开文件：
                // 抛出的异常必须同时带上两个失败原因，否则启动早期（日志助手未注册时）映射失败的线索会彻底丢失。
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    FrameworkException ex = Assert.Throws<FrameworkException>(
                        () => ConfigBlobFile.OpenFile(path, TestSchemaHash));
                    StringAssert.Contains("均失败", ex.Message);
                    StringAssert.Contains("映射失败原因", ex.Message);
                    Assert.IsNotNull(ex.InnerException, "整块读入的失败必须作为内部异常保留。");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 手工拼一个只含单个条目的最小 zip（本地文件头 + 数据 + 中央目录 + EOCD）。
        /// <paramref name="stored"/> 为 false 时只把 compressionMethod 标成 Deflate（数据仍是原文），
        /// 用于验证定位器对压缩条目的拒绝路径——该路径在读到方法号时就已失败，不会触碰数据。
        /// <paramref name="localExtraLength"/> 与 <paramref name="centralExtraLength"/> 刻意可以不同：
        /// zip 规范允许两处 extra 字段长度不一致，数据真正的起点只能由本地文件头决定。
        /// 若定位器错误地拿中央目录的长度去算，就会读偏——这是该格式最经典的坑。
        /// <paramref name="zipComment"/> 用于构造带注释的 EOCD。
        /// </summary>
        private static byte[] BuildMinimalZip(
            string entryName,
            byte[] payload,
            bool stored,
            int localExtraLength = 0,
            int centralExtraLength = 0,
            byte[] zipComment = null)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(entryName);
            ushort method = stored ? (ushort)0 : (ushort)8;
            byte[] localExtra = new byte[localExtraLength];
            byte[] centralExtra = new byte[centralExtraLength];
            for (int i = 0; i < localExtra.Length; i++)
            {
                localExtra[i] = 0xAA;
            }

            for (int i = 0; i < centralExtra.Length; i++)
            {
                centralExtra[i] = 0xBB;
            }

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                long localHeaderOffset = stream.Position;

                writer.Write(0x04034B50u);            // 本地文件头签名
                writer.Write((ushort)20);             // version needed
                writer.Write((ushort)0);              // flags
                writer.Write(method);                 // compression method
                writer.Write((ushort)0);              // mod time
                writer.Write((ushort)0);              // mod date
                writer.Write(0u);                     // crc32
                writer.Write((uint)payload.Length);   // compressed size
                writer.Write((uint)payload.Length);   // uncompressed size
                writer.Write((ushort)nameBytes.Length);
                writer.Write((ushort)localExtra.Length);
                writer.Write(nameBytes);
                writer.Write(localExtra);
                writer.Write(payload);

                long centralDirectoryOffset = stream.Position;

                writer.Write(0x02014B50u);            // 中央目录头签名
                writer.Write((ushort)20);             // version made by
                writer.Write((ushort)20);             // version needed
                writer.Write((ushort)0);              // flags
                writer.Write(method);                 // compression method
                writer.Write((ushort)0);              // mod time
                writer.Write((ushort)0);              // mod date
                writer.Write(0u);                     // crc32
                writer.Write((uint)payload.Length);   // compressed size
                writer.Write((uint)payload.Length);   // uncompressed size
                writer.Write((ushort)nameBytes.Length);
                writer.Write((ushort)centralExtra.Length);
                writer.Write((ushort)0);              // comment length
                writer.Write((ushort)0);              // disk number start
                writer.Write((ushort)0);              // internal attributes
                writer.Write(0u);                     // external attributes
                writer.Write((uint)localHeaderOffset);
                writer.Write(nameBytes);
                writer.Write(centralExtra);

                long centralDirectorySize = stream.Position - centralDirectoryOffset;

                writer.Write(0x06054B50u);            // EOCD 签名
                writer.Write((ushort)0);              // disk number
                writer.Write((ushort)0);              // disk with CD
                writer.Write((ushort)1);              // entries on this disk
                writer.Write((ushort)1);              // total entries
                writer.Write((uint)centralDirectorySize);
                writer.Write((uint)centralDirectoryOffset);
                writer.Write((ushort)(zipComment != null ? zipComment.Length : 0));
                if (zipComment != null)
                {
                    writer.Write(zipComment);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
