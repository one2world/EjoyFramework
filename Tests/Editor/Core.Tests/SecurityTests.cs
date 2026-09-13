//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Security;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class SecurityTests
    {
        // ===== SecureInt =====

        [Test]
        public void SecureInt_RoundTrip()
        {
            SecureInt v = 12345;
            Assert.AreEqual(12345, v.Value);
            v.Value = 67890;
            Assert.AreEqual(67890, v.Value);
        }

        [Test]
        public void SecureInt_ImplicitConversion()
        {
            SecureInt v = new SecureInt(42);
            int extracted = v;
            Assert.AreEqual(42, extracted);
        }

        [Test]
        public void SecureFloat_RoundTrip()
        {
            SecureFloat v = 3.14159f;
            Assert.AreEqual(3.14159f, v.Value, 0.00001f);
        }

        [Test]
        public void SecureInt_Rekey_PreservesValue()
        {
            var v = new SecureInt(999);
            v.Rekey();
            Assert.AreEqual(999, v.Value);
        }

        [Test]
        public void SecureInt_Default_ReadsAsZero()
        {
            // default(SecureInt)（含未初始化字段、new SecureInt[n]）必须读作 0，
            // 而非把 ProcessSalt 当成垃圾明文。
            SecureInt def = default;
            Assert.AreEqual(0, def.Value);
            var arr = new SecureInt[3];
            Assert.AreEqual(0, arr[0].Value);
            Assert.AreEqual(0, arr[2].Value);
        }

        [Test]
        public void SecureInt_ExplicitZero_RoundTrips()
        {
            var v = new SecureInt(0);
            Assert.AreEqual(0, v.Value);
        }

        [Test]
        public void SecureInt_WriteToDefault_LazilyKeysAndRoundTrips()
        {
            SecureInt v = default;
            v.Value = 777;
            Assert.AreEqual(777, v.Value);
        }

        [Test]
        public void SecureInt_RekeyOnDefault_StaysZero()
        {
            SecureInt v = default;
            v.Rekey();
            Assert.AreEqual(0, v.Value);
        }

        [Test]
        public void SecureFloat_Default_ReadsAsZero()
        {
            SecureFloat def = default;
            Assert.AreEqual(0f, def.Value, 0f);
            var arr = new SecureFloat[2];
            Assert.AreEqual(0f, arr[1].Value, 0f);
        }

        [Test]
        public void SecureFloat_WriteToDefault_LazilyKeysAndRoundTrips()
        {
            SecureFloat v = default;
            v.Value = 2.5f;
            Assert.AreEqual(2.5f, v.Value, 0.00001f);
        }

        // ===== MessageReplayDetector =====

        [Test]
        public void Replay_AcceptsNewSequences()
        {
            var d = new MessageReplayDetector(16);
            Assert.IsTrue(d.Accept(1));
            Assert.IsTrue(d.Accept(2));
            Assert.IsTrue(d.Accept(3));
        }

        [Test]
        public void Replay_RejectsDuplicate()
        {
            var d = new MessageReplayDetector(16);
            d.Accept(5);
            Assert.IsFalse(d.Accept(5));
            Assert.AreEqual(1, d.RejectedReplayCount);
        }

        [Test]
        public void Replay_AcceptsOutOfOrder_WithinWindow()
        {
            var d = new MessageReplayDetector(16);
            Assert.IsTrue(d.Accept(10));
            Assert.IsTrue(d.Accept(5));   // 乱序但窗口内仍接受
            Assert.IsTrue(d.Accept(8));
        }

        [Test]
        public void Replay_RejectsTooOld_AfterWindowSlide()
        {
            var d = new MessageReplayDetector(16);
            d.Accept(1);
            d.Accept(100);   // 窗口推进到 [85, 100]
            Assert.IsFalse(d.Accept(1), "1 远小于 base，应被拒");
            Assert.AreEqual(1, d.RejectedTooOldCount);
        }

        [Test]
        public void Replay_WindowSize_TooSmall_Throws()
        {
            Assert.Throws<FrameworkException>(() => new MessageReplayDetector(4));
        }

        // ===== IntegrityCheck =====

        [Test]
        public void Crc32_OfKnownString_MatchesStandard()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
            // Standard CRC32 IEEE for "123456789" = 0xCBF43926
            Assert.AreEqual(0xCBF43926u, IntegrityCheck.Crc32(data));
        }

        [Test]
        public void Crc32_DifferentInputs_DifferentCrc()
        {
            byte[] a = { 1, 2, 3 };
            byte[] b = { 1, 2, 4 };
            Assert.AreNotEqual(IntegrityCheck.Crc32(a), IntegrityCheck.Crc32(b));
        }

        [Test]
        public void Verify_True_OnExpectedCrc()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("hello");
            uint crc = IntegrityCheck.Crc32(data);
            Assert.IsTrue(IntegrityCheck.Verify(data, crc));
        }

        [Test]
        public void Verify_False_OnTamperedData()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("hello");
            uint crc = IntegrityCheck.Crc32(data);
            data[0] = (byte)'H';   // 改字节
            Assert.IsFalse(IntegrityCheck.Verify(data, crc));
        }
    }
}
