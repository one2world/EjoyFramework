//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：TempText 复合格式（AppendFormat / TryAppendFormat）、泛型追加、自定义格式化器与零分配。
    /// 合法格式串的期望输出取自同一运行时的 string.Format(InvariantCulture, ...)，逐字对拍。
    /// </summary>
    public class TempTextFormatTests
    {
        private enum Fruit
        {
            Apple,
            Banana,
            Cherry = 7,
        }

        [Flags]
        private enum Access
        {
            None = 0,
            Read = 1,
            Write = 2,
        }

        private struct Vec2
        {
            public float X;
            public float Y;

            public Vec2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public override string ToString()
            {
                return "Vec2(" + X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private struct Money : IFormattable
        {
            public decimal Amount;

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return "$" + Amount.ToString(format, formatProvider);
            }

            public override string ToString()
            {
                return ToString(null, CultureInfo.InvariantCulture);
            }
        }

        private sealed class Vec2Formatter : ITextFormatter<Vec2>
        {
            public bool TryFormat(Vec2 value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format)
            {
                charsWritten = 0;
                int x, y;
                if (destination.Length < 1)
                {
                    return false;
                }

                destination[0] = '(';
                if (!value.X.TryFormat(destination.Slice(1), out x, format, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                int pos = 1 + x;
                if (destination.Length < pos + 2)
                {
                    return false;
                }

                destination[pos++] = ',';
                destination[pos++] = ' ';
                if (!value.Y.TryFormat(destination.Slice(pos), out y, format, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                pos += y;
                if (destination.Length < pos + 1)
                {
                    return false;
                }

                destination[pos++] = ')';
                charsWritten = pos;
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            CharBufferPool.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TextFormatter.Register<Vec2>(null);
            CharBufferPool.Clear();
        }

        [Test]
        public void AppendFormat_MatchesStringFormatInvariant()
        {
            AssertSame("{0}/{1}", 30, 100);
            AssertSame("{0,5}|{1,-5}|", 42, "ab");
            AssertSame("{0:F2} {1:F1}", 3.14159, 2.25f);
            AssertSame("{0:D4}|{0:X}|{0:x8}", 255);
            AssertSame("{0,8:F1}!", -12.345);
            AssertSame("{{literal}} {0}", 7);
            AssertSame("}}{0}{{", 'c');
            AssertSame("{1} {0} {1}", "a", "b");
            AssertSame("{0:N0}", 1234567);
            AssertSame("{0} {1}", true, long.MinValue);
            AssertSame("{0:yyyy-MM-dd HH:mm}", new DateTime(2026, 9, 24, 13, 5, 0));
            AssertSame("{0:c}", new TimeSpan(1, 2, 3));
            AssertSame("{0 ,4}|{1, -3}|", 1, 2);
            AssertSame("{0} {1}", Fruit.Banana, (Fruit)42);
            AssertSame("{0}", Access.Read | Access.Write);
            AssertSame("{0:D}", Fruit.Cherry);
            AssertSame("[{0}]", string.Empty);
            AssertSame("[{0}]", (string)null);
            AssertSame("[{0}]", (object)null);
            AssertSame("{0,300}|", 1);
            AssertSame("{0:F2}", new Money { Amount = 3.5m });
            AssertSame("{0}{1}{2}{3}{4}{5}", 1, 2u, 3L, 4.5f, 'x', "six");
        }

        [Test]
        public void AppendFormat_InvalidFormat_ThrowsAndRollsBack()
        {
            string[] invalid = { "{", "}", "{0", "{a}", "{1}", "{0,}", "{0,-}", "{0:{}", "x{0}y}", "{-1}", "{0,99999}" };
            foreach (string format in invalid)
            {
                using (var t = TempText.Rent())
                {
                    t.Append("keep");
                    try
                    {
                        t.AppendFormat(format, 5);
                        Assert.Fail("expected FrameworkException for \"" + format + "\"");
                    }
                    catch (FrameworkException)
                    {
                    }

                    Assert.AreEqual("keep", t.ToString(), "AppendFormat must roll back \"" + format + "\"");
                    Assert.IsFalse(t.TryAppendFormat(format, 5), format);
                    Assert.AreEqual("keep", t.ToString(), "TryAppendFormat must roll back \"" + format + "\"");
                }
            }

            using (var t = TempText.Rent())
            {
                Assert.IsFalse(t.TryAppendFormat(null, 1));
                Assert.AreEqual(0, t.Length);
            }
        }

        [Test]
        public void AppendFormat_InvalidSpecifier_WrapsFormatExceptionAndRollsBack()
        {
            using (var t = TempText.Rent())
            {
                t.Append("keep");
                try
                {
                    t.AppendFormat("a{0:Q}b", 5);
                    Assert.Fail("expected FrameworkException");
                }
                catch (FrameworkException e)
                {
                    Assert.IsInstanceOf<FormatException>(e.InnerException);
                }

                Assert.AreEqual("keep", t.ToString());
                Assert.IsFalse(t.TryAppendFormat("a{0:Q}b", 5));
                Assert.AreEqual("keep", t.ToString());
            }
        }

        [Test]
        public void Append_Generic_EnumCharArrayAndRepeat()
        {
            using (var t = TempText.Rent())
            {
                t.Append(Fruit.Banana).Append(' ').Append((Fruit)42).Append(' ').Append(Access.Read | Access.Write);
                t.Append(' ').Append(new[] { 'a', 'b' }).Append(' ').Append('-', 3).Append((char[])null).Append('x', 0);
                t.Append(' ').Append(Fruit.Cherry, "D");
                Assert.AreEqual("Banana 42 Read, Write ab --- 7", t.ToString());

                try
                {
                    t.Append('x', -1);
                    Assert.Fail("expected FrameworkException for a negative repeat count");
                }
                catch (FrameworkException)
                {
                }
            }
        }

        [Test]
        public void Register_CustomFormatter_ReplacesToStringFallback()
        {
            Assert.IsFalse(TextFormatter.IsAllocationFree<Vec2>());
            using (var t = TempText.Rent())
            {
                t.Append(new Vec2(1f, 2.5f));
                Assert.AreEqual("Vec2(1,2.5)", t.ToString(), "unregistered types fall back to ToString()");
            }

            TextFormatter.Register(new Vec2Formatter());
            Assert.IsTrue(TextFormatter.IsAllocationFree<Vec2>());
            using (var t = TempText.Rent(4))
            {
                t.AppendFormat("p={0:F1}", new Vec2(1f, 2.5f));
                Assert.AreEqual("p=(1.0, 2.5)", t.ToString(), "the buffer grows when the formatter reports insufficient space");
            }

            TextFormatter.Register<Vec2>(null);
            Assert.IsFalse(TextFormatter.IsAllocationFree<Vec2>(), "registering null restores the default");
        }

        [Test]
        public void AppendFormat_IsAllocationFree()
        {
            int hp = 30;
            int max = 100;
            float ratio = 0.3f;
            string name = "Hero";
            Fruit fruit = Fruit.Cherry;
            ZeroAlloc.Assert(() =>
            {
                using (var t = TempText.Rent(64))
                {
                    t.AppendFormat("{0}/{1} {2:F1}% {3,-6}|{4}", hp, max, ratio * 100f, name, fruit);
                    t.Append(fruit).Append(' ').AppendCompact(1234567).Append(' ').AppendDuration(3725L).Append(' ').AppendPercent(0.456, 1);
                }
            });
        }

        [Test]
        public void AppendFormat_RegisteredStruct_IsAllocationFree()
        {
            TextFormatter.Register(new Vec2Formatter());
            Vec2 position = new Vec2(3f, 4f);
            ZeroAlloc.Assert(() =>
            {
                using (var t = TempText.Rent(32))
                {
                    t.AppendFormat("pos {0:F1}", position);
                }
            });
        }

        private static void AssertSame<T0>(string format, T0 arg0)
        {
            string expected = string.Format(CultureInfo.InvariantCulture, format, arg0);
            using (var t = TempText.Rent(8))
            {
                t.AppendFormat(format, arg0);
                Assert.AreEqual(expected, t.ToString(), format);
            }
        }

        private static void AssertSame<T0, T1>(string format, T0 arg0, T1 arg1)
        {
            string expected = string.Format(CultureInfo.InvariantCulture, format, arg0, arg1);
            using (var t = TempText.Rent(8))
            {
                t.AppendFormat(format, arg0, arg1);
                Assert.AreEqual(expected, t.ToString(), format);
            }
        }

        private static void AssertSame<T0, T1, T2, T3, T4, T5>(string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            string expected = string.Format(CultureInfo.InvariantCulture, format, arg0, arg1, arg2, arg3, arg4, arg5);
            using (var t = TempText.Rent(8))
            {
                t.AppendFormat(format, arg0, arg1, arg2, arg3, arg4, arg5);
                Assert.AreEqual(expected, t.ToString(), format);
            }
        }
    }
}
