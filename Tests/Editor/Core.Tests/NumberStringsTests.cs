//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Globalization;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：NumberStrings —— 缓存范围内返回共享实例（零分配）、范围外照常生成、调整范围与参数校验。
    /// </summary>
    public class NumberStringsTests
    {
        [TearDown]
        public void TearDown()
        {
            NumberStrings.SetCachedRange(NumberStrings.DefaultMin, NumberStrings.DefaultMax);
        }

        [Test]
        public void Get_ReturnsInvariantText_AndSharesCachedInstances()
        {
            Assert.AreEqual("5", NumberStrings.Get(5));
            Assert.AreSame(NumberStrings.Get(5), NumberStrings.Get(5));
            Assert.AreSame(NumberStrings.Get(1023), NumberStrings.Get(1023L));
            Assert.AreEqual("-1", NumberStrings.Get(-1));
            Assert.AreEqual("123456", NumberStrings.Get(123456));
            Assert.AreNotSame(NumberStrings.Get(123456), NumberStrings.Get(123456), "values outside the range are not cached");
            Assert.AreEqual("2147483648", NumberStrings.Get(2147483648L));
            Assert.AreEqual(int.MinValue.ToString(CultureInfo.InvariantCulture), NumberStrings.Get(int.MinValue));
            Assert.AreEqual(long.MinValue.ToString(CultureInfo.InvariantCulture), NumberStrings.Get(long.MinValue));
        }

        [Test]
        public void SetCachedRange_ChangesRangeAndValidates()
        {
            NumberStrings.SetCachedRange(-100, 100);
            Assert.AreEqual(-100, NumberStrings.CachedMin);
            Assert.AreEqual(100, NumberStrings.CachedMax);
            Assert.AreEqual("-100", NumberStrings.Get(-100));
            Assert.AreSame(NumberStrings.Get(-100), NumberStrings.Get(-100));

            NumberStrings.SetCachedRange(int.MaxValue - 1, int.MaxValue);
            Assert.AreSame(NumberStrings.Get(int.MaxValue), NumberStrings.Get(int.MaxValue));
            Assert.AreEqual("0", NumberStrings.Get(0), "outside the new range still formats correctly");

            Assert.Throws<FrameworkException>(() => NumberStrings.SetCachedRange(10, 9));
            Assert.Throws<FrameworkException>(() => NumberStrings.SetCachedRange(0, NumberStrings.MaxRangeSize));
            Assert.Throws<FrameworkException>(() => NumberStrings.SetCachedRange(int.MinValue, int.MaxValue));
        }

        [Test]
        public void Get_CachedValue_IsAllocationFree()
        {
            string sink = null;
            ZeroAlloc.Assert(() =>
            {
                sink = NumberStrings.Get(42);
                sink = NumberStrings.Get(7L);
            });
            Assert.AreEqual("7", sink);
        }
    }
}
