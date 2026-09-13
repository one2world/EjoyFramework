//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Atmosphere;

namespace EjoyFramework.GamePlay.Tests.Atmosphere
{
    public class TimeOfDayCycleTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Constructor_DefaultStart_IsDawnBoundary_06h()
        {
            // 默认 startNormalized=0.25 => 06:00，落在白昼之前的黎明之后？0.25 ∈ [0.21,0.29) => Dawn。
            var cycle = new TimeOfDayCycle(100f);
            Assert.AreEqual(0.25f, cycle.Normalized, Delta);
            Assert.AreEqual(6f, cycle.HourOfDay, Delta);
            Assert.AreEqual(DayPhase.Dawn, cycle.Phase);
            Assert.AreEqual(0, cycle.DayCount);
        }

        [Test]
        public void Constructor_StartAtNoon_IsDayPhase()
        {
            var cycle = new TimeOfDayCycle(100f, 0.5f);
            Assert.AreEqual(12f, cycle.HourOfDay, Delta);
            Assert.AreEqual(DayPhase.Day, cycle.Phase);
        }

        [Test]
        public void Constructor_StartAtMidnight_IsNight()
        {
            var cycle = new TimeOfDayCycle(100f, 0f);
            Assert.AreEqual(0f, cycle.HourOfDay, Delta);
            Assert.AreEqual(DayPhase.Night, cycle.Phase);
        }

        [Test]
        public void Constructor_OutOfRangeStart_WrapsInto01()
        {
            var cycle = new TimeOfDayCycle(100f, 1.25f);
            Assert.AreEqual(0.25f, cycle.Normalized, Delta);
        }

        [Test]
        public void Tick_AdvancesNormalizedAndHour()
        {
            // 一天 100 秒，前进 25 秒 => +0.25。起点 0 => 0.25。
            var cycle = new TimeOfDayCycle(100f, 0f);
            cycle.Tick(25f);
            Assert.AreEqual(0.25f, cycle.Normalized, Delta);
            Assert.AreEqual(6f, cycle.HourOfDay, Delta);
        }

        [Test]
        public void Tick_NonPositiveDelta_DoesNotAdvance()
        {
            var cycle = new TimeOfDayCycle(100f, 0.3f);
            cycle.Tick(0f);
            cycle.Tick(-5f);
            Assert.AreEqual(0.3f, cycle.Normalized, Delta);
        }

        [Test]
        public void Tick_AcrossPhaseBoundary_FiresOnPhaseChangedOnce()
        {
            // 一天 100 秒。起点 0.20（Night），前进到 0.25（Dawn）跨过 0.21 边界。
            var cycle = new TimeOfDayCycle(100f, 0.20f);
            Assert.AreEqual(DayPhase.Night, cycle.Phase);

            var fired = new List<DayPhase>();
            cycle.OnPhaseChanged += (c, p) => fired.Add(p);

            cycle.Tick(5f); // +0.05 => 0.25 => Dawn
            Assert.AreEqual(DayPhase.Dawn, cycle.Phase);
            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(DayPhase.Dawn, fired[0]);
        }

