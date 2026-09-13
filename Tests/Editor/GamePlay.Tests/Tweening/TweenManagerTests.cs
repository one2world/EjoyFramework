//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Tweening;

namespace EjoyFramework.GamePlay.Tests.Tweening
{
    public class TweenManagerTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Add_IncreasesActiveCount()
        {
            var manager = new TweenManager();
            Assert.AreEqual(0, manager.ActiveCount);

            manager.Add(new FloatTween(0f, 1f, 1f));
            manager.Add(new FloatTween(0f, 1f, 1f));
            Assert.AreEqual(2, manager.ActiveCount);
        }

        [Test]
        public void Tick_DrivesAllTweens()
        {
            var manager = new TweenManager();
            var a = new FloatTween(0f, 10f, 1f);
            var b = new FloatTween(0f, 20f, 1f);
            manager.Add(a);
            manager.Add(b);

            manager.Tick(0.5f);
            Assert.AreEqual(5f, a.Value, Delta);
            Assert.AreEqual(10f, b.Value, Delta);
        }

        [Test]
        public void Tick_RemovesCompletedTweens()
        {
            var manager = new TweenManager();
            manager.Add(new FloatTween(0f, 10f, 1f));
            manager.Add(new FloatTween(0f, 10f, 2f));

            manager.Tick(1f); // 第一个完成，第二个还在跑。
            Assert.AreEqual(1, manager.ActiveCount);

            manager.Tick(1f); // 第二个完成。
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void ActiveCount_DropsToZero_WhenAllDone()
        {
            var manager = new TweenManager();
            for (int i = 0; i < 5; i++)
            {
                manager.Add(new FloatTween(0f, 1f, 1f));
            }

            manager.Tick(1f);
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void OnComplete_AddingTweenDuringTick_DoesNotThrow()
        {
            var manager = new TweenManager();
            bool addedRan = false;

            var first = new FloatTween(0f, 10f, 1f);
            first.OnComplete(() =>
            {
                // 在 Tick 遍历过程中加入新 Tween——不得抛异常。
                var added = new FloatTween(0f, 5f, 1f);
                added.OnComplete(() => addedRan = true);
                manager.Add(added);
            });
            manager.Add(first);

            Assert.DoesNotThrow(() => manager.Tick(1f));

            // 新加入的 Tween 在下一帧才被驱动。
            Assert.AreEqual(1, manager.ActiveCount);
            manager.Tick(1f);
            Assert.IsTrue(addedRan);
            Assert.AreEqual(0, manager.ActiveCount);
        }

        [Test]
        public void OnComplete_KillingAnotherTweenDuringTick_DoesNotThrow()
        {
            var manager = new TweenManager();
            var victim = new FloatTween(0f, 10f, 5f);
            var trigger = new FloatTween(0f, 10f, 1f);
            trigger.OnComplete(() => victim.Kill(false));

            manager.Add(victim);
            manager.Add(trigger);

            Assert.DoesNotThrow(() => manager.Tick(1f));
            Assert.AreEqual(0, manager.ActiveCount, "被杀死与已完成的都应被移除");
        }

        [Test]
        public void KillAll_NoComplete_ClearsManager()
        {
            var manager = new TweenManager();
            int completeCount = 0;
            for (int i = 0; i < 3; i++)
            {
                var t = new FloatTween(0f, 10f, 1f);
                t.OnComplete(() => completeCount++);
                manager.Add(t);
            }

            manager.KillAll(false);
            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(0, completeCount);
        }

        [Test]
        public void KillAll_Complete_FiresAllOnComplete()
        {
            var manager = new TweenManager();
            int completeCount = 0;
            for (int i = 0; i < 3; i++)
            {
                var t = new FloatTween(0f, 10f, 1f);
                t.OnComplete(() => completeCount++);
                manager.Add(t);
            }

            manager.KillAll(true);
            Assert.AreEqual(0, manager.ActiveCount);
            Assert.AreEqual(3, completeCount);
        }

        [Test]
        public void Add_Duplicate_IsIgnored()
        {
            var manager = new TweenManager();
            var t = new FloatTween(0f, 1f, 1f);
            manager.Add(t);
            manager.Add(t);
            Assert.AreEqual(1, manager.ActiveCount);
        }

        [Test]
        public void Add_Null_IsIgnored()
        {
            var manager = new TweenManager();
            manager.Add(null);
            Assert.AreEqual(0, manager.ActiveCount);
        }
    }
}
