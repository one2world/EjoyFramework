//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Blobs;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// ConfigBlob 热路径的零 GC 回归防线。
    ///
    /// 为什么用 AllocatingGCMemory 约束而不是 GC.GetAllocatedBytesForCurrentThread：
    /// 后者在 Unity 编辑器的 Mono/Boehm 运行时下是死计数器（恒零，本套件的
    /// <see cref="Detector_SeesKnownAllocation_PositiveControl"/> 反向证明了这一点会被误读），
    /// 前者走 Profiler 的 GC.Alloc 事件，在 Boehm 下真实有效。
    ///
    /// 覆盖面说明：断言对象是 ConfigBlob/ConfigTableView/BlobStringHandle 的读原语——
    /// 生成代码（XxxRow/XxxTable）是这些原语加编译期常量偏移的 readonly struct 薄壳，
    /// 不引入接口/虚调用/装箱点，因此原语级零分配即覆盖生成层热路径
    /// （生成层位于 Assembly-CSharp，框架测试程序集无法直接引用）。
    /// 每个断言前先把被测操作真实执行一遍：首次 JIT 的内部分配不属于稳态行为。
    /// </summary>
    public sealed class ConfigBlobZeroGcTests
    {
        private const ulong TestSchemaHash = 0x0123456789ABCDEFUL;

        private ConfigBlob m_Blob;
        private ConfigTableView m_Monsters;
        private ulong m_MonstersHash;

        [OneTimeSetUp]
        public void OpenSampleBlob()
        {
            // 与 ConfigBlobTests.BuildSampleBlob 同构的小样本：一张 Monsters 表足够覆盖全部读原语。
            var writer = new ConfigBlobWriter(TestSchemaHash);
            writer.BeginTable("Monsters", 16);
            int r0 = writer.AddRow(1001);
            writer.SetInt32(r0, 0, 1001);
            writer.SetSingle(r0, 4, 12.5f);
            writer.SetString(r0, 8, "火");
            writer.SetString(r0, 12, "icon_fire");
            int r1 = writer.AddRow(1002);
            writer.SetInt32(r1, 0, 1002);
            writer.SetSingle(r1, 4, -0.25f);
            writer.SetString(r1, 8, "火");
            writer.SetString(r1, 12, string.Empty);
            writer.EndTable();

            m_Blob = ConfigBlob.Open(new NativeAllocBlobSource(writer.Build()), TestSchemaHash);
            m_MonstersHash = ConfigBlobHash.Compute("Monsters");
            if (!m_Blob.TryGetTable(m_MonstersHash, out m_Monsters))
            {
                throw new FrameworkException("ConfigBlobZeroGcTests：样本表打开失败。");
            }
        }

        [OneTimeTearDown]
        public void ReleaseSampleBlob()
        {
            if (m_Blob != null)
            {
                m_Blob.Release();
                m_Blob = null;
            }
        }

        /// <summary>
        /// 阳性对照：探测器必须能看见一次明确的托管分配。它失败说明 AllocatingGCMemory
        /// 在当前环境根本不工作——那样的话下面所有"零分配"绿灯都是假的。
        /// </summary>
        [Test]
        public void Detector_SeesKnownAllocation_PositiveControl()
        {
            Assert.That(() =>
            {
                byte[] garbage = new byte[1024];
                garbage[0] = 1;
            }, new AllocatingGCMemoryConstraint());
        }

        [Test]
        public void TableLookup_ByHash_DoesNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            ulong hash = m_MonstersHash;
            blob.TryGetTable(hash, out _);

            Assert.That(() =>
            {
                blob.TryGetTable(hash, out ConfigTableView view);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void RowLookup_BinarySearchAndByIndex_DoesNotAllocate()
        {
            ConfigTableView view = m_Monsters;
            view.FindRowOffset(1002);
            view.GetRowOffsetByIndex(0);

            Assert.That(() =>
            {
                int hit = view.FindRowOffset(1002);
                int miss = view.FindRowOffset(999999);
                int byIndex = view.GetRowOffsetByIndex(1);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void FieldReads_AllPrimitives_DoNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            int row = m_Monsters.GetRowOffsetByIndex(0);
            blob.ReadInt32(row + 0);

            Assert.That(() =>
            {
                blob.ReadInt32(row + 0);
                blob.ReadSingle(row + 4);
                blob.ReadInt64(row + 0);
                blob.ReadInt16(row + 0);
                blob.ReadUInt16(row + 0);
                blob.ReadUInt32(row + 0);
                blob.ReadUInt64(row + 0);
                blob.ReadDouble(row + 0);
                blob.ReadByte(row + 0);
                blob.ReadSByte(row + 0);
                blob.ReadBoolean(row + 0);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void StringHandleMetadataAndEquality_WithoutToString_DoNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            int row0 = m_Monsters.GetRowOffsetByIndex(0);
            int row1 = m_Monsters.GetRowOffsetByIndex(1);
            BlobStringHandle warm = blob.ReadString(row0 + 8);
            warm.Equals(blob.ReadString(row1 + 8));
            warm.GetHashCode();
            _ = warm.Utf8Length;
            _ = warm.CharCount;
            _ = warm.IsEmpty;

            Assert.That(() =>
            {
                BlobStringHandle a = blob.ReadString(row0 + 8);
                BlobStringHandle b = blob.ReadString(row1 + 8);
                bool same = a.Equals(b);
                int hash = a.GetHashCode();
                int utf8 = a.Utf8Length;
                int chars = a.CharCount;
                bool empty = a.IsEmpty;
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void StringHandleContentEquals_DoesNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            int row0 = m_Monsters.GetRowOffsetByIndex(0);
            BlobStringHandle warm = blob.ReadString(row0 + 8);
            warm.ContentEquals("火");
            warm.ContentEquals("水");

            TestDelegate operation = () =>
            {
                BlobStringHandle a = blob.ReadString(row0 + 8);
                bool eq = a.ContentEquals("火");
                bool ne = a.ContentEquals("水");
            };
            operation();

            Assert.That(operation, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void StringHandle_GetChars_IntoStackBuffer_DoesNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            int row0 = m_Monsters.GetRowOffsetByIndex(0);
            blob.ReadString(row0 + 12).GetChars(stackalloc char[16]);

            Assert.That(() =>
            {
                Span<char> buffer = stackalloc char[16];
                int written = blob.ReadString(row0 + 12).GetChars(buffer);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void FullTableScan_AllFieldsAllRows_DoesNotAllocate()
        {
            ConfigBlob blob = m_Blob;
            ConfigTableView view = m_Monsters;
            long warmSink = ScanOnce(blob, view);

            Assert.That(() =>
            {
                long sink = ScanOnce(blob, view);
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(warmSink, ScanOnce(blob, view), "扫描结果必须确定。");
        }

        private static long ScanOnce(ConfigBlob blob, ConfigTableView view)
        {
            long sink = 0;
            for (int r = 0; r < view.RowCount; r++)
            {
                int row = view.GetRowOffsetByIndex(r);
                sink += blob.ReadInt32(row + 0);
                sink += (long)blob.ReadSingle(row + 4);
                sink += blob.ReadString(row + 8).Utf8Length;
                sink += blob.ReadString(row + 12).Utf8Length;
            }

            return sink;
        }
    }
}
