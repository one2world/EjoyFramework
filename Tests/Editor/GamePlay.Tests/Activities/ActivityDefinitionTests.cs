//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Activities;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Activities
{
    /// <summary>
    /// 针对 <see cref="ActivityDefinition"/> 三个工厂方法的构造与参数校验测试。
    /// </summary>
    [TestFixture]
    public class ActivityDefinitionTests
    {
        [Test]
        public void Once_StoresAbsoluteWindowAndPayload()
        {
            object payload = new object();
            ActivityDefinition def = ActivityDefinition.Once("e", 1000L, 2000L, payload);

            Assert.AreEqual("e", def.Id);
            Assert.AreEqual(ActivityRecurrence.Once, def.Recurrence);
            Assert.AreEqual(1000L, def.StartEpochMs);
            Assert.AreEqual(2000L, def.EndEpochMs);
            Assert.AreSame(payload, def.Payload);
        }

        [Test]
        public void Daily_StoresWindowSeconds()
        {
            ActivityDefinition def = ActivityDefinition.Daily("d", 3600, 7200);

            Assert.AreEqual(ActivityRecurrence.Daily, def.Recurrence);
            Assert.AreEqual(3600, def.DailyStartSec);
            Assert.AreEqual(7200, def.DailyEndSec);
            // Daily 每天生效，掩码应为全 1。
            Assert.AreEqual(0x7F, def.WeekdayMask);
        }

        [Test]
        public void Weekly_MasksToValidBits()
        {
            // 传入越界高位，构造时应裁剪到低 7 位。
            ActivityDefinition def = ActivityDefinition.Weekly("w", 0, 3600, 0x155);

            Assert.AreEqual(ActivityRecurrence.Weekly, def.Recurrence);
            Assert.AreEqual(0x155 & 0x7F, def.WeekdayMask);
        }

        [Test]
        public void Once_EndNotAfterStart_Throws()
        {
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Once("x", 2000L, 2000L));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Once("x", 2000L, 1000L));
        }

        [Test]
        public void NullOrEmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Once(null, 0L, 1L));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Daily("", 0, 1));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Weekly(null, 0, 1, 0x7F));
        }

        [Test]
        public void Daily_SecondOutOfRange_Throws()
        {
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Daily("d", -1, 3600));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Daily("d", 0, 86400));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Daily("d", 86400, 0));
        }

        [Test]
        public void Weekly_EmptyMask_Throws()
        {
            // 掩码低 7 位全 0 => 无任何有效星期 => 抛出。
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Weekly("w", 0, 3600, 0));
            Assert.Throws<ArgumentException>(() => ActivityDefinition.Weekly("w", 0, 3600, 0x80));
        }
    }
}
