//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.AntiCheat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.AntiCheat
{
    /// <summary>
    /// 针对 <see cref="ObscuredBool"/> 的往返、隐式转换、混淆、篡改检测与相等性测试。
    /// </summary>
    [TestFixture]
    public class ObscuredBoolTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void RoundTrip_PreservesValue(bool value)
        {
            ObscuredBool obscured = new ObscuredBool(value);
            Assert.AreEqual(value, obscured.Value);
        }

        [Test]
        public void ImplicitConversion_BothDirections()
        {
            ObscuredBool obscured = true;    // bool -> ObscuredBool
            bool back = obscured;            // ObscuredBool -> bool
            Assert.IsTrue(back);

            ObscuredBool obscuredFalse = false;
            Assert.IsFalse(obscuredFalse);
        }

        [Test]
        public void Obfuscation_HiddenDiffersFromPlain_ForTrue()
        {
            ObscuredBool obscured = new ObscuredBool(true);
            // 密钥非零，明文字节为 1，隐藏字节应偏离 1。
            Assert.AreNotEqual(0, obscured.m_Key);
            Assert.AreNotEqual((byte)1, obscured.m_Hidden);
            Assert.AreEqual((byte)1, (byte)(obscured.m_Hidden ^ obscured.m_Key));
        }

        [Test]
        public void Tamper_FlippingHidden_TripsIsTampered()
        {
            ObscuredBool obscured = new ObscuredBool(true);
            Assert.IsFalse(obscured.IsTampered);

            obscured.m_Hidden = (byte)(obscured.m_Hidden ^ 0x1);
            Assert.IsTrue(obscured.IsTampered);
        }

        [Test]
        public void Default_ReadsAsFalse_AndNotTampered()
        {
            ObscuredBool obscured = default;
            Assert.IsFalse(obscured.Value);
            Assert.IsFalse(obscured.IsTampered);
        }

        [Test]
        public void Setter_ReEncrypts_AndClearsTamper()
        {
            ObscuredBool obscured = new ObscuredBool(false);
            obscured.m_Hidden = (byte)(obscured.m_Hidden ^ 0x1);
            Assert.IsTrue(obscured.IsTampered);

            obscured.Value = true;
            Assert.IsFalse(obscured.IsTampered);
            Assert.IsTrue(obscured.Value);
        }

        [Test]
        public void Equals_And_GetHashCode_Consistency()
        {
            ObscuredBool a = new ObscuredBool(true);
            ObscuredBool b = new ObscuredBool(true);
            ObscuredBool c = new ObscuredBool(false);

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(true.GetHashCode(), a.GetHashCode());
        }
    }
}
