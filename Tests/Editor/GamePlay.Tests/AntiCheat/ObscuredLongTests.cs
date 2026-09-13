//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.AntiCheat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.AntiCheat
{
    /// <summary>
    /// 针对 <see cref="ObscuredLong"/> 的往返、隐式转换、混淆、篡改检测与相等性测试。
    /// </summary>
    [TestFixture]
    public class ObscuredLongTests
    {
        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(-1L)]
        [TestCase(9876543210L)]
        [TestCase(-1234567890123L)]
        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void RoundTrip_PreservesValue(long value)
        {
            ObscuredLong obscured = new ObscuredLong(value);
            Assert.AreEqual(value, obscured.Value);
        }

        [Test]
        public void ImplicitConversion_BothDirections()
        {
            ObscuredLong obscured = 9000000000L;  // long -> ObscuredLong
            long back = obscured;                 // ObscuredLong -> long
            Assert.AreEqual(9000000000L, back);
        }

        [Test]
        public void ImplicitConversion_EnablesArithmetic()
        {
            ObscuredLong obscured = 1000L;
            long result = obscured + 24L;
            Assert.AreEqual(1024L, result);
        }

        [Test]
        public void Obfuscation_HiddenDiffersFromPlain_ForNonzeroValue()
        {
            ObscuredLong obscured = new ObscuredLong(123456789012L);
            Assert.AreNotEqual(0L, obscured.m_Key);
            Assert.AreNotEqual(123456789012L, obscured.m_Hidden);
            Assert.AreEqual(123456789012L, obscured.m_Hidden ^ obscured.m_Key);
        }

        [Test]
        public void Tamper_FlippingHidden_TripsIsTampered()
        {
            ObscuredLong obscured = new ObscuredLong(424242L);
            Assert.IsFalse(obscured.IsTampered);

            obscured.m_Hidden = obscured.m_Hidden ^ 0x1L;
            Assert.IsTrue(obscured.IsTampered);
        }

        [Test]
        public void Default_ReadsAsZero_AndNotTampered()
        {
            ObscuredLong obscured = default;
            Assert.AreEqual(0L, obscured.Value);
            Assert.IsFalse(obscured.IsTampered);
        }

        [Test]
        public void Setter_ReEncrypts_AndClearsTamper()
        {
            ObscuredLong obscured = new ObscuredLong(500L);
            obscured.m_Hidden = obscured.m_Hidden ^ 0xFFL;
            Assert.IsTrue(obscured.IsTampered);

            obscured.Value = 600L;
            Assert.IsFalse(obscured.IsTampered);
            Assert.AreEqual(600L, obscured.Value);
        }

        [Test]
        public void Equals_And_GetHashCode_Consistency()
        {
            ObscuredLong a = new ObscuredLong(7777777L);
            ObscuredLong b = new ObscuredLong(7777777L);
            ObscuredLong c = new ObscuredLong(8888888L);

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(7777777L.GetHashCode(), a.GetHashCode());
        }
    }
}
