//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.AntiCheat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.AntiCheat
{
    /// <summary>
    /// 针对 <see cref="ObscuredInt"/> 的往返、隐式转换、混淆、篡改检测与相等性测试。
    /// </summary>
    [TestFixture]
    public class ObscuredIntTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-1)]
        [TestCase(12345)]
        [TestCase(-987654)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void RoundTrip_PreservesValue(int value)
        {
            ObscuredInt obscured = new ObscuredInt(value);
            Assert.AreEqual(value, obscured.Value);
        }

        [Test]
        public void ImplicitConversion_BothDirections()
        {
            ObscuredInt obscured = 42;       // int -> ObscuredInt
            int back = obscured;             // ObscuredInt -> int
            Assert.AreEqual(42, back);
        }

        [Test]
        public void ImplicitConversion_EnablesArithmetic()
        {
            ObscuredInt obscured = 10;
            int result = obscured + 5;       // 通过隐式转换为 int 参与算术
            Assert.AreEqual(15, result);
        }

        [Test]
        public void Obfuscation_HiddenDiffersFromPlain_ForNonzeroValue()
        {
            ObscuredInt obscured = new ObscuredInt(123456);
            // 密钥非零，因此隐藏值必然偏离明文。
            Assert.AreNotEqual(0, obscured.m_Key);
            Assert.AreNotEqual(123456, obscured.m_Hidden);
            // 隐藏值 ^ 密钥 应还原明文。
            Assert.AreEqual(123456, obscured.m_Hidden ^ obscured.m_Key);
        }

        [Test]
        public void Tamper_FlippingHidden_TripsIsTampered()
        {
            ObscuredInt obscured = new ObscuredInt(777);
            Assert.IsFalse(obscured.IsTampered);

            // 模拟内存编辑器翻转隐藏字段。
            obscured.m_Hidden = obscured.m_Hidden ^ 0x1;
            Assert.IsTrue(obscured.IsTampered);
        }

        [Test]
        public void Default_ReadsAsZero_AndNotTampered()
        {
            ObscuredInt obscured = default;
            Assert.AreEqual(0, obscured.Value);
            Assert.IsFalse(obscured.IsTampered);
            int back = obscured;
            Assert.AreEqual(0, back);
        }

        [Test]
        public void Default_SetterInitializes_AndRoundTrips()
        {
            ObscuredInt obscured = default;
            obscured.Value = 555;
            Assert.AreEqual(555, obscured.Value);
            Assert.IsFalse(obscured.IsTampered);
        }

        [Test]
        public void Setter_ReEncrypts_AndClearsTamper()
        {
            ObscuredInt obscured = new ObscuredInt(100);
            obscured.m_Hidden = obscured.m_Hidden ^ 0xFF;
            Assert.IsTrue(obscured.IsTampered);

            // 重新写入应刷新蜜罐并清除篡改状态。
            obscured.Value = 200;
            Assert.IsFalse(obscured.IsTampered);
            Assert.AreEqual(200, obscured.Value);
        }

        [Test]
        public void ToString_MatchesPlain()
        {
            ObscuredInt obscured = new ObscuredInt(-42);
            Assert.AreEqual("-42", obscured.ToString());
        }

        [Test]
        public void Equals_And_GetHashCode_Consistency()
        {
            ObscuredInt a = new ObscuredInt(999);
            ObscuredInt b = new ObscuredInt(999);
            ObscuredInt c = new ObscuredInt(1000);

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(c));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreEqual(999.GetHashCode(), a.GetHashCode());

            // 与装箱 int 比较。
            Assert.IsTrue(a.Equals((object)999));
            Assert.IsFalse(a.Equals((object)"999"));
        }
    }
}
