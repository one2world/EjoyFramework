//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// MutableString 行为测试。除少数标注的用例外，全部断言在启用与未启用 EJOY_UNSAFE_STRING 两种编译态下都应通过。
    /// </summary>
    public class MutableStringTests
    {
        [SetUp]
        public void SetUp()
        {
            MutableString.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            MutableString.ClearCache();
        }

        [Test]
        public void Rent_Starts_Empty()
        {
            using (var ms = MutableString.Rent(64))
            {
                Assert.AreEqual(0, ms.Length);
                Assert.AreEqual(0, ms.Value.Length);
                Assert.AreEqual(string.Empty, ms.Value);
            }
        }

        [Test]
        public void Set_Span_Updates_Content_And_Length()
        {
            using (var ms = MutableString.Rent(64))
            {
                ms.Set("Hello".AsSpan());
                Assert.AreEqual(5, ms.Length);
                Assert.AreEqual(5, ms.Value.Length);
                Assert.AreEqual("Hello", ms.Value);
                Assert.AreEqual("Hello".GetHashCode(), ms.Value.GetHashCode());
            }
        }

        [Test]
        public void Set_Null_String_Yields_Empty()
        {
            using (var ms = MutableString.Rent(16))
            {
                ms.Set("abc");
                ms.Set((string)null);
                Assert.AreEqual(0, ms.Length);
                Assert.AreEqual(string.Empty, ms.Value);
            }
        }

        [Test]
        public void Set_Shorter_Value_Truncates_Correctly()
        {
            using (var ms = MutableString.Rent(64))
            {
                ms.Set("LongerContent");
                ms.Set("ab");
                Assert.AreEqual(2, ms.Length);
                Assert.AreEqual("ab", ms.Value);
                Assert.AreEqual(2, ms.Value.Length);
            }
        }

        [Test]
        public void Clear_Yields_Empty_Value()
        {
            using (var ms = MutableString.Rent(64))
            {
                ms.Set("something");
                ms.Clear();
                Assert.AreEqual(0, ms.Length);
                Assert.AreEqual(string.Empty, ms.Value);
            }
        }

        [Test]
        public void Set_From_TempText_Copies_Written_Part()
        {
            using (var ms = MutableString.Rent(64))
            {
                using (var t = TempText.Rent(32))
                {
                    t.Append("HP ").Append(42).Append('/').Append(100);
                    ms.Set(t);
                }

                Assert.AreEqual("HP 42/100", ms.Value);
            }
        }

        [Test]
        public void Value_Can_Be_Used_As_Dictionary_Lookup_Key()
        {
            var dict = new System.Collections.Generic.Dictionary<string, int>
            {
                { "alpha", 1 },
                { "beta", 2 },
            };

            using (var ms = MutableString.Rent(64))
            {
                ms.Set("beta");
                Assert.IsTrue(dict.TryGetValue(ms.Value, out int value));
                Assert.AreEqual(2, value);

                ms.Set("alpha");
                Assert.IsTrue(dict.TryGetValue(ms.Value, out value));
                Assert.AreEqual(1, value);

                ms.Set("gamma");
                Assert.IsFalse(dict.TryGetValue(ms.Value, out value));
            }
        }

        [Test]
        public void Oversized_Value_Still_Produces_Correct_Content()
        {
            int oversize = MutableString.MaxPooledCapacity + 137;
            string expected = new string('x', oversize);
            using (var ms = MutableString.Rent(64))
            {
                ms.Set(expected);
                Assert.AreEqual(oversize, ms.Length);
                Assert.AreEqual(expected, ms.Value);
            }
        }

        [Test]
        public void Rent_With_Oversized_Capacity_Works()
        {
            using (var ms = MutableString.Rent(MutableString.MaxPooledCapacity * 4))
            {
                ms.Set("still works");
                Assert.AreEqual("still works", ms.Value);
            }
        }

        [Test]
        public void Growing_Across_Tiers_Keeps_Content_Correct()
        {
            using (var ms = MutableString.Rent(64))
            {
                for (int length = 1; length <= 600; length += 97)
                {
                    string expected = new string('a', length);
                    ms.Set(expected);
                    Assert.AreEqual(length, ms.Length, "Length mismatch at {0}.", length);
                    Assert.AreEqual(expected, ms.Value, "Content mismatch at {0}.", length);
                }
            }
        }

        [Test]
        public void ToString_Returns_Independent_Copy()
        {
            string snapshot;
            using (var ms = MutableString.Rent(64))
            {
                ms.Set("snapshot");
                snapshot = ms.ToString();
                ms.Set("overwritten");
                Assert.AreEqual("snapshot", snapshot);
            }

            Assert.AreEqual("snapshot", snapshot);
        }

        [Test]
        public void Dispose_Is_Idempotent()
        {
            var ms = MutableString.Rent(64);
            ms.Set("abc");
            ms.Dispose();
            Assert.DoesNotThrow(() => ms.Dispose());
        }

        [Test]
        public void Default_Instance_Dispose_Does_Not_Throw()
        {
            var ms = default(MutableString);
            Assert.DoesNotThrow(() => ms.Dispose());
        }

        [Test]
        public void Sequential_Rents_Reuse_The_Cached_Instance()
        {
            // 归还后再租借应当拿回同一批实例；此处只验证内容正确性，实例复用由下面的 unsafe 专属用例断言。
            for (int i = 0; i < 8; i++)
            {
                using (var ms = MutableString.Rent(64))
                {
                    ms.Set("round");
                    Assert.AreEqual("round", ms.Value);
                }
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Test]
        public void Use_After_Dispose_Throws()
        {
            var ms = MutableString.Rent(64);
            ms.Set("abc");
            ms.Dispose();

            Assert.Throws<FrameworkException>(() => { var _ = ms.Value; });
            Assert.Throws<FrameworkException>(() => { var _ = ms.Length; });
            Assert.Throws<FrameworkException>(() => ms.Set("def"));
        }

        [Test]
        public void Default_Instance_Access_Throws()
        {
            var ms = default(MutableString);
            Assert.Throws<FrameworkException>(() => { var _ = ms.Value; });
        }
#endif

        /// <summary>
        /// unsafe 专属用例的统一前置：能力未启用时标记为 Ignore 而不是编译掉，
        /// 这样"这些断言当前没有在跑"在 Test Runner 里是可见的。
        /// </summary>
        private static void RequireUnsafe()
        {
            if (!MutableString.IsUnsafeEnabled)
            {
                Assert.Ignore(
                    "Zero-allocation path is disabled: define EJOY_UNSAFE_STRING in Player Settings "
                    + "and run on Unity Mono/IL2CPP to exercise these assertions.");
            }
        }

        [Test]
        public void Capability_Flags_Are_Consistent()
        {
            // 无论开关如何，Verify() 与 IsUnsafeEnabled 必须给出一致的结论。
            Assert.AreEqual(MutableString.IsUnsafeEnabled, MutableString.Verify());
        }

        [Test]
        public void Layout_SelfCheck_Passes()
        {
            RequireUnsafe();
            Assert.IsTrue(MutableString.Verify(), "String layout self-check failed on this runtime.");
        }

        [Test]
        public void Same_Instance_Is_Reused_Across_Rents()
        {
            RequireUnsafe();

            object first;
            using (var ms = MutableString.Rent(64))
            {
                Assert.IsTrue(ms.IsPooled);
                ms.Set("first");
                first = ms.Value;
            }

            using (var ms = MutableString.Rent(64))
            {
                Assert.IsTrue(ms.IsPooled);
                ms.Set("second");
                Assert.AreSame(first, ms.Value, "Pooled string instance was not reused.");
                Assert.AreEqual("second", ms.Value);
            }
        }

        [Test]
        public void Returned_Instance_Is_Restored_To_Full_Capacity()
        {
            RequireUnsafe();

            string borrowed;
            int capacity;
            using (var ms = MutableString.Rent(64))
            {
                capacity = ms.Capacity;
                ms.Set("short");
                borrowed = ms.Value;
                Assert.AreEqual(5, borrowed.Length);
            }

            // 归还后池化实例回到"满容量"静息状态，残留引用看到的仍是自洽对象。
            Assert.AreEqual(capacity, borrowed.Length);
        }

        [Test]
        public void Oversized_Value_Falls_Back_To_Allocation()
        {
            RequireUnsafe();

            using (var ms = MutableString.Rent(64))
            {
                ms.Set(new string('y', MutableString.MaxPooledCapacity + 1));
                Assert.IsFalse(ms.IsPooled);
            }
        }

        [Test]
        public void Degraded_Instance_Upgrades_Back_To_Pooled()
        {
            RequireUnsafe();

            using (var ms = MutableString.Rent(64))
            {
                ms.Set(new string('y', MutableString.MaxPooledCapacity + 1));
                Assert.IsFalse(ms.IsPooled);

                ms.Set("small");
                Assert.IsTrue(ms.IsPooled);
                Assert.AreEqual("small", ms.Value);
            }
        }

        [Test]
        public void Survives_Garbage_Collection_While_Borrowed()
        {
            RequireUnsafe();

            // 改写 length 的对象在移动式 GC 下会导致堆遍历错位；本用例确认当前运行时的 GC
            // 在租借窗口内收集是安全的。若这里崩溃或断言失败，说明编译期闸门放行了不该放行的运行时。
            using (var ms = MutableString.Rent(64))
            {
                ms.Set("survives");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Assert.AreEqual("survives", ms.Value);
                Assert.AreEqual(8, ms.Length);
            }
        }
    }
}
