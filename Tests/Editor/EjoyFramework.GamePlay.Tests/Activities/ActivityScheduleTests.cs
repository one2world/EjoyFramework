//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Activities;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Activities
{
    /// <summary>
    /// 针对 <see cref="ActivitySchedule"/> 的状态计算、倒计时、周期窗口（含跨午夜与星期掩码）
    /// 以及 Refresh 状态变化回调的单元测试。
    /// 时钟以可变字段 m_Now 注入，便于确定性地推进时间。
    /// </summary>
    [TestFixture]
    public class ActivityScheduleTests
    {
        // 锚点：2024-01-07 00:00:00 UTC，星期日（我方编号 0）。
        private const long SunMidnight = 1704585600000L;
        private const long MsPerSec = 1000L;
        private const long MsPerHour = 3600L * MsPerSec;
        private const long MsPerDay = 86400L * MsPerSec;

        // 星期位（bit0=周日 .. bit6=周六）。
        private const int Sun = 1 << 0;
        private const int Mon = 1 << 1;
        private const int Wed = 1 << 3;
        private const int Sat = 1 << 6;

        // 可变时钟，被 provider lambda 捕获。
        private long m_Now;

        // NUnit 默认在同一 Fixture 内复用实例，故每个测试前显式归零，避免时钟状态跨测试污染。
        [SetUp]
        public void SetUp()
        {
            m_Now = 0L;
        }

        private ActivitySchedule NewSchedule()
        {
            return new ActivitySchedule(() => m_Now);
        }

        [Test]
        public void Parameterless_DefaultsToRealtimeClock_AndSetClockInjects()
        {
            // 无参构造（框架解析路径）应可用，默认实时挂钟下未注册活动安全回退。
            ActivitySchedule sch = new ActivitySchedule();
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("nope"));

            // 注入确定性时钟后，状态严格随注入时钟推进。
            sch.SetClock(() => m_Now);
            sch.Define(ActivityDefinition.Once("e", 1000L, 2000L));

            m_Now = 500L;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("e"));

            m_Now = 1500L;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("e"));

            // SetClock(null) 回退实时挂钟，不应抛出。
            Assert.DoesNotThrow(() => sch.SetClock(null));
        }

        // ---------------- Once ----------------

        [Test]
        public void Once_NotStarted_Active_Ended_AcrossNow()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("e", 1000L, 2000L));

            m_Now = 500L;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("e"));
            Assert.IsFalse(sch.IsActive("e"));

            // 开始时刻（含）=> Active。
            m_Now = 1000L;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("e"));
            Assert.IsTrue(sch.IsActive("e"));

            // 窗口内。
            m_Now = 1500L;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("e"));

            // 结束时刻（不含）=> Ended。
            m_Now = 2000L;
            Assert.AreEqual(ActivityStatus.Ended, sch.GetStatus("e"));
            Assert.IsFalse(sch.IsActive("e"));
        }

        [Test]
        public void Once_TimeUntilStartAndEnd()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("e", 1000L, 2000L));

            // 未开始：距离开始 = 500；距离结束 = 0（未 Active）。
            m_Now = 500L;
            Assert.AreEqual(500L, sch.TimeUntilStartMs("e"));
            Assert.AreEqual(0L, sch.TimeUntilEndMs("e"));

            // Active：距离开始 = 0；距离结束 = 800。
            m_Now = 1200L;
            Assert.AreEqual(0L, sch.TimeUntilStartMs("e"));
            Assert.AreEqual(800L, sch.TimeUntilEndMs("e"));

            // 已结束：两者皆 0（无未来开始）。
            m_Now = 5000L;
            Assert.AreEqual(0L, sch.TimeUntilStartMs("e"));
            Assert.AreEqual(0L, sch.TimeUntilEndMs("e"));
        }

        [Test]
        public void UnknownId_DefaultsSafely()
        {
            ActivitySchedule sch = NewSchedule();
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("nope"));
            Assert.IsFalse(sch.IsActive("nope"));
            Assert.AreEqual(0L, sch.TimeUntilStartMs("nope"));
            Assert.AreEqual(0L, sch.TimeUntilEndMs("nope"));
        }

        // ---------------- Daily ----------------

        [Test]
        public void Daily_ActiveInsideWindow_InactiveOutside()
        {
            ActivitySchedule sch = NewSchedule();
            // 每日 09:00–11:00。
            sch.Define(ActivityDefinition.Daily("d", 9 * 3600, 11 * 3600));

            // 08:00 周日 => 窗外 NotStarted（永不 Ended）。
            m_Now = SunMidnight + 8 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("d"));

            // 10:00 => Active。
            m_Now = SunMidnight + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("d"));

            // 11:00（右端开区间）=> 窗外。
            m_Now = SunMidnight + 11 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("d"));

            // 次日 10:00 => 仍 Active（每日周期）。
            m_Now = SunMidnight + MsPerDay + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("d"));
        }

        [Test]
        public void Daily_TimeUntilStart_SameDayAndNextDay()
        {
            ActivitySchedule sch = NewSchedule();
            // 每日 09:00–11:00。
            sch.Define(ActivityDefinition.Daily("d", 9 * 3600, 11 * 3600));

            // 08:00 => 今天 09:00 开始，差 1 小时。
            m_Now = SunMidnight + 8 * MsPerHour;
            Assert.AreEqual(MsPerHour, sch.TimeUntilStartMs("d"));

            // 12:00 => 今天窗口已过 => 明天 09:00，差 21 小时。
            m_Now = SunMidnight + 12 * MsPerHour;
            Assert.AreEqual(21 * MsPerHour, sch.TimeUntilStartMs("d"));

            // 10:00（Active）=> 距离开始 0；距离结束 1 小时。
            m_Now = SunMidnight + 10 * MsPerHour;
            Assert.AreEqual(0L, sch.TimeUntilStartMs("d"));
            Assert.AreEqual(MsPerHour, sch.TimeUntilEndMs("d"));
        }

        [Test]
        public void Daily_WrappingWindow_AcrossMidnight()
        {
            ActivitySchedule sch = NewSchedule();
            // 每日 23:00–01:00（跨午夜环绕）。
            sch.Define(ActivityDefinition.Daily("night", 23 * 3600, 1 * 3600));

            // 周日 22:00 => 窗外，距开始 1 小时。
            m_Now = SunMidnight + 22 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("night"));
            Assert.AreEqual(MsPerHour, sch.TimeUntilStartMs("night"));

            // 周日 23:30 => Active；距离结束 = 到次日 01:00，1.5 小时。
            m_Now = SunMidnight + 23 * MsPerHour + 30 * 60 * MsPerSec;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("night"));
            Assert.AreEqual(90L * 60L * MsPerSec, sch.TimeUntilEndMs("night"));

            // 周一 00:30（环绕段）=> 仍 Active；距离结束 0.5 小时。
            m_Now = SunMidnight + MsPerDay + 30 * 60 * MsPerSec;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("night"));
            Assert.AreEqual(30L * 60L * MsPerSec, sch.TimeUntilEndMs("night"));

            // 周一 02:00 => 窗外。
            m_Now = SunMidnight + MsPerDay + 2 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("night"));
        }

        // ---------------- Weekly ----------------

        [Test]
        public void Weekly_ActiveOnlyOnMaskedWeekdays()
        {
            ActivitySchedule sch = NewSchedule();
            // 周一、周三 09:00–11:00。
            sch.Define(ActivityDefinition.Weekly("w", 9 * 3600, 11 * 3600, Mon | Wed));

            // 周日 10:00 => 星期未命中 => NotStarted。
            m_Now = SunMidnight + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("w"));

            // 周一 10:00 => Active。
            m_Now = SunMidnight + MsPerDay + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("w"));

            // 周二 10:00 => 未命中 => NotStarted。
            m_Now = SunMidnight + 2 * MsPerDay + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("w"));

            // 周三 10:00 => Active。
            m_Now = SunMidnight + 3 * MsPerDay + 10 * MsPerHour;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("w"));
        }

        [Test]
        public void Weekly_TimeUntilStart_SkipsToNextMaskedDay()
        {
            ActivitySchedule sch = NewSchedule();
            // 仅周三 09:00–11:00。
            sch.Define(ActivityDefinition.Weekly("w", 9 * 3600, 11 * 3600, Wed));

            // 周日 10:00 => 下一次开始为周三 09:00。
            // 从周日 10:00 到周三 09:00 = 2 天 + 23 小时。
            m_Now = SunMidnight + 10 * MsPerHour;
            long expected = 2 * MsPerDay + 23 * MsPerHour;
            Assert.AreEqual(expected, sch.TimeUntilStartMs("w"));
        }

        [Test]
        public void Weekly_WrappingWindow_BelongsToStartingDay()
        {
            ActivitySchedule sch = NewSchedule();
            // 仅周日 23:00–01:00（跨午夜），环绕段计入“开启那天”的星期（周日）。
            sch.Define(ActivityDefinition.Weekly("w", 23 * 3600, 1 * 3600, Sun));

            // 周日 23:30 => Active（命中周日）。
            m_Now = SunMidnight + 23 * MsPerHour + 30 * 60 * MsPerSec;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("w"));

            // 周一 00:30 => 仍 Active（环绕段归属周日，掩码命中）。
            m_Now = SunMidnight + MsPerDay + 30 * 60 * MsPerSec;
            Assert.AreEqual(ActivityStatus.Active, sch.GetStatus("w"));

            // 周一 23:30 => 周一未命中（窗口开启那天是周一）=> NotStarted。
            m_Now = SunMidnight + MsPerDay + 23 * MsPerHour + 30 * 60 * MsPerSec;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("w"));
        }

        // ---------------- Active set / Define / Remove ----------------

        [Test]
        public void ActiveActivityIds_ReflectsCurrentNow_InRegistrationOrder()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("a", 1000L, 5000L));
            sch.Define(ActivityDefinition.Once("b", 2000L, 3000L));

            m_Now = 1500L; // 仅 a 处于窗口。
            CollectionAssert.AreEqual(new[] { "a" }, new List<string>(sch.ActiveActivityIds));

            m_Now = 2500L; // a、b 均在窗口，按注册顺序。
            CollectionAssert.AreEqual(new[] { "a", "b" }, new List<string>(sch.ActiveActivityIds));

            m_Now = 4000L; // b 已结束，仅 a。
            CollectionAssert.AreEqual(new[] { "a" }, new List<string>(sch.ActiveActivityIds));
        }

        [Test]
        public void Define_DuplicateId_Throws()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("dup", 0L, 1L));
            Assert.Throws<ArgumentException>(() => sch.Define(ActivityDefinition.Once("dup", 5L, 9L)));
        }

        [Test]
        public void Define_Null_Throws()
        {
            ActivitySchedule sch = NewSchedule();
            Assert.Throws<ArgumentNullException>(() => sch.Define(null));
        }

        [Test]
        public void Remove_RemovesAndReturnsFlag()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("a", 0L, 1000L));

            Assert.IsTrue(sch.Remove("a"));
            Assert.IsFalse(sch.Remove("a"));   // 已移除。
            Assert.IsFalse(sch.Remove("none")); // 不存在。

            m_Now = 500L;
            Assert.AreEqual(ActivityStatus.NotStarted, sch.GetStatus("a")); // 移除后回退默认。
        }

        // ---------------- Refresh / OnStatusChanged ----------------

        [Test]
        public void Refresh_FiresOnStatusChanged_OnlyOnTransitions()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("e", 1000L, 2000L)); // 基线在 m_Now=0 => NotStarted

            List<string> log = new List<string>();
            sch.OnStatusChanged += (s, id, status) => log.Add(id + ":" + status);

            // 仍未开始：无变化，无回调。
            m_Now = 500L;
            sch.Refresh();
            Assert.AreEqual(0, log.Count);

            // NotStarted -> Active：触发一次。
            m_Now = 1500L;
            sch.Refresh();
            CollectionAssert.AreEqual(new[] { "e:Active" }, log);

            // 仍 Active：无新回调。
            m_Now = 1800L;
            sch.Refresh();
            Assert.AreEqual(1, log.Count);

            // Active -> Ended：触发一次。
            m_Now = 2500L;
            sch.Refresh();
            CollectionAssert.AreEqual(new[] { "e:Active", "e:Ended" }, log);

            // 此后再 Refresh 不再触发。
            sch.Refresh();
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Refresh_DailyTransition_FiresActiveThenNotStarted()
        {
            ActivitySchedule sch = NewSchedule();
            m_Now = SunMidnight + 8 * MsPerHour; // 基线：窗外 NotStarted
            sch.Define(ActivityDefinition.Daily("d", 9 * 3600, 11 * 3600));

            List<ActivityStatus> seen = new List<ActivityStatus>();
            sch.OnStatusChanged += (s, id, status) => seen.Add(status);

            m_Now = SunMidnight + 10 * MsPerHour; // 进入窗口
            sch.Refresh();
            m_Now = SunMidnight + 12 * MsPerHour; // 离开窗口
            sch.Refresh();

            CollectionAssert.AreEqual(
                new[] { ActivityStatus.Active, ActivityStatus.NotStarted }, seen);
        }

        [Test]
        public void Refresh_NoChangeAfterDefineBaseline_NoCallback()
        {
            ActivitySchedule sch = NewSchedule();
            m_Now = 1500L;
            // Define 时已 Active，基线记录为 Active。
            sch.Define(ActivityDefinition.Once("e", 1000L, 2000L));

            int count = 0;
            sch.OnStatusChanged += (s, id, status) => count++;

            // 时间未跨越任何边界 => 与基线一致 => 无回调。
            m_Now = 1800L;
            sch.Refresh();
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Refresh_RemovedDuringIteration_IsSafe()
        {
            ActivitySchedule sch = NewSchedule();
            sch.Define(ActivityDefinition.Once("a", 1000L, 2000L));
            sch.Define(ActivityDefinition.Once("b", 1000L, 2000L));

            // 在回调中移除另一活动，验证遍历已快照、不抛异常。
            sch.OnStatusChanged += (s, id, status) =>
            {
                if (id == "a")
                {
                    sch.Remove("b");
                }
            };

            m_Now = 1500L;
            Assert.DoesNotThrow(() => sch.Refresh());
        }
    }
}
