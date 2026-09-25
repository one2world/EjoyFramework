//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：SpanParse（与 BCL NumberStyles.Integer / Float + InvariantCulture 逐例对拍）、
    /// TextLineEnumerator（与 Split("\r\n","\n","\r") 同语义、同行号）、TextFieldReader，以及解析路径零分配。
    /// </summary>
    public class SpanParseTests
    {
        private static readonly string[] s_IntegerInputs =
        {
            "0", "-0", "+0", "7", "+5", "-5", " 42 ", "\t-7\n", "\r\n13\v", "00012", "-00012",
            "2147483647", "2147483648", "-2147483648", "-2147483649",
            "9223372036854775807", "9223372036854775808", "-9223372036854775808", "-9223372036854775809",
            "99999999999999999999", "", " ", "+", "-", "+-1", "--1", "1 2", "12a", "a12", "1_000",
            "0x10", "1e3", "1.0", ",1", "1,000", "\u0663", "\uFF11", "- 1", "1-",
        };

        private static readonly string[] s_FloatInputs =
        {
            "0", "1.5", "-2.25e3", " 3.14 ", "+.5", "5.", "1e-7", "3.4028235e38", "NaN", "Infinity", "-Infinity",
            "", "abc", "1,5", "1.5.2", "0x1", "1e", "--1",
        };

        [Test]
        public void TryParseInt32_MatchesBcl()
        {
            foreach (string s in IntegerInputs())
            {
                int expected;
                bool expectedOk = int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out expected);
                int actual;
                bool actualOk = SpanParse.TryParseInt32(s.AsSpan(), out actual);
                Assert.AreEqual(expectedOk, actualOk, Describe(s));
                Assert.AreEqual(expected, actual, Describe(s));
            }
        }

        [Test]
        public void TryParseInt64_MatchesBcl()
        {
            foreach (string s in IntegerInputs())
            {
                long expected;
                bool expectedOk = long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out expected);
                long actual;
                bool actualOk = SpanParse.TryParseInt64(s.AsSpan(), out actual);
                Assert.AreEqual(expectedOk, actualOk, Describe(s));
                Assert.AreEqual(expected, actual, Describe(s));
            }
        }

        [Test]
        public void TryParseFloatingPoint_MatchesBcl()
        {
            foreach (string s in s_FloatInputs)
            {
                float expectedSingle;
                bool expectedSingleOk = float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out expectedSingle);
                float actualSingle;
                Assert.AreEqual(expectedSingleOk, SpanParse.TryParseSingle(s.AsSpan(), out actualSingle), Describe(s));
                Assert.AreEqual(expectedSingle, actualSingle, Describe(s));

                double expectedDouble;
                bool expectedDoubleOk = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out expectedDouble);
                double actualDouble;
                Assert.AreEqual(expectedDoubleOk, SpanParse.TryParseDouble(s.AsSpan(), out actualDouble), Describe(s));
                Assert.AreEqual(expectedDouble, actualDouble, Describe(s));
            }
        }

        [Test]
        public void TryParseBoolean_IsLenient()
        {
            string[] truthy = { "1", "true", "TRUE", "True", " yes ", "Yes", "\ttrue\n" };
            string[] falsy = { "0", "false", "FALSE", "no", " No " };
            string[] invalid = { "", " ", "2", "y", "n", "t", "maybe", "truee", "1 1" };
            bool value;
            foreach (string s in truthy)
            {
                Assert.IsTrue(SpanParse.TryParseBoolean(s.AsSpan(), out value), Describe(s));
                Assert.IsTrue(value, Describe(s));
            }

            foreach (string s in falsy)
            {
                Assert.IsTrue(SpanParse.TryParseBoolean(s.AsSpan(), out value), Describe(s));
                Assert.IsFalse(value, Describe(s));
            }

            foreach (string s in invalid)
            {
                Assert.IsFalse(SpanParse.TryParseBoolean(s.AsSpan(), out value), Describe(s));
                Assert.IsFalse(value, Describe(s));
            }
        }

        [Test]
        public void TextLineEnumerator_MatchesSplitSemantics()
        {
            string[] samples = { "", "a", "a\n", "\n", "a\r\nb\rc\nd", "\r\n\r\n", "a\r", "\r\r\n\n", "x\n\ny", "\r" };
            string[] separators = { "\r\n", "\n", "\r" };
            foreach (string s in samples)
            {
                string[] expected = s.Split(separators, StringSplitOptions.None);
                var actual = new List<string>();
                var lines = new TextLineEnumerator(s.AsSpan());
                while (lines.MoveNext())
                {
                    actual.Add(lines.Current.ToString());
                    Assert.AreEqual(actual.Count, lines.LineNumber, Describe(s));
                }

                Assert.IsFalse(lines.MoveNext(), "stays finished");
                CollectionAssert.AreEqual(expected, actual, Describe(s));

                int foreachCount = 0;
                foreach (ReadOnlySpan<char> line in new TextLineEnumerator(s.AsSpan()))
                {
                    foreachCount += line.Length >= 0 ? 1 : 0;
                }

                Assert.AreEqual(expected.Length, foreachCount, "foreach pattern " + Describe(s));
            }
        }

        [Test]
        public void TextFieldReader_ReadsTypedFieldsInOrder()
        {
            var reader = new TextFieldReader("7\tHero\t2.5\tyes\t9000000000\t\t-1".AsSpan());
            int id;
            string name;
            float speed;
            bool enabled;
            long big;
            string empty;
            double last;
            Assert.IsTrue(reader.TryReadInt32(out id));
            Assert.IsTrue(reader.TryReadString(out name));
            Assert.IsTrue(reader.TryReadSingle(out speed));
            Assert.IsTrue(reader.TryReadBoolean(out enabled));
            Assert.IsTrue(reader.TryReadInt64(out big));
            Assert.IsTrue(reader.HasMore);
            Assert.IsTrue(reader.TryReadString(out empty));
            Assert.IsTrue(reader.TryReadDouble(out last));
            Assert.AreEqual(7, id);
            Assert.AreEqual("Hero", name);
            Assert.AreEqual(2.5f, speed);
            Assert.IsTrue(enabled);
            Assert.AreEqual(9000000000L, big);
            Assert.AreSame(string.Empty, empty);
            Assert.AreEqual(-1.0, last);
            Assert.IsFalse(reader.HasMore);
            Assert.AreEqual(7, reader.FieldIndex);
            Assert.IsFalse(reader.TryReadInt32(out id), "no more fields");
            Assert.AreEqual(0, id);
        }

        [Test]
        public void TextFieldReader_CountSkipAndFailures()
        {
            Assert.AreEqual(1, TextFieldReader.CountFields(string.Empty.AsSpan(), '\t'));
            Assert.AreEqual(3, TextFieldReader.CountFields("a,b,".AsSpan(), ','));

            var reader = new TextFieldReader("a,b,c".AsSpan(), ',');
            Assert.IsTrue(reader.TrySkip(2));
            string c;
            Assert.IsTrue(reader.TryReadString(out c));
            Assert.AreEqual("c", c);
            Assert.IsFalse(reader.TrySkip());

            var empty = new TextFieldReader(string.Empty.AsSpan());
            ReadOnlySpan<char> field;
            Assert.IsTrue(empty.TryReadField(out field), "an empty line is one empty field, like Split");
            Assert.AreEqual(0, field.Length);
            Assert.IsFalse(empty.TryReadField(out field));

            var bad = new TextFieldReader("x\t1".AsSpan());
            int value;
            Assert.IsFalse(bad.TryReadInt32(out value), "'x' is not an integer");
            Assert.AreEqual(0, value);
        }

        [Test]
        public void Parsing_IsAllocationFree()
        {
            string text = "1\t2.5\ttrue\n-7\t0.125\tno\r\n42\t1e3\t1";
            long sum = 0;
            ZeroAlloc.Assert(() =>
            {
                var lines = new TextLineEnumerator(text.AsSpan());
                while (lines.MoveNext())
                {
                    var reader = new TextFieldReader(lines.Current);
                    int i;
                    float f;
                    bool b;
                    if (reader.TryReadInt32(out i) && reader.TryReadSingle(out f) && reader.TryReadBoolean(out b))
                    {
                        sum += i + (long)f + (b ? 1 : 0);
                    }
                }

                long l;
                double d;
                if (SpanParse.TryParseInt64(" -9223372036854775808 ".AsSpan(), out l) && SpanParse.TryParseDouble("6.02e23".AsSpan(), out d))
                {
                    sum += l == long.MinValue && d > 0 ? 1 : 0;
                }
            });
            Assert.AreNotEqual(0L, sum);
        }

        private static IEnumerable<string> IntegerInputs()
        {
            foreach (string s in s_IntegerInputs)
            {
                yield return s;
            }

            var random = new Random(20260924);
            var bytes = new byte[8];
            const string alphabet = "0123456789 +-\t";
            for (int n = 0; n < 1000; n++)
            {
                random.NextBytes(bytes);
                long value = BitConverter.ToInt64(bytes, 0);
                yield return (n & 1) == 0 ? value.ToString(CultureInfo.InvariantCulture) : ((int)value).ToString(CultureInfo.InvariantCulture);

                var sb = new StringBuilder();
                int length = random.Next(0, 22);
                for (int i = 0; i < length; i++)
                {
                    sb.Append(alphabet[random.Next(alphabet.Length)]);
                }

                yield return sb.ToString();
            }
        }

        private static string Describe(string s)
        {
            var sb = new StringBuilder("'");
            foreach (char c in s)
            {
                if (c >= 0x20 && c < 0x7F)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            return sb.Append('\'').ToString();
        }
    }
}
