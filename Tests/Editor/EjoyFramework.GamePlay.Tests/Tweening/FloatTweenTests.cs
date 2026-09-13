//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Tweening;

namespace EjoyFramework.GamePlay.Tests.Tweening
{
    public class FloatTweenTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Linear_HalfWay_IsHalfValue()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.Tick(0.5f);
            Assert.AreEqual(5f, tween.Value, Delta);
            Assert.IsFalse(tween.IsComplete);
        }

        [Test]
        public void Linear_FullDuration_ReachesEnd_AndCompletes()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.Tick(0.5f);
            tween.Tick(0.5f);
            Assert.AreEqual(10f, tween.Value, Delta);
            Assert.IsTrue(tween.IsComplete);
        }

        [Test]
        public void OnComplete_FiresExactlyOnce()
        {
            int completeCount = 0;
            var tween = new FloatTween(0f, 10f, 1f);
            tween.OnComplete(() => completeCount++);

            tween.Tick(0.5f);
            Assert.AreEqual(0, completeCount, "中途不应触发 OnComplete");

            tween.Tick(0.5f);
            Assert.AreEqual(1, completeCount, "到达终点应触发一次");

            // 已完成后继续 Tick 不应再次触发。
            tween.Tick(0.5f);
            Assert.AreEqual(1, completeCount, "完成后不应重复触发");
        }

        [Test]
        public void OnUpdate_FiresEachTick()
        {
            int updateCount = 0;
            var tween = new FloatTween(0f, 10f, 1f);
            tween.OnUpdate(() => updateCount++);

            tween.Tick(0.25f);
            tween.Tick(0.25f);
            Assert.AreEqual(2, updateCount);
        }

        [Test]
        public void Setter_ReceivesInterpolatedValue()
        {
            float captured = -1f;
            var tween = new FloatTween(0f, 100f, 1f, v => captured = v);
            tween.Tick(0.25f);
            Assert.AreEqual(25f, captured, Delta);
        }

        [Test]
        public void SetDelay_HoldsAtFrom_UntilDelayElapses()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.SetDelay(0.5f);

            tween.Tick(0.25f);
            Assert.AreEqual(0f, tween.Value, Delta, "延迟期间应停留在起点");

            tween.Tick(0.25f);
            Assert.AreEqual(0f, tween.Value, Delta, "延迟刚好耗尽，进度尚未推进");

            tween.Tick(0.5f);
            Assert.AreEqual(5f, tween.Value, Delta, "延迟结束后正常推进");
        }

        [Test]
        public void SetDelay_OverflowTime_CarriesIntoProgress()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.SetDelay(0.5f);

            // 单帧 0.75：0.5 消耗延迟，剩 0.25 推进进度。
            tween.Tick(0.75f);
            Assert.AreEqual(2.5f, tween.Value, Delta);
        }

        [Test]
        public void ZeroDuration_CompletesOnFirstTick_AtEndValue()
        {
            int completeCount = 0;
            var tween = new FloatTween(3f, 9f, 0f);
            tween.OnComplete(() => completeCount++);

            bool finished = tween.Tick(0.016f);
            Assert.IsTrue(finished);
            Assert.IsTrue(tween.IsComplete);
            Assert.AreEqual(9f, tween.Value, Delta);
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void SetLoops_Restart_CompletesAfterTwoDurations()
        {
            int completeCount = 0;
            var tween = new FloatTween(0f, 10f, 1f);
            tween.SetLoops(2, LoopType.Restart);
            tween.OnComplete(() => completeCount++);

            tween.Tick(1f);
            Assert.IsFalse(tween.IsComplete, "第一圈结束后还应继续");
            Assert.AreEqual(0, completeCount);

            tween.Tick(0.5f);
            // 第二圈进行到一半，Restart 从起点重新开始：值≈5。
            Assert.AreEqual(5f, tween.Value, Delta);

            tween.Tick(0.5f);
            Assert.IsTrue(tween.IsComplete, "两圈跑完后应完成");
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void SetLoops_Yoyo_ReversesDirectionOnSecondLoop()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.SetLoops(2, LoopType.Yoyo);

            tween.Tick(1f); // 第一圈结束，值=10，进入反向阶段。
            Assert.AreEqual(10f, tween.Value, Delta);

            tween.Tick(0.5f); // 反向阶段一半：从 10 往 0 走，值≈5。
            Assert.AreEqual(5f, tween.Value, Delta);

            tween.Tick(0.5f); // 反向阶段结束：值≈0。
            Assert.AreEqual(0f, tween.Value, Delta);
            Assert.IsTrue(tween.IsComplete);
        }

        [Test]
        public void Kill_Complete_SnapsToEnd_AndFiresOnComplete()
        {
            int completeCount = 0;
            var tween = new FloatTween(0f, 10f, 1f);
            tween.OnComplete(() => completeCount++);

            tween.Tick(0.3f);
            Assert.AreEqual(3f, tween.Value, Delta);

            tween.Kill(true);
            Assert.AreEqual(10f, tween.Value, Delta, "Kill(true) 应快照到终点");
            Assert.IsTrue(tween.IsKilled);
            Assert.AreEqual(1, completeCount, "Kill(true) 应触发一次 OnComplete");
        }

        [Test]
        public void Kill_NoComplete_Stops_WithoutOnComplete()
        {
            int completeCount = 0;
            var tween = new FloatTween(0f, 10f, 1f);
            tween.OnComplete(() => completeCount++);

            tween.Tick(0.3f);
            tween.Kill(false);

            Assert.AreEqual(3f, tween.Value, Delta, "Kill(false) 不改变当前值");
            Assert.IsTrue(tween.IsKilled);
            Assert.IsFalse(tween.IsComplete);
            Assert.AreEqual(0, completeCount, "Kill(false) 不应触发 OnComplete");
        }

        [Test]
        public void Tick_AfterKilled_ReturnsTrue_AndDoesNothing()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.Tick(0.3f);
            tween.Kill(false);

            bool finished = tween.Tick(0.3f);
            Assert.IsTrue(finished);
            Assert.AreEqual(3f, tween.Value, Delta);
        }

        [Test]
        public void SetEase_AffectsInterpolation()
        {
            var tween = new FloatTween(0f, 10f, 1f);
            tween.SetEase(Ease.InQuad);
            tween.Tick(0.5f);
            // InQuad(0.5) = 0.25 -> 值 = 2.5。
            Assert.AreEqual(2.5f, tween.Value, Delta);
        }
    }
}
