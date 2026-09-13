//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using EjoyFramework.GamePlay.Netcode.Messages;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Messages
{
    /// <summary>
    /// 针对 <see cref="NetWriter"/> 与 <see cref="NetReader"/> 的单元测试：覆盖每个基础类型的精确往返、
    /// 字符串的 null/空/Unicode、字节数组的 null/空、已知整数的小端字节序断言、读取越界异常与写入器复用。
    /// </summary>
    [TestFixture]
    public class NetWriterReaderTests
    {
        // ---- 基础类型往返 ----

        [Test]
        public void Byte_RoundTrips()
        {
            NetWriter w = new NetWriter();
            w.WriteByte(0);
            w.WriteByte(200);
            w.WriteByte(255);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadByte());
            Assert.AreEqual(200, r.ReadByte());
            Assert.AreEqual(255, r.ReadByte());
        }

        [Test]
        public void Bool_RoundTrips()
        {
            NetWriter w = new NetWriter();
            w.WriteBool(true);
            w.WriteBool(false);

            NetReader r = new NetReader(w.ToArray());
            Assert.IsTrue(r.ReadBool());
            Assert.IsFalse(r.ReadBool());
        }

        [Test]
        public void Short_RoundTrips_IncludingExtremes()
        {
            NetWriter w = new NetWriter();
            w.WriteShort(0);
            w.WriteShort(short.MinValue);
            w.WriteShort(short.MaxValue);
            w.WriteShort(-1234);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadShort());
            Assert.AreEqual(short.MinValue, r.ReadShort());
            Assert.AreEqual(short.MaxValue, r.ReadShort());
            Assert.AreEqual(-1234, r.ReadShort());
        }

        [Test]
        public void UShort_RoundTrips_IncludingExtremes()
        {
            NetWriter w = new NetWriter();
            w.WriteUShort(0);
            w.WriteUShort(ushort.MaxValue);
            w.WriteUShort(40000);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadUShort());
            Assert.AreEqual(ushort.MaxValue, r.ReadUShort());
            Assert.AreEqual(40000, r.ReadUShort());
        }

        [Test]
        public void Int_RoundTrips_IncludingExtremes()
        {
            NetWriter w = new NetWriter();
            w.WriteInt(0);
            w.WriteInt(int.MinValue);
            w.WriteInt(int.MaxValue);
            w.WriteInt(-987654321);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.ReadInt());
            Assert.AreEqual(int.MinValue, r.ReadInt());
            Assert.AreEqual(int.MaxValue, r.ReadInt());
            Assert.AreEqual(-987654321, r.ReadInt());
        }

        [Test]
        public void UInt_RoundTrips_IncludingExtremes()
        {
            NetWriter w = new NetWriter();
            w.WriteUInt(0u);
            w.WriteUInt(uint.MaxValue);
            w.WriteUInt(3000000000u);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0u, r.ReadUInt());
            Assert.AreEqual(uint.MaxValue, r.ReadUInt());
            Assert.AreEqual(3000000000u, r.ReadUInt());
        }

        [Test]
        public void Long_RoundTrips_IncludingExtremes()
        {
            NetWriter w = new NetWriter();
            w.WriteLong(0L);
            w.WriteLong(long.MinValue);
            w.WriteLong(long.MaxValue);
            w.WriteLong(-1234567890123456789L);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0L, r.ReadLong());
            Assert.AreEqual(long.MinValue, r.ReadLong());
            Assert.AreEqual(long.MaxValue, r.ReadLong());
            Assert.AreEqual(-1234567890123456789L, r.ReadLong());
        }

        [Test]
        public void Float_RoundTrips_IncludingSpecialValues()
        {
            NetWriter w = new NetWriter();
            w.WriteFloat(0f);
            w.WriteFloat(3.1415927f);
            w.WriteFloat(-2.5e20f);
            w.WriteFloat(float.MaxValue);
            w.WriteFloat(float.MinValue);
            w.WriteFloat(float.NaN);
            w.WriteFloat(float.PositiveInfinity);
            w.WriteFloat(float.NegativeInfinity);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0f, r.ReadFloat());
            Assert.AreEqual(3.1415927f, r.ReadFloat());
            Assert.AreEqual(-2.5e20f, r.ReadFloat());
            Assert.AreEqual(float.MaxValue, r.ReadFloat());
            Assert.AreEqual(float.MinValue, r.ReadFloat());
            Assert.IsTrue(float.IsNaN(r.ReadFloat()));
            Assert.IsTrue(float.IsPositiveInfinity(r.ReadFloat()));
            Assert.IsTrue(float.IsNegativeInfinity(r.ReadFloat()));
        }

        // ---- 字符串往返：null / 空 / ASCII / Unicode ----

        [Test]
        public void String_Ascii_RoundTrips()
        {
            NetWriter w = new NetWriter();
            w.WriteString("Hello, EjoyGame!");

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual("Hello, EjoyGame!", r.ReadString());
        }

        [Test]
        public void String_Null_RoundTripsAsNull()
        {
            NetWriter w = new NetWriter();
            w.WriteString(null);

            NetReader r = new NetReader(w.ToArray());
            Assert.IsNull(r.ReadString());
        }

        [Test]
        public void String_Empty_RoundTripsAsEmpty_AndIsDistinctFromNull()
        {
            NetWriter w = new NetWriter();
            w.WriteString(string.Empty);
            w.WriteString(null);

            NetReader r = new NetReader(w.ToArray());
            string empty = r.ReadString();
            string asNull = r.ReadString();

            Assert.IsNotNull(empty);
            Assert.AreEqual(string.Empty, empty);
            Assert.IsNull(asNull);
        }

        [Test]
        public void String_Unicode_RoundTrips()
        {
            // 中文 + emoji（代理对）+ 重音符，验证 UTF-8 编解码无损。
            const string value = "你好，世界 🎮 café ✓";
            NetWriter w = new NetWriter();
            w.WriteString(value);

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(value, r.ReadString());
        }

        [Test]
        public void String_MixedSequence_RoundTrips()
        {
            NetWriter w = new NetWriter();
            w.WriteString("first");
            w.WriteString(null);
            w.WriteString(string.Empty);
            w.WriteString("最后一个 🚀");

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual("first", r.ReadString());
            Assert.IsNull(r.ReadString());
            Assert.AreEqual(string.Empty, r.ReadString());
            Assert.AreEqual("最后一个 🚀", r.ReadString());
        }

        // ---- 字节数组往返：普通 / null / 空 ----

        [Test]
        public void Bytes_RoundTrips()
        {
            byte[] data = { 1, 2, 3, 250, 0, 128 };
            NetWriter w = new NetWriter();
            w.WriteBytes(data);

            NetReader r = new NetReader(w.ToArray());
            byte[] read = r.ReadBytes();
            Assert.AreEqual(data, read);
        }

        [Test]
        public void Bytes_Null_RoundTripsAsNull()
        {
            NetWriter w = new NetWriter();
            w.WriteBytes(null);

            NetReader r = new NetReader(w.ToArray());
            Assert.IsNull(r.ReadBytes());
        }

        [Test]
        public void Bytes_Empty_RoundTripsAsEmpty_AndIsDistinctFromNull()
        {
            NetWriter w = new NetWriter();
            w.WriteBytes(Array.Empty<byte>());
            w.WriteBytes(null);

            NetReader r = new NetReader(w.ToArray());
            byte[] empty = r.ReadBytes();
            byte[] asNull = r.ReadBytes();

            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.Length);
            Assert.IsNull(asNull);
        }

        // ---- 字节序与混合往返 ----

        [Test]
        public void Int_IsWrittenLittleEndian()
        {
            // 0x01020304 => 小端字节序应为 04 03 02 01。
            NetWriter w = new NetWriter();
            w.WriteInt(0x01020304);
            byte[] bytes = w.ToArray();

            Assert.AreEqual(4, bytes.Length);
            Assert.AreEqual(0x04, bytes[0]);
            Assert.AreEqual(0x03, bytes[1]);
            Assert.AreEqual(0x02, bytes[2]);
            Assert.AreEqual(0x01, bytes[3]);
        }

        [Test]
        public void UShort_IsWrittenLittleEndian()
        {
            // 0xABCD => 小端字节序应为 CD AB。
            NetWriter w = new NetWriter();
            w.WriteUShort(0xABCD);
            byte[] bytes = w.ToArray();

            Assert.AreEqual(2, bytes.Length);
            Assert.AreEqual(0xCD, bytes[0]);
            Assert.AreEqual(0xAB, bytes[1]);
        }

        [Test]
        public void HeterogeneousSequence_RoundTrips()
        {
            NetWriter w = new NetWriter();
            w.WriteByte(7);
            w.WriteBool(true);
            w.WriteShort(-300);
            w.WriteUShort(60000);
            w.WriteInt(-1);
            w.WriteUInt(4000000000u);
            w.WriteLong(9000000000L);
            w.WriteFloat(1.5f);
            w.WriteString("混合 mix");
            w.WriteBytes(new byte[] { 9, 8, 7 });

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(7, r.ReadByte());
            Assert.IsTrue(r.ReadBool());
            Assert.AreEqual(-300, r.ReadShort());
            Assert.AreEqual(60000, r.ReadUShort());
            Assert.AreEqual(-1, r.ReadInt());
            Assert.AreEqual(4000000000u, r.ReadUInt());
            Assert.AreEqual(9000000000L, r.ReadLong());
            Assert.AreEqual(1.5f, r.ReadFloat());
            Assert.AreEqual("混合 mix", r.ReadString());
            Assert.AreEqual(new byte[] { 9, 8, 7 }, r.ReadBytes());
            Assert.AreEqual(0, r.Remaining);
        }

        // ---- Position / Remaining 追踪 ----

        [Test]
        public void Reader_TracksPositionAndRemaining()
        {
            NetWriter w = new NetWriter();
            w.WriteInt(42);   // 4 字节
            w.WriteByte(1);   // 1 字节

            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(0, r.Position);
            Assert.AreEqual(5, r.Remaining);

            r.ReadInt();
            Assert.AreEqual(4, r.Position);
            Assert.AreEqual(1, r.Remaining);

            r.ReadByte();
            Assert.AreEqual(5, r.Position);
            Assert.AreEqual(0, r.Remaining);
        }

        // ---- 越界（underflow）异常 ----

        [Test]
        public void Reader_Underflow_OnInt_Throws()
        {
            // 只有 2 字节，读取 int（需 4 字节）应抛出。
            NetReader r = new NetReader(new byte[] { 1, 2 });
            Assert.Throws<EndOfStreamException>(() => r.ReadInt());
        }

        [Test]
        public void Reader_Underflow_OnByte_FromEmpty_Throws()
        {
            NetReader r = new NetReader(Array.Empty<byte>());
            Assert.Throws<EndOfStreamException>(() => r.ReadByte());
        }

        [Test]
        public void Reader_Underflow_DoesNotAdvancePosition()
        {
            NetReader r = new NetReader(new byte[] { 1, 2 });
            Assert.Throws<EndOfStreamException>(() => r.ReadInt());
            // 抛出前不应推进位置，剩余仍为完整 2 字节。
            Assert.AreEqual(0, r.Position);
            Assert.AreEqual(2, r.Remaining);
        }

        [Test]
        public void Reader_Underflow_OnStringPayload_Throws()
        {
            // 写入声称长度为 5 的字符串前缀，但实际负载不足。
            NetWriter w = new NetWriter();
            w.WriteUShort(5); // 长度前缀
            w.WriteByte((byte)'a');
            w.WriteByte((byte)'b'); // 仅 2 字节负载，少于 5

            NetReader r = new NetReader(w.ToArray());
            Assert.Throws<EndOfStreamException>(() => r.ReadString());
        }

        // ---- ArraySegment 构造 ----

        [Test]
        public void Reader_FromSegment_ReadsOnlyTheWindow()
        {
            // 在前后各加入哨兵字节，验证仅读取中间区间。
            byte[] full = new byte[8];
            full[0] = 0xEE;
            full[1] = 0xEE;
            // 中间 4 字节写入 int 0x01020304（小端）。
            full[2] = 0x04;
            full[3] = 0x03;
            full[4] = 0x02;
            full[5] = 0x01;
            full[6] = 0xFF;
            full[7] = 0xFF;

            NetReader r = new NetReader(new ArraySegment<byte>(full, 2, 4));
            Assert.AreEqual(4, r.Remaining);
            Assert.AreEqual(0x01020304, r.ReadInt());
            Assert.AreEqual(0, r.Remaining);
        }

        [Test]
        public void Writer_AsSegment_MatchesToArray()
        {
            NetWriter w = new NetWriter();
            w.WriteInt(12345);
            w.WriteString("seg");

            ArraySegment<byte> seg = w.AsSegment();
            byte[] arr = w.ToArray();

            Assert.AreEqual(arr.Length, seg.Count);
            for (int i = 0; i < arr.Length; i++)
            {
                Assert.AreEqual(arr[i], seg.Array[seg.Offset + i]);
            }
        }

        // ---- Reset 复用 ----

        [Test]
        public void Writer_Reset_ClearsLengthAndReuses()
        {
            NetWriter w = new NetWriter();
            w.WriteInt(111);
            w.WriteString("first round");
            Assert.Greater(w.Length, 0);

            w.Reset();
            Assert.AreEqual(0, w.Length);

            w.WriteInt(222);
            NetReader r = new NetReader(w.ToArray());
            Assert.AreEqual(222, r.ReadInt());
            Assert.AreEqual(0, r.Remaining);
        }

        [Test]
        public void Writer_Reset_ProducesIdenticalBytesOnReuse()
        {
            NetWriter w = new NetWriter();
            w.WriteInt(0x0A0B0C0D);
            w.WriteString("payload");
            byte[] firstPass = w.ToArray();

            w.Reset();
            w.WriteInt(0x0A0B0C0D);
            w.WriteString("payload");
            byte[] secondPass = w.ToArray();

            Assert.AreEqual(firstPass, secondPass);
        }

        // ---- 缓冲增长（doubling）正确性 ----

        [Test]
        public void Writer_GrowsBeyondInitialCapacity_WithoutCorruption()
        {
            // 初始容量 2，写入远超它的数据，验证扩容后内容仍正确。
            NetWriter w = new NetWriter(2);
            for (int i = 0; i < 500; i++)
            {
                w.WriteInt(i);
            }

            NetReader r = new NetReader(w.ToArray());
            for (int i = 0; i < 500; i++)
            {
                Assert.AreEqual(i, r.ReadInt());
            }

            Assert.AreEqual(0, r.Remaining);
        }
    }
}
