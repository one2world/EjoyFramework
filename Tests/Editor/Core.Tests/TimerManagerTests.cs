//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Timer;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// TimerManager 单测。
    /// 测试程序集只引用 EjoyFramework.Core（core），不引用 Unity 层，因此直接测 Manager：
    /// new TimerManager()（internal，经 InternalsVisibleTo 可见），手动调用 Update(dt, dt) 驱动。
    /// </summary>
    public class TimerManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void OneShot_FiresOnceAfterDelay()
        {
            var tm = new TimerManager();
            int hits = 0;
            int id = tm.AddTimer(1f, () => hits++);

            Assert.Greater(id, 0);
            Assert.AreEqual(1, tm.ActiveTimerCount);

            // 未到时：不触发
            tm.Update(0.5f, 0.5f);
            Assert.AreEqual(0, hits);

            // 到时：触发一次，并从活动列表移除
            tm.Update(0.5f, 0.5f);
            Assert.AreEqual(1, hits);
            Assert.AreEqual(0, tm.ActiveTimerCount);

            // 再 Update 不会重复触发
            tm.Update(1f, 1f);
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void Repeating_FiresExactlyNTimes()
        {
            var tm = new TimerManager();
            int hits = 0;
            tm.AddRepeatingTimer(1f, () => hits++, repeatCount: 3);

            for (int i = 0; i < 5; i++) tm.Update(1f, 1f);

            Assert.AreEqual(3, hits);
            Assert.AreEqual(0, tm.ActiveTimerCount, "次数用尽后应自动移除");
        }

        [Test]
        public void Repeating_Infinite_KeepsFiring()
        {
            var tm = new TimerManager();
            int hits = 0;
            int id = tm.AddRepeatingTimer(1f, () => hits++, repeatCount: -1);

            for (int i = 0; i < 4; i++) tm.Update(1f, 1f);

            Assert.AreEqual(4, hits);
            Assert.IsTrue(tm.IsTimerActive(id), "无限重复定时器不应被移除");
        }

        [Test]
        public void Repeating_CatchesUpWhenDeltaExceedsInterval()
        {
            var tm = new TimerManager();
            int hits = 0;
            // 间隔 0.5，单次 Update 推进 2.0 秒 → 应补触发 4 次
            tm.AddRepeatingTimer(0.5f, () => hits++, repeatCount: -1);

            tm.Update(2.0f, 2.0f);

            Assert.AreEqual(4, hits);
        }

        [Test]
        public void FrameTimer_FiresAfterNUpdates()
        {
            var tm = new TimerManager();
            int hits = 0;
            tm.AddFrameTimer(3, () => hits++);

            tm.Update(0.016f, 0.016f);
            Assert.AreEqual(0, hits);
            tm.Update(0.016f, 0.016f);
            Assert.AreEqual(0, hits);
            tm.Update(0.016f, 0.016f);
            Assert.AreEqual(1, hits, "第 3 次 Update 后触发");
            Assert.AreEqual(0, tm.ActiveTimerCount);

            // 帧定时器不受 dt 影响（即便 dt 为 0 也按帧递减）
            int hits2 = 0;
            tm.AddFrameTimer(2, () => hits2++);
            tm.Update(0f, 0f);
            tm.Update(0f, 0f);
            Assert.AreEqual(1, hits2);
        }

        [Test]
        public void RemoveTimer_CancelsBeforeFiring()
        {
            var tm = new TimerManager();
            int hits = 0;
            int id = tm.AddTimer(1f, () => hits++);

            Assert.IsTrue(tm.RemoveTimer(id));
            Assert.IsFalse(tm.IsTimerActive(id));
            Assert.AreEqual(0, tm.ActiveTimerCount);

            tm.Update(2f, 2f);
            Assert.AreEqual(0, hits);

            // 重复移除返回 false
            Assert.IsFalse(tm.RemoveTimer(id));
        }

        [Test]
        public void PauseResume_HaltsAndResumesProgress()
        {
            var tm = new TimerManager();
            int hits = 0;
            int id = tm.AddTimer(1f, () => hits++);

            tm.Update(0.5f, 0.5f);   // remaining 0.5
            tm.PauseTimer(id);

            // 暂停期间推进不生效
            tm.Update(1f, 1f);
            Assert.AreEqual(0, hits);
            Assert.IsTrue(tm.IsTimerActive(id), "暂停态仍视为活动");

            tm.ResumeTimer(id);
            tm.Update(0.5f, 0.5f);   // remaining 0 → 触发
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void UnscaledTimer_UsesRealElapseNotScaled()
        {
            var tm = new TimerManager();
            int scaledHits = 0;
            int unscaledHits = 0;

            // 模拟 Time.timeScale = 0：elapseSeconds=0，realElapseSeconds 仍走真实时间。
            int scaledId = tm.AddTimer(1f, () => scaledHits++, useUnscaledTime: false);
            int unscaledId = tm.AddTimer(1f, () => unscaledHits++, useUnscaledTime: true);

            tm.Update(0f, 1f);   // scaled 不推进；unscaled 推进 1 秒

            Assert.AreEqual(0, scaledHits, "缩放定时器在 elapse=0 时不应推进");
            Assert.AreEqual(1, unscaledHits, "非缩放定时器应按 realElapse 推进并触发");
            Assert.IsTrue(tm.IsTimerActive(scaledId));
            Assert.IsFalse(tm.IsTimerActive(unscaledId));
        }

        [Test]
        public void Reentrancy_CallbackCanAddAndRemoveTimers()
        {
            var tm = new TimerManager();
            int outerHits = 0;
            int innerHits = 0;

            // 回调里新增一个定时器，并移除自己之外的逻辑——验证遍历期间增删字典不抛异常。
            tm.AddTimer(1f, () =>
            {
                outerHits++;
                tm.AddTimer(1f, () => innerHits++);
            });

            tm.Update(1f, 1f);
            Assert.AreEqual(1, outerHits);
            Assert.AreEqual(0, innerHits, "本帧新增的定时器不应在同一帧立即触发");
            Assert.AreEqual(1, tm.ActiveTimerCount, "外层结束、内层新增");

            tm.Update(1f, 1f);
            Assert.AreEqual(1, innerHits);
        }

        [Test]
        public void RemoveAllTimers_ClearsEverything()
        {
            var tm = new TimerManager();
            int hits = 0;
            tm.AddTimer(1f, () => hits++);
            tm.AddRepeatingTimer(1f, () => hits++, repeatCount: -1);
            tm.AddFrameTimer(2, () => hits++);
            Assert.AreEqual(3, tm.ActiveTimerCount);

            tm.RemoveAllTimers();
            Assert.AreEqual(0, tm.ActiveTimerCount);

            tm.Update(5f, 5f);
            Assert.AreEqual(0, hits);
        }

        [Test]
        public void InvalidArgs_ReturnZeroAndDoNotRegister()
        {
            var tm = new TimerManager();

            Assert.AreEqual(0, tm.AddTimer(1f, null));
            Assert.AreEqual(0, tm.AddRepeatingTimer(1f, null));
            Assert.AreEqual(0, tm.AddRepeatingTimer(0f, () => { }), "间隔必须 > 0");
            Assert.AreEqual(0, tm.AddRepeatingTimer(1f, () => { }, repeatCount: 0), "repeatCount 0 永不触发");
            Assert.AreEqual(0, tm.AddFrameTimer(3, null));
            Assert.AreEqual(0, tm.ActiveTimerCount);
        }
    }
}
