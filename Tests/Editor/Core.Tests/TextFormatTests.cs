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
    /// WS5-M1：TextFormat —— 紧凑数字（截断不进位）、时长四种样式与取整、百分比（未满 1 不显示 100%）、参数校验与零分配。
    /// 中文单位用 \u 转义书写，避免源文件编码问题改变测试向量。
    /// </summary>
    public class TextFormatTests
    {
        private const string Wan = "\u4E07";      // wan (10^4)
        private const string Yi = "\u4EBF";       // yi (10^8)
        private const string Day = "\u5929";      // day
        private const string Hour = "\u5C0F\u65F6"; // hour
        private const string Minute = "\u5206";   // minute
        private const string Second = "\u79D2";   // second

        [Test]
        public void Compact_Western()
        {
            Assert.AreEqual("0", Compact(0));
            Assert.AreEqual("999", Compact(999));
            Assert.AreEqual("1K", Compact(1000));
            Assert.AreEqual("1K", Compact(1050));
            Assert.AreEqual("1.5K", Compact(1500));
            Assert.AreEqual("1.9K", Compact(1999), "truncates instead of rounding up");
            Assert.AreEqual("12.3K", Compact(12345));
            Assert.AreEqual("999.9K", Compact(999999));
            Assert.AreEqual("1M", Compact(1000000));
            Assert.AreEqual("1.2B", Compact(1234567890));
            Assert.AreEqual("1T", Compact(1000000000000));
            Assert.AreEqual("-1.5K", Compact(-1500));
            Assert.AreEqual("9223372T", Compact(long.MaxValue));
            Assert.AreEqual("-9223372T", Compact(long.MinValue));
        }

        [Test]
        public void Compact_Chinese()
        {
            CompactNumberFormat zh = CompactNumberFormat.Chinese;
            Assert.AreEqual("9999", Compact(9999, zh));
            Assert.AreEqual("1" + Wan, Compact(10000, zh));
            Assert.AreEqual("1.2" + Wan, Compact(12345, zh));
            Assert.AreEqual("1.2" + Yi, Compact(123456789, zh));
            Assert.AreEqual("1" + Wan + Yi, Compact(1000000000000, zh));
        }

        [Test]
        public void Compact_Options()
        {
            CompactNumberFormat significant = CompactNumberFormat.Western.WithDecimals(2, 3);
            Assert.AreEqual("1.23K", Compact(1234, significant));
            Assert.AreEqual("12.3K", Compact(12345, significant));
            Assert.AreEqual("123K", Compact(123456, significant));
            Assert.AreEqual("1K", Compact(1000, significant));

            Assert.AreEqual("1.0K", Compact(1000, CompactNumberFormat.Western.WithTrimTrailingZeros(false)));

            CompactNumberFormat highThreshold = CompactNumberFormat.Western.WithThreshold(100000);
            Assert.AreEqual("99999", Compact(99999, highThreshold));
            Assert.AreEqual("100K", Compact(100000, highThreshold));

            CompactNumberFormat grouped = CompactNumberFormat.Western.WithThreshold(1000000).WithSeparators('.', ',');
            Assert.AreEqual("999,999", Compact(999999, grouped));
            Assert.AreEqual("-999,999", Compact(-999999, grouped));
            Assert.AreEqual("1.2M", Compact(1234567, grouped));
            Assert.AreEqual("1,234.5T", Compact(1234567890123456, grouped));

            Assert.AreEqual("1,5K", Compact(1500, CompactNumberFormat.Western.WithSeparators(',', '.')));
            Assert.AreNotSame(CompactNumberFormat.Western, grouped, "With* returns a new instance");
            Assert.AreEqual(1000, CompactNumberFormat.Western.Threshold, "presets are immutable");
        }

        [Test]
        public void Compact_InvalidFormats_Throw()
        {
            Assert.Throws<FrameworkException>(() => new CompactNumberFormat(new long[0], new string[0]));
            Assert.Throws<FrameworkException>(() => new CompactNumberFormat(new[] { 1000L }, new[] { "K", "M" }));
            Assert.Throws<FrameworkException>(() => new CompactNumberFormat(new[] { 1000L, 1000L }, new[] { "K", "M" }));
            Assert.Throws<FrameworkException>(() => new CompactNumberFormat(new[] { 1L }, new[] { "x" }));
            Assert.Throws<FrameworkException>(() => new CompactNumberFormat(new[] { 1000L }, new string[] { null }));
            Assert.Throws<FrameworkException>(() => CompactNumberFormat.Western.WithThreshold(999));
            Assert.Throws<FrameworkException>(() => CompactNumberFormat.Western.WithDecimals(7));
            Assert.Throws<FrameworkException>(() => CompactNumberFormat.Western.WithDecimals(1, 20));
            Assert.Throws<FrameworkException>(
                () => new CompactNumberFormat(new[] { 1000000000000000000L }, new[] { "E" }).WithDecimals(2),
                "remainder * 10^decimals must not overflow");
        }

        [Test]
        public void Compact_TooSmallBuffer_ReturnsFalse()
        {
            Span<char> small = stackalloc char[3];
            int written;
            Assert.IsFalse(TextFormat.TryFormatCompact(12345, small, out written));
            Assert.AreEqual(0, written);
            Assert.IsFalse(TextFormat.TryFormatCompact(-5, small.Slice(0, 1), out written));
            Assert.IsTrue(TextFormat.TryFormatCompact(-5, small.Slice(0, 2), out written));
            Assert.AreEqual(2, written);
        }

        [Test]
        public void Duration_ClockStyles()
        {
            Assert.AreEqual("0:00", Duration(0, DurationStyle.Clock));
            Assert.AreEqual("0:05", Duration(5, DurationStyle.Clock));
            Assert.AreEqual("1:05", Duration(65, DurationStyle.Clock));
            Assert.AreEqual("59:59", Duration(3599, DurationStyle.Clock));
            Assert.AreEqual("1:00:00", Duration(3600, DurationStyle.Clock));
            Assert.AreEqual("25:01:01", Duration(90061, DurationStyle.Clock));
            Assert.AreEqual("-1:05", Duration(-65, DurationStyle.Clock));
            Assert.AreEqual("-2562047788015215:30:08", Duration(long.MinValue, DurationStyle.Clock));

            Assert.AreEqual("00:00", Duration(0, DurationStyle.MinutesSeconds));
            Assert.AreEqual("01:05", Duration(65, DurationStyle.MinutesSeconds));
            Assert.AreEqual("60:00", Duration(3600, DurationStyle.MinutesSeconds));

            Assert.AreEqual("00:01:05", Duration(65, DurationStyle.HoursMinutesSeconds));
            Assert.AreEqual("100:00:00", Duration(360000, DurationStyle.HoursMinutesSeconds));
        }

        [Test]
        public void Duration_CompactStyle()
        {
            Assert.AreEqual("0s", Duration(0, DurationStyle.Compact));
            Assert.AreEqual("59s", Duration(59, DurationStyle.Compact));
            Assert.AreEqual("1m 1s", Duration(61, DurationStyle.Compact));
            Assert.AreEqual("1h", Duration(3600, DurationStyle.Compact));
            Assert.AreEqual("1h 1m", Duration(3661, DurationStyle.Compact), "at most two adjacent units by default");
            Assert.AreEqual("1h", Duration(3601, DurationStyle.Compact), "zero units inside the window are omitted");
            Assert.AreEqual("1d", Duration(86400, DurationStyle.Compact));
            Assert.AreEqual("1d 1h", Duration(90061, DurationStyle.Compact));
            Assert.AreEqual("-1m 1s", Duration(-61, DurationStyle.Compact));

            Assert.AreEqual("1" + Hour + "1" + Minute, Duration(3661, DurationStyle.Compact, DurationUnits.Chinese));
            Assert.AreEqual("1" + Day + "1" + Hour, Duration(90061, DurationStyle.Compact, DurationUnits.Chinese));
            Assert.AreEqual("59" + Second, Duration(59, DurationStyle.Compact, DurationUnits.Chinese));

            Assert.AreEqual("1d 1h 1m", Duration(90061, DurationStyle.Compact, new DurationUnits("d", "h", "m", "s", " ", 3)));
            Assert.AreEqual("1d 1h 1m 1s", Duration(90061, DurationStyle.Compact, new DurationUnits("d", "h", "m", "s", " ", 4)));
        }

        [Test]
        public void Duration_InvalidArguments_Throw()
        {
            char[] buffer = new char[32];
            int written;
            Assert.Throws<FrameworkException>(() => TextFormat.TryFormatDuration(1, buffer, out written, (DurationStyle)99));
            Assert.Throws<FrameworkException>(() => new DurationUnits(null, "h", "m", "s", " "));
            Assert.Throws<FrameworkException>(() => new DurationUnits("d", "h", "m", "s", " ", 0));
            Assert.Throws<FrameworkException>(() => new DurationUnits("d", "h", "m", "s", " ", 5));
        }

        [Test]
        public void ToWholeSeconds_RoundsAndClamps()
        {
            Assert.AreEqual(1L, TextFormat.ToWholeSeconds(0.2, DurationRounding.Ceiling));
            Assert.AreEqual(0L, TextFormat.ToWholeSeconds(0.2, DurationRounding.Floor));
            Assert.AreEqual(59L, TextFormat.ToWholeSeconds(59.0, DurationRounding.Ceiling));
            Assert.AreEqual(0L, TextFormat.ToWholeSeconds(-0.5, DurationRounding.Ceiling));
            Assert.AreEqual(-1L, TextFormat.ToWholeSeconds(-0.5, DurationRounding.Floor));
            Assert.AreEqual(0L, TextFormat.ToWholeSeconds(double.NaN, DurationRounding.Ceiling));
            Assert.AreEqual(long.MaxValue, TextFormat.ToWholeSeconds(1e30, DurationRounding.Floor));
            Assert.AreEqual(long.MinValue, TextFormat.ToWholeSeconds(-1e30, DurationRounding.Floor));

            using (var t = TempText.Rent())
            {
                t.AppendDuration(59.2, DurationStyle.Clock, DurationRounding.Ceiling);
                Assert.AreEqual("1:00", t.ToString(), "a countdown shows 0:00 only when it has really finished");
            }
        }

        [Test]
        public void Percent_RoundsAndNeverShowsFullBeforeComplete()
        {
            Assert.AreEqual("0%", Percent(0.0));
            Assert.AreEqual("50%", Percent(0.5));
            Assert.AreEqual("46%", Percent(0.456));
            Assert.AreEqual("45.6%", Percent(0.456, 1));
            Assert.AreEqual("12.50%", Percent(0.125, 2));
            Assert.AreEqual("0.1%", Percent(0.001, 1));
            Assert.AreEqual("0.0%", Percent(0.0004, 1));
            Assert.AreEqual("99%", Percent(0.9999), "below 1 never rounds up to 100%");
            Assert.AreEqual("99.99%", Percent(0.99999, 2));
            Assert.AreEqual("100%", Percent(1.0));
            Assert.AreEqual("150%", Percent(1.5));
            Assert.AreEqual("-25%", Percent(-0.25));
            Assert.AreEqual("0%", Percent(-0.001), "a tiny negative ratio rounds to 0%, not -0%");
            Assert.AreEqual("0%", Percent(double.NaN));
            Assert.AreEqual("0%", Percent(double.PositiveInfinity));
            Assert.AreEqual("9007199254740992%", Percent(1e20), "clamped to 2^53");
        }

        [Test]
        public void Percent_InvalidDecimalsOrSmallBuffer()
        {
            char[] buffer = new char[16];
            int written;
            Assert.Throws<FrameworkException>(() => TextFormat.TryFormatPercent(0.5, buffer, out written, -1));
            Assert.Throws<FrameworkException>(() => TextFormat.TryFormatPercent(0.5, buffer, out written, 7));
            Assert.IsFalse(TextFormat.TryFormatPercent(0.5, new char[2], out written));
            Assert.AreEqual(0, written);
            Assert.IsTrue(TextFormat.TryFormatPercent(0.5, new char[3], out written));
            Assert.AreEqual(3, written);
        }

        [Test]
        public void Formatting_IsAllocationFree()
        {
            char[] buffer = new char[64];
            CompactNumberFormat zh = CompactNumberFormat.Chinese;
            int total = 0;
            ZeroAlloc.Assert(() =>
            {
                int written;
                TextFormat.TryFormatCompact(123456789, buffer, out written, zh);
                total += written;
                TextFormat.TryFormatDuration(90061, buffer, out written, DurationStyle.Compact);
                total += written;
                TextFormat.TryFormatDuration(3725, buffer, out written);
                total += written;
                TextFormat.TryFormatPercent(0.456, buffer, out written, 1);
                total += written;
            });
            Assert.Greater(total, 0);
        }

        private static string Compact(long value, CompactNumberFormat format = null)
        {
            Span<char> buffer = stackalloc char[64];
            int written;
            Assert.IsTrue(TextFormat.TryFormatCompact(value, buffer, out written, format));
            return buffer.Slice(0, written).ToString();
        }

        private static string Duration(long seconds, DurationStyle style, DurationUnits units = null)
        {
            Span<char> buffer = stackalloc char[64];
            int written;
            Assert.IsTrue(TextFormat.TryFormatDuration(seconds, buffer, out written, style, units));
            return buffer.Slice(0, written).ToString();
        }

        private static string Percent(double ratio, int decimals = 0)
        {
            Span<char> buffer = stackalloc char[64];
            int written;
            Assert.IsTrue(TextFormat.TryFormatPercent(ratio, buffer, out written, decimals));
            return buffer.Slice(0, written).ToString();
        }
    }
}