        [Test]
        public void Tick_WithinSamePhase_DoesNotFirePhaseChanged()
        {
            var cycle = new TimeOfDayCycle(100f, 0.40f); // Day
            int count = 0;
            cycle.OnPhaseChanged += (c, p) => count++;

            cycle.Tick(5f); // 0.45 still Day
            Assert.AreEqual(DayPhase.Day, cycle.Phase);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Tick_MidnightWrap_IncrementsDayCount_AndFiresOnNewDay()
        {
            // 起点 0.9，一天 100 秒。前进 20 秒 => 0.9+0.2=1.1 => wrap => 0.1，跨午夜一次。
            var cycle = new TimeOfDayCycle(100f, 0.9f);
            int newDayCount = 0;
            int lastDay = -1;
            cycle.OnNewDay += (c, d) => { newDayCount++; lastDay = d; };

            cycle.Tick(20f);
            Assert.AreEqual(0.1f, cycle.Normalized, Delta);
            Assert.AreEqual(1, cycle.DayCount);
            Assert.AreEqual(1, newDayCount);
            Assert.AreEqual(1, lastDay);
        }

        [Test]
        public void Tick_MultiDayJump_FiresOnNewDayPerDay()
        {
            // 一天 100 秒，单帧 250 秒 => 跨 2 个完整午夜（外加 0.5 天）。起点 0 => 2.5 => 2 次 wrap。
            var cycle = new TimeOfDayCycle(100f, 0f);
            int newDayCount = 0;
            cycle.OnNewDay += (c, d) => newDayCount++;

            cycle.Tick(250f);
            Assert.AreEqual(2, cycle.DayCount);
            Assert.AreEqual(2, newDayCount);
            Assert.AreEqual(0.5f, cycle.Normalized, Delta);
        }

        [Test]
        public void SetNormalized_Jump_UpdatesPhase_WithoutDayCount()
        {
            var cycle = new TimeOfDayCycle(100f, 0f); // Night, day 0
            DayPhase fired = DayPhase.Night;
            int phaseChangeCount = 0;
            cycle.OnPhaseChanged += (c, p) => { fired = p; phaseChangeCount++; };
            int newDayCount = 0;
            cycle.OnNewDay += (c, d) => newDayCount++;

            cycle.SetNormalized(0.5f); // => Day
            Assert.AreEqual(0.5f, cycle.Normalized, Delta);
            Assert.AreEqual(DayPhase.Day, cycle.Phase);
            Assert.AreEqual(DayPhase.Day, fired);
            Assert.AreEqual(1, phaseChangeCount);
            Assert.AreEqual(0, cycle.DayCount, "SetNormalized 不应改变天数");
            Assert.AreEqual(0, newDayCount, "SetNormalized 不应触发 OnNewDay");
        }

        [Test]
        public void SetNormalized_SamePhase_DoesNotFirePhaseChanged()
        {
            var cycle = new TimeOfDayCycle(100f, 0.40f); // Day
            int count = 0;
            cycle.OnPhaseChanged += (c, p) => count++;

            cycle.SetNormalized(0.60f); // still Day
            Assert.AreEqual(DayPhase.Day, cycle.Phase);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void SetNormalized_OutOfRange_Wraps()
        {
            var cycle = new TimeOfDayCycle(100f, 0f);
            cycle.SetNormalized(-0.25f); // => 0.75
            Assert.AreEqual(0.75f, cycle.Normalized, Delta);
        }

        [Test]
        public void SunAngleDegrees_KeyTimes_MapAsDocumented()
        {
            // 映射：angle = (Normalized*360 + 270) mod 360
            var midnight = new TimeOfDayCycle(100f, 0f);
            Assert.AreEqual(270f, midnight.SunAngleDegrees, Delta);

            var sixAm = new TimeOfDayCycle(100f, 0.25f);
            Assert.AreEqual(0f, sixAm.SunAngleDegrees, Delta);

            var noon = new TimeOfDayCycle(100f, 0.5f);
            Assert.AreEqual(90f, noon.SunAngleDegrees, Delta);

            var sixPm = new TimeOfDayCycle(100f, 0.75f);
            Assert.AreEqual(180f, sixPm.SunAngleDegrees, Delta);
        }

        [Test]
        public void SunAngleDegrees_AlwaysInRange0To360()
        {
            var cycle = new TimeOfDayCycle(100f, 0.99f);
            float angle = cycle.SunAngleDegrees;
            Assert.GreaterOrEqual(angle, 0f);
            Assert.Less(angle, 360f);
        }

        [Test]
        public void DayLengthSeconds_Setter_AffectsTickRate()
        {
            var cycle = new TimeOfDayCycle(100f, 0f);
            cycle.DayLengthSeconds = 50f;
            cycle.Tick(25f); // 25/50 = 0.5
            Assert.AreEqual(0.5f, cycle.Normalized, Delta);
        }
    }
}
