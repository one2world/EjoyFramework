//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.AntiCheat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.AntiCheat
{
    /// <summary>
    /// 针对 <see cref="ObscuredFloat"/> 的往返（含分数/极值/NaN 比特）、隐式转换、混淆、
    /// 篡改检测与相等性测试。普通断言使用 delta，精确比特用例除外。
    /// </summary>
    [TestFixture]
    public class ObscuredFloatTests
    {
        private const float Delta = 1e-5f;

        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(-1f)]
        [TestCase(3.14159f)]
        [TestCase(-2.71828f)]
        [TestCase(0.0001f)]
        [TestCase(123456.789f)]
        public void RoundTrip_PreservesValue(float value)
        {
            ObscuredFloat obscured = new ObscuredFloat(value);
            Assert.AreEqual(value, obscured.Value, Delta);
        }

        [Test]
        public void RoundTrip_Extremes_ExactBits()
        {
            AssertExactRoundTrip(float.MaxValue);
            AssertExactRoundTrip(float.MinValue);
            AssertExactRoundTrip(float.Epsilon);
            AssertExactRoundTrip(float.PositiveInfinity);
            AssertExactRoundTrip(float.NegativeInfinity);
        }

        [Test]
        public void RoundTrip_NaN_PreservesNaN()
        {
            // NaN 的混淆作用于比特位，往返后仍是 NaN（IEEE-754 规定 NaN != NaN，故用 IsNaN 断言）。
            ObscuredFloat obscured = new ObscuredFloat(float.NaN);
            Assert.IsTrue(float.IsNaN(obscured.Value));
            // NaN 实例不应被误报为篡改。
            Assert.IsFalse(obscured.IsTampered);
        }

        [Test]
        public void ImplicitConversion_BothDirections()
        {
            ObscuredFloat obscured = 1.5f;   // float -> ObscuredFloat
            float back = obscured;           // ObscuredFloat -> float
            Assert.AreEqual(1.5f, back, Delta);
        }

        [Test]
        public void ImplicitConversion_EnablesArithmetic()
        {
            ObscuredFloat obscured = 2.5f;
            float result = obscured + 0.5f;
            Assert.AreEqual(3.0f, result, Delta);
        }

        [Test]
        public void Obfuscation_HiddenDiffersFromPlainBits_ForNonzeroValue()
        {
            ObscuredFloat obscured = new ObscuredFloat(3.14159f);
            int plainBits = System.BitConverter.SingleToInt32Bits(3.14159f);
            Assert.AreNotEqual(0, obscured.m_Key);
            Assert.AreNotEqual(plainBits, obscured.m_Hidden);
            Assert.AreEqual(plainBits, obscured.m_Hidden ^ obscured.m_Key);
        }

        [Test]
        public void Tamper_FlippingHidden_TripsIsTampered()
        {
            ObscuredFloat obscured = new ObscuredFloat(9.99f);
            Assert.IsFalse(obscured.IsTampered);

            obscured.m_Hidden = obscured.m_Hidden ^ 0x1;
            Assert.IsTrue(obscured.IsTampered);
        }

        [Test]
        public void Default_ReadsAsZero_AndNotTampered()
        {
            ObscuredFloat obscured = default;
            Assert.AreEqual(0f, obscured.Value, Delta);
            Assert.IsFalse(obscured.IsTampered);
        }

        [Test]
        public void Setter_ReEncrypts_AndClearsTamper()
        {
            ObscuredFloat obscured = new ObscuredFloat(1.0f);
            obscured.m_Hidden = obscured.m_Hidden ^ 0xFF;
            Assert.IsTrue(obscured.IsTampered);

            obscured.Value = 7.5f;
            Assert.IsFalse(obscured.IsTampered);
            Assert.AreEqual(7.5f, obscured.Value, Delta);
        }

        [Test]
        public void Equals_And_GetHashCode_Consistency()
        {
            ObscuredFloat a = new ObscuredFloat(4.25f);
            ObscuredFloat b = new ObscuredFloat(4.25f);
            ObscuredFloat c = new ObscuredFloat(4.26f);

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(4.25f.GetHashCode(), a.GetHashCode());
        }

        private static void AssertExactRoundTrip(float value)
        {
            ObscuredFloat obscured = new ObscuredFloat(value);
            int expectedBits = System.BitConverter.SingleToInt32Bits(value);
            int actualBits = System.BitConverter.SingleToInt32Bits(obscured.Value);
            Assert.AreEqual(expectedBits, actualBits, "比特位应精确往返：" + value);
            Assert.IsFalse(obscured.IsTampered);
        }
    }
}
