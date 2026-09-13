//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Tweening;

namespace EjoyFramework.GamePlay.Tests.Tweening
{
    public class SequenceTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Append_RunsSequentially()
        {
            var a = new FloatTween(0f, 10f, 1f);
            var b = new FloatTween(0f, 20f, 1f);
            var seq = new Sequence();
            seq.Append(a);
            seq.Append(b);

            Assert.AreEqual(2f, seq.Duration, Delta, "总时长为两段之和");

            // 第一段进行中：A 推进，B 未开始。
            seq.Tick(0.5f);
            Assert.AreEqual(5f, a.Value, Delta);
            Assert.AreEqual(0f, b.Value, Delta);
            Assert.IsFalse(seq.IsComplete);

            // 第一段结束，A 完成；B 尚未推进。
            seq.Tick(0.5f);
            Assert.AreEqual(10f, a.Value, Delta);
            Assert.IsTrue(a.IsComplete);
            Assert.AreEqual(0f, b.Value, Delta);
            Assert.IsFalse(seq.IsComplete);

            // 第二段进行到一半。
            seq.Tick(0.5f);
            Assert.AreEqual(10f, b.Value, Delta);
            Assert.IsFalse(seq.IsComplete);

            // 第二段结束，整个序列完成。
            seq.Tick(0.5f);
            Assert.AreEqual(20f, b.Value, Delta);
            Assert.IsTrue(b.IsComplete);
            Assert.IsTrue(seq.IsComplete);
        }

        [Test]
        public void SetLoops_Restart_ReplaysChildrenEachLoop()
        {
            // 回归：SetLoops(>1) 的 Sequence 必须在每一轮重新驱动子 Tween。
            // 修复前 DriveTo 的单调本地时间线会把第二轮目标时间钳回 Duration，使循环静默无效。
            var a = new FloatTween(0f, 10f, 1f);
            var seq = new Sequence();
            seq.Append(a);
            seq.SetLoops(2);

            Assert.AreEqual(1f, seq.Duration, Delta);

            // 第 1 轮跑完。
            seq.Tick(1f);
            Assert.AreEqual(10f, a.Value, Delta);
            Assert.IsFalse(seq.IsComplete, "仍有第 2 轮，不应完成");

            // 第 2 轮推进一半：子 Tween 必须被复位并从头重新播放。
            seq.Tick(0.5f);
            Assert.AreEqual(5f, a.Value, Delta, "第 2 轮应从头重新驱动子 Tween");
            Assert.IsFalse(seq.IsComplete);

            // 第 2 轮跑完，整体完成。
            seq.Tick(0.5f);
            Assert.AreEqual(10f, a.Value, Delta);
            Assert.IsTrue(seq.IsComplete);
        }

        [Test]
        public void Append_CompletesAfterSumOfDurations()
        {
            int completeCount = 0;
            var seq = new Sequence();
            seq.Append(new FloatTween(0f, 1f, 1f));
            seq.Append(new FloatTween(0f, 1f, 2f));
            seq.OnComplete(() => completeCount++);

            Assert.AreEqual(3f, seq.Duration, Delta);

            seq.Tick(1.5f);
            Assert.IsFalse(seq.IsComplete);

            seq.Tick(1.5f);
            Assert.IsTrue(seq.IsComplete);
            Assert.AreEqual(1, completeCount);
        }

        [Test]
        public void Join_RunsInParallelWithLastAppended()
        {
            var a = new FloatTween(0f, 10f, 1f);
            var b = new FloatTween(0f, 100f, 1f);
            var seq = new Sequence();
            seq.Append(a);
            seq.Join(b); // 与 a 并行，起点同为 0。

            Assert.AreEqual(1f, seq.Duration, Delta, "并行项不增加总时长");

            seq.Tick(0.5f);
            Assert.AreEqual(5f, a.Value, Delta);
            Assert.AreEqual(50f, b.Value, Delta, "Join 的项与上一个 Append 并行推进");
        }

        [Test]
        public void AppendInterval_DelaysNextAppend()
        {
            var a = new FloatTween(0f, 10f, 1f);
            var seq = new Sequence();
            seq.AppendInterval(1f);
            seq.Append(a);

            Assert.AreEqual(2f, seq.Duration, Delta);

            // 间隔期间，a 还未开始。
            seq.Tick(1f);
            Assert.AreEqual(0f, a.Value, Delta);

            // 间隔结束后 a 推进到一半。
            seq.Tick(0.5f);
            Assert.AreEqual(5f, a.Value, Delta);
        }

        [Test]
        public void AppendCallback_FiresAtItsSlot()
        {
            bool fired = false;
            var seq = new Sequence();
            seq.Append(new FloatTween(0f, 10f, 1f));
            seq.AppendCallback(() => fired = true);
            seq.Append(new FloatTween(0f, 10f, 1f));

            Assert.AreEqual(2f, seq.Duration, Delta, "零时长回调不增加总时长");

            // 第一段进行中，回调（位于 t=1）尚未触发。
            seq.Tick(0.5f);
            Assert.IsFalse(fired);

            // 越过第一段结束点（t=1），回调触发。
            seq.Tick(0.5f);
            Assert.IsTrue(fired);
        }

        [Test]
        public void AppendCallback_FiresExactlyOnce()
        {
            int count = 0;
            var seq = new Sequence();
            seq.Append(new FloatTween(0f, 10f, 1f));
            seq.AppendCallback(() => count++);
            seq.Append(new FloatTween(0f, 10f, 1f));

            seq.Tick(1f);  // 越过回调点
            seq.Tick(0.5f);
            seq.Tick(0.5f);
            Assert.AreEqual(1, count, "回调只应触发一次");
        }

        [Test]
        public void Kill_Complete_DrivesAllChildrenToEnd()
        {
            var a = new FloatTween(0f, 10f, 1f);
            var b = new FloatTween(0f, 20f, 1f);
            bool seqCompleted = false;
            var seq = new Sequence();
            seq.Append(a);
            seq.Append(b);
            seq.OnComplete(() => seqCompleted = true);

            seq.Tick(0.5f);
            seq.Kill(true);

            Assert.AreEqual(10f, a.Value, Delta);
            Assert.AreEqual(20f, b.Value, Delta);
            Assert.IsTrue(seqCompleted);
            Assert.IsTrue(seq.IsKilled);
        }

        [Test]
        public void EmptySequence_HasZeroDuration_CompletesOnFirstTick()
        {
            var seq = new Sequence();
            Assert.AreEqual(0f, seq.Duration, Delta);

            bool finished = seq.Tick(0.016f);
            Assert.IsTrue(finished);
            Assert.IsTrue(seq.IsComplete);
        }

        [Test]
        public void SequenceWithOnlyCallback_FiresOnFirstTick()
        {
            bool fired = false;
            var seq = new Sequence();
            seq.AppendCallback(() => fired = true);

            seq.Tick(0.016f);
            Assert.IsTrue(fired);
            Assert.IsTrue(seq.IsComplete);
        }

        [Test]
        public void DrivenByManager_CompletesAndIsRemoved()
        {
            var manager = new TweenManager();
            var seq = new Sequence();
            seq.Append(new FloatTween(0f, 10f, 1f));
            seq.Append(new FloatTween(0f, 10f, 1f));
            manager.Add(seq);

            manager.Tick(1f);
            Assert.AreEqual(1, manager.ActiveCount);

            manager.Tick(1f);
            Assert.AreEqual(0, manager.ActiveCount);
            Assert.IsTrue(seq.IsComplete);
        }
    }
}
