//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    public class TempTextTests
    {
        [SetUp]
        public void SetUp()
        {
            CharBufferPool.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            CharBufferPool.Clear();
        }

        [Test]
        public void Append_String_And_Char_Produces_Expected_Text()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append("HP ");
                t.Append('=');
                t.Append(" 100");
                Assert.AreEqual("HP = 100", t.ToString());
                Assert.AreEqual(8, t.Length);
            }
        }

        [Test]
        public void Append_Null_String_Is_NoOp()
        {
            using (var t = TempText.Rent())
            {
                t.Append((string)null);
                Assert.AreEqual(0, t.Length);
                Assert.AreEqual(string.Empty, t.ToString());
            }
        }

        [Test]
        public void Append_Integers_Formats_Without_Allocation_Path()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append(42).Append('|').Append(-7).Append('|').Append(long.MinValue);
                Assert.AreEqual("42|-7|" + long.MinValue.ToString(), t.ToString());
            }
        }

        [Test]
        public void Append_Unsigned_Integers_Are_Correct()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append(4000000000u).Append('|').Append(ulong.MaxValue);
                Assert.AreEqual("4000000000|" + ulong.MaxValue.ToString(), t.ToString());
            }
        }

        [Test]
        public void Append_Bool_Matches_ToString()
        {
            using (var t = TempText.Rent())
            {
                t.Append(true).Append(',').Append(false);
                Assert.AreEqual("True,False", t.ToString());
            }
        }

        [Test]
        public void Append_Float_With_Format_Is_Correct()
        {
            using (var t = TempText.Rent())
            {
                t.Append(1.26f, "F1");
                Assert.AreEqual("1.3", t.ToString());
            }
        }

        [Test]
        public void Append_Double_With_Format_Is_Correct()
        {
            using (var t = TempText.Rent())
            {
                t.Append(3.14159d, "F2");
                Assert.AreEqual("3.14", t.ToString());
            }
        }

        [Test]
        public void Append_Span_Is_Correct()
        {
            using (var t = TempText.Rent())
            {
                ReadOnlySpan<char> span = "abcdef".AsSpan(1, 3);
                t.Append(span);
                Assert.AreEqual("bcd", t.ToString());
            }
        }

        [Test]
        public void Chaining_Returns_Same_Underlying_State()
        {
            using (var t = TempText.Rent(64))
            {
                var chained = t.Append("a").Append(1).Append('b');
                Assert.AreEqual("a1b", t.ToString());
                Assert.AreEqual(t.Length, chained.Length);
            }
        }

        [Test]
        public void Grows_Beyond_Initial_Capacity_And_Keeps_Content()
        {
            using (var t = TempText.Rent(64))
            {
                int initialCapacity = t.Capacity;
                for (int i = 0; i < 100; i++)
                {
                    t.Append("0123456789");
                }

                Assert.Greater(t.Capacity, initialCapacity);
                Assert.AreEqual(1000, t.Length);
                string s = t.ToString();
                Assert.AreEqual(1000, s.Length);
                StringAssert.StartsWith("0123456789", s);
                StringAssert.EndsWith("0123456789", s);
            }
        }

        [Test]
        public void Grows_When_Number_Does_Not_Fit_Remaining_Space()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append(new string('x', 60));
                int capacity = t.Capacity;
                t.Append(1234567890123456789L);
                Assert.Greater(t.Capacity, capacity);
                StringAssert.EndsWith("1234567890123456789", t.ToString());
                Assert.AreEqual(79, t.Length);
            }
        }

        [Test]
        public void Clear_Resets_Length_But_Keeps_Buffer()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append("hello");
                int capacity = t.Capacity;
                t.Clear();
                Assert.AreEqual(0, t.Length);
                Assert.AreEqual(capacity, t.Capacity);
                t.Append("hi");
                Assert.AreEqual("hi", t.ToString());
            }
        }

        [Test]
        public void CopyTo_Writes_At_Offset()
        {
            char[] dest = new char[8];
            using (var t = TempText.Rent())
            {
                t.Append("abc");
                t.CopyTo(dest, 2);
            }

            Assert.AreEqual('a', dest[2]);
            Assert.AreEqual('b', dest[3]);
            Assert.AreEqual('c', dest[4]);
        }

        [Test]
        public void CopyTo_Throws_When_Dest_Too_Small()
        {
            using (var t = TempText.Rent())
            {
                t.Append("abcd");
                char[] dest = new char[2];
                try
                {
                    t.CopyTo(dest, 0);
                    Assert.Fail("Expected FrameworkException.");
                }
                catch (FrameworkException)
                {
                }
            }
        }

        [Test]
        public void AsSpan_Reflects_Written_Content()
        {
            using (var t = TempText.Rent())
            {
                t.Append("span");
                ReadOnlySpan<char> span = t.AsSpan();
                Assert.AreEqual(4, span.Length);
                Assert.AreEqual('s', span[0]);
                Assert.AreEqual('n', span[3]);
            }
        }

        [Test]
        public void Dispose_Is_Idempotent()
        {
            var t = TempText.Rent(64);
            t.Append("x");
            t.Dispose();
            t.Dispose();
        }

        [Test]
        public void Buffer_Is_Reused_After_Dispose()
        {
            // 通过 TempText 的租借/归还全链路验证复用：Dispose 后缓冲回到池，下一次 Rent 不再新分配。
            Assert.AreEqual(0, CharBufferPool.GetCachedCount(0));

            var t = TempText.Rent(64);
            t.Append("first");
            t.Dispose();
            Assert.AreEqual(1, CharBufferPool.GetCachedCount(0), "Dispose 应把缓冲归还到 64 档。");

            using (var t2 = TempText.Rent(64))
            {
                Assert.AreEqual(0, CharBufferPool.GetCachedCount(0), "再次 Rent 应复用池中缓冲而非新分配。");
                Assert.AreEqual(0, t2.Length);
                Assert.AreEqual(64, t2.Capacity);
                t2.Append("second");
                Assert.AreEqual("second", t2.ToString());
            }

            // 池层面的实例同一性。
            char[] direct = CharBufferPool.Rent(64);
            CharBufferPool.Return(direct);
            Assert.AreSame(direct, CharBufferPool.Rent(64));
        }

        [Test]
        public void Pool_Rounds_Up_To_Tier_Size()
        {
            char[] buffer = CharBufferPool.Rent(100);
            Assert.AreEqual(256, buffer.Length);
            CharBufferPool.Return(buffer);
        }

        [Test]
        public void Pool_Does_Not_Cache_Oversized_Buffers()
        {
            int oversized = CharBufferPool.MaxPooledCapacity + 1;
            char[] buffer = CharBufferPool.Rent(oversized);
            Assert.AreEqual(oversized, buffer.Length);
            CharBufferPool.Return(buffer);
            char[] again = CharBufferPool.Rent(oversized);
            Assert.AreNotSame(buffer, again);
        }

        [Test]
        public void Pool_Caps_Cached_Count_Per_Tier()
        {
            for (int i = 0; i < 16; i++)
            {
                CharBufferPool.Return(new char[64]);
            }

            Assert.AreEqual(4, CharBufferPool.GetCachedCount(0));
        }

        // 以下两项防护（版本号校验）现在是常编译的，不再受 UNITY_EDITOR / DEVELOPMENT_BUILD 限制。
        [Test]
        public void Append_After_Dispose_Throws()
        {
            var t = TempText.Rent(64);
            t.Append("a");
            t.Dispose();

            try
            {
                t.Append("b");
                Assert.Fail("Expected FrameworkException after dispose.");
            }
            catch (FrameworkException)
            {
            }
        }

        [Test]
        public void Stale_Copy_Throws_After_State_Is_Reused()
        {
            var stale = TempText.Rent(64);
            stale.Dispose();

            using (var fresh = TempText.Rent(64))
            {
                fresh.Append("fresh");
                try
                {
                    stale.Append("stale");
                    Assert.Fail("Expected FrameworkException for stale TempText copy.");
                }
                catch (FrameworkException)
                {
                }

                Assert.AreEqual("fresh", fresh.ToString());
            }
        }

        [Test]
        public void Default_Instance_Throws_Instead_Of_NullReference()
        {
            var uninitialized = default(TempText);
            try
            {
                uninitialized.Append("x");
                Assert.Fail("Expected FrameworkException for default(TempText).");
            }
            catch (FrameworkException)
            {
            }
        }

        [Test]
        public void Rent_With_Zero_Or_Negative_Capacity_Still_Works()
        {
            using (var t = TempText.Rent(0))
            {
                Assert.Greater(t.Capacity, 0);
                t.Append("ok").Append(123);
                Assert.AreEqual("ok123", t.ToString());
            }

            using (var t = TempText.Rent(-5))
            {
                Assert.Greater(t.Capacity, 0);
                t.Append('x');
                Assert.AreEqual("x", t.ToString());
            }
        }

        [Test]
        public void Growth_Returns_Old_Buffer_To_Pool()
        {
            Assert.AreEqual(0, CharBufferPool.GetCachedCount(0));

            using (var t = TempText.Rent(64))
            {
                t.Append(new string('a', 200));
                Assert.Greater(t.Capacity, 64);
                Assert.AreEqual(1, CharBufferPool.GetCachedCount(0), "扩容后旧的 64 档缓冲应归还池中。");
            }
        }

        [Test]
        public void Append_Own_Span_Duplicates_Content_Across_Growth()
        {
            using (var t = TempText.Rent(64))
            {
                t.Append(new string('a', 40));
                t.Append(t.AsSpan());
                Assert.AreEqual(80, t.Length);
                Assert.AreEqual(new string('a', 80), t.ToString());
            }
        }

        [Test]
        public void Appending_Does_Not_Allocate_Gc_Memory()
        {
            TestDelegate body = () =>
            {
                using (var t = TempText.Rent(256))
                {
                    t.Append("HP ").Append(100).Append('/').Append(9999L).Append(true).Append(1.5f, "F1");
                    ReadOnlySpan<char> span = t.AsSpan();
                    if (span.Length == 0)
                    {
                        throw new InvalidOperationException("unreachable");
                    }
                }
            };

            // 预热必须作用于"同一个委托实例"：首次调用会触发该 lambda 自身的 JIT 与线程级
            // 缓冲池/状态池的初始化分配。若只预热方法体里另写一遍的等价代码（不同 IL 位置），
            // 那段 JIT 开销仍会落在测量窗口内，导致 GC.Alloc recorder 误报分配。
            for (int i = 0; i < 64; i++)
            {
                body();
            }

            Assert.That(body, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }
    }
}
