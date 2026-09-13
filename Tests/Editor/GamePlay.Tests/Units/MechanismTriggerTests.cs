//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对引擎无关核心 <see cref="MechanismTrigger"/> 的单元测试：覆盖三种触发模式、冷却、就绪/解除武装、
    /// 触发计数、剩余冷却，以及发火事件的触发次数。
    /// </summary>
    [TestFixture]
    public class MechanismTriggerTests
    {
        private const float Delta = 1e-4f;

        // ---------------------------------------------------------------
        // 默认 / 就绪状态
        // ---------------------------------------------------------------

        [Test]
        public void Fresh_Trigger_IsArmed_WithZeroCounts()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity);

            Assert.AreEqual(TriggerMode.Proximity, trigger.Mode);
            Assert.IsTrue(trigger.IsArmed);
            Assert.AreEqual(0, trigger.TriggerCount);
            Assert.AreEqual(0f, trigger.CooldownRemaining, Delta);
        }

        [Test]
        public void Cooldown_And_Radius_ClampNegativeToZero()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity)
            {
                Cooldown = -5f,
                Radius = -3f,
            };

            Assert.AreEqual(0f, trigger.Cooldown, Delta);
            Assert.AreEqual(0f, trigger.Radius, Delta);
        }

        [Test]
        public void Interval_NonPositive_FallsBackToOne()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Timer) { Interval = 0f };
            Assert.AreEqual(1f, trigger.Interval, Delta);

            trigger.Interval = -2f;
            Assert.AreEqual(1f, trigger.Interval, Delta);

            trigger.Interval = 2.5f;
            Assert.AreEqual(2.5f, trigger.Interval, Delta);
        }

        // ---------------------------------------------------------------
        // Proximity
        // ---------------------------------------------------------------

        [Test]
        public void Proximity_Fires_WhenTargetInRangeAndArmed()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity) { Cooldown = 2f };
            int fired = 0;
            trigger.OnFired += t => fired++;

            bool result = trigger.Tick(0.1f, targetInRange: true);

            Assert.IsTrue(result);
            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, trigger.TriggerCount);
        }

        [Test]
        public void Proximity_DoesNotFire_WhenTargetNotInRange()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity) { Cooldown = 2f };
            int fired = 0;
            trigger.OnFired += t => fired++;

            bool result = trigger.Tick(0.1f, targetInRange: false);

            Assert.IsFalse(result);
            Assert.AreEqual(0, fired);
            Assert.AreEqual(0, trigger.TriggerCount);
            Assert.IsTrue(trigger.IsArmed);
        }

        [Test]
        public void Proximity_AfterFire_EntersCooldown_AndWillNotRefireUntilElapsed()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity) { Cooldown = 1f };
            int fired = 0;
            trigger.OnFired += t => fired++;

            // 首次发火，进入冷却。Tick 内先递减冷却（此时为 0）再发火，发火把冷却重置为完整 Cooldown，
            // 因此发火这一帧结束后剩余冷却为完整的 1.0（不在发火帧内再扣减）。
            Assert.IsTrue(trigger.Tick(0.1f, targetInRange: true));
            Assert.AreEqual(1, fired);
            Assert.IsFalse(trigger.IsArmed, "刚发火后应处于冷却，未就绪。");
            Assert.AreEqual(1f, trigger.CooldownRemaining, Delta);

            // 冷却期内即便目标在范围内也不再发火；后续帧才递减冷却。
            Assert.IsFalse(trigger.Tick(0.5f, targetInRange: true));
            Assert.AreEqual(1, fired);
            Assert.AreEqual(0.5f, trigger.CooldownRemaining, Delta);
        }

        [Test]
        public void Proximity_Refires_AfterCooldownElapsed()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity) { Cooldown = 1f };
            int fired = 0;
            trigger.OnFired += t => fired++;

            Assert.IsTrue(trigger.Tick(0.1f, targetInRange: true));  // fire #1, cd -> 1.0
            Assert.IsFalse(trigger.Tick(0.5f, targetInRange: true)); // cd -> 0.5, 仍在冷却
            Assert.IsFalse(trigger.Tick(0.4f, targetInRange: true)); // cd -> 0.1, 仍在冷却

            // 这一帧先把冷却递减到 0（0.1 - 0.1），随后 IsArmed 转真，且目标在范围内 -> 同帧重新发火。
            Assert.IsTrue(trigger.Tick(0.1f, targetInRange: true));  // cd -> 0.0 then fire #2
            Assert.AreEqual(2, fired);
            Assert.AreEqual(2, trigger.TriggerCount);
        }

        // ---------------------------------------------------------------
        // Timer
        // ---------------------------------------------------------------

        [Test]
        public void Timer_Fires_EveryInterval()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Timer)
            {
                Interval = 1f,
                Cooldown = 0f,
            };
            int fired = 0;
            trigger.OnFired += t => fired++;

            Assert.IsFalse(trigger.Tick(0.5f)); // accum 0.5
            Assert.IsFalse(trigger.Tick(0.4f)); // accum 0.9
            Assert.IsTrue(trigger.Tick(0.2f));  // accum 1.1 -> fire, remainder 0.1
            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, trigger.TriggerCount);

            Assert.IsFalse(trigger.Tick(0.5f)); // accum 0.6
            Assert.IsTrue(trigger.Tick(0.5f));  // accum 1.1 -> fire #2
            Assert.AreEqual(2, fired);
            Assert.AreEqual(2, trigger.TriggerCount);
        }

        [Test]
        public void Timer_Tick_IgnoresTargetInRangeFlag()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Timer)
            {
                Interval = 1f,
                Cooldown = 0f,
            };
            int fired = 0;
            trigger.OnFired += t => fired++;

            // 即便传入 targetInRange=false，定时器到点仍发火。
            Assert.IsTrue(trigger.Tick(1f, targetInRange: false));
            Assert.AreEqual(1, fired);
        }

        // ---------------------------------------------------------------
        // Manual
        // ---------------------------------------------------------------

        [Test]
        public void Manual_Fires_OnlyViaTryManualTrigger_WhenArmed()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Manual) { Cooldown = 1f };
            int fired = 0;
            trigger.OnFired += t => fired++;

            // Tick 不会自动发火。
            Assert.IsFalse(trigger.Tick(5f, targetInRange: true));
            Assert.AreEqual(0, fired);

            // 手动触发：就绪 -> 发火。
            Assert.IsTrue(trigger.TryManualTrigger());
            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, trigger.TriggerCount);

            // 冷却中再次手动触发失败。
            Assert.IsFalse(trigger.TryManualTrigger());
            Assert.AreEqual(1, fired);

            // 推进足够时间消耗冷却后，可再次手动触发。
            trigger.Tick(1f);
            Assert.IsTrue(trigger.IsArmed);
            Assert.IsTrue(trigger.TryManualTrigger());
            Assert.AreEqual(2, fired);
        }

        // ---------------------------------------------------------------
        // MaxTriggers / Reset
        // ---------------------------------------------------------------

        [Test]
        public void MaxTriggers_DisarmsAfterN_AndResetReArms()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Manual)
            {
                Cooldown = 0f,
                MaxTriggers = 2,
            };
            int fired = 0;
            trigger.OnFired += t => fired++;

            Assert.IsTrue(trigger.TryManualTrigger());  // #1
            Assert.IsTrue(trigger.TryManualTrigger());  // #2 -> reaches max
            Assert.AreEqual(2, fired);
            Assert.AreEqual(2, trigger.TriggerCount);

            // 达到上限后永久解除武装。
            Assert.IsFalse(trigger.IsArmed);
            Assert.IsFalse(trigger.TryManualTrigger());
            Assert.AreEqual(2, fired);

            // Reset 重新就绪并清零计数。
            trigger.Reset();
            Assert.IsTrue(trigger.IsArmed);
            Assert.AreEqual(0, trigger.TriggerCount);
            Assert.AreEqual(0f, trigger.CooldownRemaining, Delta);

            Assert.IsTrue(trigger.TryManualTrigger());
            Assert.AreEqual(3, fired);
            Assert.AreEqual(1, trigger.TriggerCount);
        }

        [Test]
        public void MaxTriggers_ZeroOrNegative_MeansUnlimited()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Manual)
            {
                Cooldown = 0f,
                MaxTriggers = 0,
            };

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(trigger.TryManualTrigger());
            }

            Assert.AreEqual(10, trigger.TriggerCount);
            Assert.IsTrue(trigger.IsArmed);
        }

        // ---------------------------------------------------------------
        // OnFired 次数 / 计数与冷却一致性
        // ---------------------------------------------------------------

        [Test]
        public void OnFired_FiresExactlyOncePerFire()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Proximity) { Cooldown = 1f };
            int fired = 0;
            MechanismTrigger captured = null;
            trigger.OnFired += t =>
            {
                fired++;
                captured = t;
            };

            // 连续两帧带目标，但因冷却只发火一次。
            Assert.IsTrue(trigger.Tick(0.1f, targetInRange: true));
            Assert.IsFalse(trigger.Tick(0.1f, targetInRange: true));

            Assert.AreEqual(1, fired);
            Assert.AreSame(trigger, captured, "OnFired 应回传触发器自身。");
        }

        [Test]
        public void CooldownRemaining_NeverGoesNegative()
        {
            MechanismTrigger trigger = new MechanismTrigger(TriggerMode.Manual) { Cooldown = 0.5f };
            trigger.TryManualTrigger(); // cd -> 0.5

            trigger.Tick(10f); // 大增量
            Assert.AreEqual(0f, trigger.CooldownRemaining, Delta);
            Assert.IsTrue(trigger.IsArmed);
        }
    }
}
