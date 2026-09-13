//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Production;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Production
{
    /// <summary>
    /// 针对 <see cref="BuildQueue"/> 槽位并发、FIFO 排队、完成收割与晋升、加速/强制完成及去重行为的单元测试。
    /// </summary>
    [TestFixture]
    public class BuildQueueTests
    {
        private long m_Now;

        private Func<long> Provider
        {
            get { return () => m_Now; }
        }

        [SetUp]
        public void SetUp()
        {
            m_Now = 0L;
        }

        private static List<string> ToList(IEnumerable<string> source)
        {
            return new List<string>(source);
        }

        [Test]
        public void Enqueue_FillsSlots_ThenQueuesOverflow()
        {
            BuildQueue queue = new BuildQueue(2, Provider);

            Assert.IsTrue(queue.Enqueue("a", 1000L));
            Assert.IsTrue(queue.Enqueue("b", 1000L));
            Assert.IsTrue(queue.Enqueue("c", 1000L)); // 溢出 → 排队

            Assert.AreEqual(2, queue.ActiveCount);
            Assert.AreEqual(1, queue.QueuedCount);
            CollectionAssert.AreEqual(new[] { "a", "b" }, ToList(queue.ActiveJobIds));
            CollectionAssert.AreEqual(new[] { "c" }, ToList(queue.QueuedJobIds));

            // 已开工任务记录了开工时间；排队任务 StartedEpochMs 仍为 0。
            Assert.IsTrue(queue.GetJob("a").IsStarted);
            Assert.AreEqual(0L, queue.GetJob("a").StartedEpochMs);
            Assert.IsFalse(queue.GetJob("c").IsStarted);
            Assert.AreEqual(0L, queue.GetJob("c").StartedEpochMs);
        }

        [Test]
        public void Enqueue_DuplicateId_Rejected()
        {
            BuildQueue queue = new BuildQueue(2, Provider);

            Assert.IsTrue(queue.Enqueue("a", 1000L));
            Assert.IsFalse(queue.Enqueue("a", 500L)); // 重复（在建）

            Assert.IsTrue(queue.Enqueue("b", 1000L));
            Assert.IsTrue(queue.Enqueue("c", 1000L)); // 排队
            Assert.IsFalse(queue.Enqueue("c", 1000L)); // 重复（排队）

            Assert.AreEqual(2, queue.ActiveCount);
            Assert.AreEqual(1, queue.QueuedCount);
        }

        [Test]
        public void Enqueue_NullOrEmptyId_Rejected()
        {
            BuildQueue queue = new BuildQueue(2, Provider);
            Assert.IsFalse(queue.Enqueue(null, 1000L));
            Assert.IsFalse(queue.Enqueue(string.Empty, 1000L));
            Assert.AreEqual(0, queue.ActiveCount);
        }

        [Test]
        public void CompletePending_BeforeDuration_ReapsNothing()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 1000L);

            m_Now = 999L; // 还差 1ms
            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, finished.Count);
            Assert.AreEqual(1, queue.ActiveCount);
        }

        [Test]
        public void CompletePending_ReapsFinished_PromotesQueued_FiresEvent()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            List<string> events = new List<string>();
            queue.OnJobCompleted += (q, id) => events.Add(id);

            queue.Enqueue("a", 1000L); // 开工
            queue.Enqueue("b", 2000L); // 排队

            m_Now = 1000L; // a 完成
            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            Assert.AreEqual(1, count);
            CollectionAssert.AreEqual(new[] { "a" }, finished);
            CollectionAssert.AreEqual(new[] { "a" }, events);

            // b 被晋升并以 now=1000 开工。
            Assert.AreEqual(1, queue.ActiveCount);
            Assert.AreEqual(0, queue.QueuedCount);
            BuildJob b = queue.GetJob("b");
            Assert.IsNotNull(b);
            Assert.IsTrue(b.IsStarted);
            Assert.AreEqual(1000L, b.StartedEpochMs);

            // a 已彻底移除。
            Assert.IsNull(queue.GetJob("a"));
        }

        [Test]
        public void CompletePending_MultipleSlots_Concurrent()
        {
            BuildQueue queue = new BuildQueue(3, Provider);
            queue.Enqueue("a", 500L);
            queue.Enqueue("b", 1000L);
            queue.Enqueue("c", 1500L);
            queue.Enqueue("d", 1000L); // 排队

            Assert.AreEqual(3, queue.ActiveCount);
            Assert.AreEqual(1, queue.QueuedCount);

            m_Now = 1000L; // a、b 完成；c 仍在建
            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            Assert.AreEqual(2, count);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, finished);

            // 2 个槽位空出，但只有 1 个排队任务 d 可晋升。
            Assert.AreEqual(2, queue.ActiveCount); // c + d
            Assert.AreEqual(0, queue.QueuedCount);
            Assert.IsTrue(queue.GetJob("d").IsStarted);
            Assert.AreEqual(1000L, queue.GetJob("d").StartedEpochMs);
        }

        [Test]
        public void CompletePending_NullFinishedInto_StillCounts()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 1000L);

            m_Now = 1000L;
            int count = queue.CompletePending(null);

            Assert.AreEqual(1, count);
            Assert.AreEqual(0, queue.ActiveCount);
        }

        [Test]
        public void CompletePending_ZeroDurationQueuedJob_PromotedAndCompletedInOneCall()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            List<string> events = new List<string>();
            queue.OnJobCompleted += (q, id) => events.Add(id);

            queue.Enqueue("a", 1000L); // 开工
            queue.Enqueue("zero", 0L); // 排队，时长 0
            queue.Enqueue("b", 1000L); // 排队

            m_Now = 1000L; // a 完成 → zero 晋升即完成 → b 晋升开工
            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            // a 与 zero 在同一次调用内完成。
            Assert.AreEqual(2, count);
            CollectionAssert.AreEqual(new[] { "a", "zero" }, finished);
            CollectionAssert.AreEqual(new[] { "a", "zero" }, events);

            // b 最终占据唯一槽位且已开工。
            Assert.AreEqual(1, queue.ActiveCount);
            Assert.AreEqual(0, queue.QueuedCount);
            Assert.IsTrue(queue.GetJob("b").IsStarted);
            Assert.AreEqual(1000L, queue.GetJob("b").StartedEpochMs);
        }

        [Test]
        public void Enqueue_ZeroDurationIntoFreeSlot_CompletedByNextCompletePending()
        {
            BuildQueue queue = new BuildQueue(2, Provider);
            queue.Enqueue("z", 0L); // 立即开工，且相对 now=0 即刻完成

            Assert.IsTrue(queue.GetJob("z").IsComplete(m_Now));

            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            Assert.AreEqual(1, count);
            CollectionAssert.AreEqual(new[] { "z" }, finished);
            Assert.AreEqual(0, queue.ActiveCount);
        }

        [Test]
        public void SpeedUp_ActiveJob_ShortensRemainingMs()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 10000L); // 开工于 now=0

            m_Now = 2000L; // 已过 2 秒，剩余 8000
            Assert.AreEqual(8000L, queue.GetJob("a").RemainingMs(m_Now));

            Assert.IsTrue(queue.SpeedUp("a", 3000L)); // 缩短 3 秒
            Assert.AreEqual(5000L, queue.GetJob("a").RemainingMs(m_Now));
        }

        [Test]
        public void SpeedUp_ClampsToImmediateCompletion()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 5000L);

            m_Now = 1000L; // 剩余 4000
            Assert.IsTrue(queue.SpeedUp("a", 999999L)); // 远超剩余 → 钳到 0

            Assert.AreEqual(0L, queue.GetJob("a").RemainingMs(m_Now));
            Assert.IsTrue(queue.GetJob("a").IsComplete(m_Now));

            List<string> finished = new List<string>();
            Assert.AreEqual(1, queue.CompletePending(finished));
            CollectionAssert.AreEqual(new[] { "a" }, finished);
        }

        [Test]
        public void SpeedUp_QueuedJob_ReducesDuration()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 10000L); // 在建
            queue.Enqueue("b", 8000L);  // 排队

            Assert.IsTrue(queue.SpeedUp("b", 3000L)); // 排队任务直接削减时长
            Assert.AreEqual(5000L, queue.GetJob("b").DurationMs);
            Assert.IsFalse(queue.GetJob("b").IsStarted);
            // 排队任务剩余 = 时长（未开工）
            Assert.AreEqual(5000L, queue.GetJob("b").RemainingMs(m_Now));
        }

        [Test]
        public void SpeedUp_MissingJob_ReturnsFalse()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            Assert.IsFalse(queue.SpeedUp("nope", 1000L));
        }

        [Test]
        public void FinishNow_CompletesActiveJob_ReapedByCompletePending()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            List<string> events = new List<string>();
            queue.OnJobCompleted += (q, id) => events.Add(id);

            queue.Enqueue("a", 10000L);
            queue.Enqueue("b", 1000L); // 排队

            m_Now = 100L;
            Assert.IsTrue(queue.FinishNow("a"));
            Assert.IsTrue(queue.GetJob("a").IsComplete(m_Now)); // 标记为相对 now 完成

            List<string> finished = new List<string>();
            int count = queue.CompletePending(finished);

            Assert.AreEqual(1, count);
            CollectionAssert.AreEqual(new[] { "a" }, finished);
            CollectionAssert.AreEqual(new[] { "a" }, events);
            // b 晋升开工于 now=100。
            Assert.AreEqual(1, queue.ActiveCount);
            Assert.IsTrue(queue.GetJob("b").IsStarted);
            Assert.AreEqual(100L, queue.GetJob("b").StartedEpochMs);
        }

        [Test]
        public void FinishNow_QueuedJob_ReturnsFalse()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 10000L); // 在建
            queue.Enqueue("b", 1000L);  // 排队

            Assert.IsFalse(queue.FinishNow("b")); // 仅对在建任务有效
            Assert.IsFalse(queue.FinishNow("nope"));
        }

        [Test]
        public void RemainingMs_And_IsComplete_AcrossNowValues()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 5000L); // 开工于 now=0

            BuildJob a = queue.GetJob("a");

            Assert.AreEqual(5000L, a.RemainingMs(0L));
            Assert.IsFalse(a.IsComplete(0L));

            Assert.AreEqual(2000L, a.RemainingMs(3000L));
            Assert.IsFalse(a.IsComplete(4999L));

            Assert.AreEqual(0L, a.RemainingMs(5000L));
            Assert.IsTrue(a.IsComplete(5000L)); // 边界：恰好到时即完成

            Assert.AreEqual(0L, a.RemainingMs(9000L)); // 超过仍钳为 0
            Assert.IsTrue(a.IsComplete(9000L));
        }

        [Test]
        public void GetJob_Missing_ReturnsNull()
        {
            BuildQueue queue = new BuildQueue(1, Provider);
            Assert.IsNull(queue.GetJob("nope"));
            Assert.IsNull(queue.GetJob(null));
        }

        [Test]
        public void Constructor_SlotsLessThanOne_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BuildQueue(0, Provider));
        }

        [Test]
        public void Constructor_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BuildQueue(1, null));
        }

        [Test]
        public void CompletedId_CanBeReused_AfterReaping()
        {
            // 完成收割后该 id 从已知集合移除，可被再次入队。
            BuildQueue queue = new BuildQueue(1, Provider);
            queue.Enqueue("a", 1000L);

            m_Now = 1000L;
            queue.CompletePending(null);
            Assert.IsNull(queue.GetJob("a"));

            Assert.IsTrue(queue.Enqueue("a", 500L)); // 复用旧 id
            Assert.IsTrue(queue.GetJob("a").IsStarted);
            Assert.AreEqual(1000L, queue.GetJob("a").StartedEpochMs);
        }
    }
}
