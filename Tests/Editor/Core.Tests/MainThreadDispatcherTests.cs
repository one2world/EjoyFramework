//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Scheduling;

namespace EjoyFramework.Tests
{
    /// <summary>WS1-M4：主线程派发器。</summary>
    public sealed class MainThreadDispatcherTests
    {
        private MainThreadDispatcher m_Dispatcher;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Dispatcher = new MainThreadDispatcher();
        }

        [TearDown]
        public void TearDown()
        {
            m_Dispatcher.Shutdown();
        }

        [Test]
        public void Post_FromWorkerThread_ExecutesOnUpdate_InOrder()
        {
            var order = new List<int>();
            var t = new Thread(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    int captured = i;
                    m_Dispatcher.Post(() => order.Add(captured));
                }
            });
            t.Start();
            t.Join();

            Assert.AreEqual(5, m_Dispatcher.PendingCount);
            Assert.AreEqual(0, order.Count, "投递不应立即执行。");

            m_Dispatcher.Update(0f, 0f);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, order);
            Assert.AreEqual(0, m_Dispatcher.PendingCount);
            Assert.AreEqual(5, m_Dispatcher.ExecutedCount);
        }

        [Test]
        public void Post_Work_IsReleasedToReferencePoolAfterExecute()
        {
            CounterWork work = ReferencePool.Acquire<CounterWork>();
            work.Target = new int[1];
            m_Dispatcher.Post(work);
            m_Dispatcher.Update(0f, 0f);

            Assert.AreEqual(1, work.Target == null ? 1 : 0, "执行后应被 Clear（归还引用池）。");
            Assert.AreEqual(1, CounterWork.TotalExecuted);
        }

        [Test]
        public void ThrowingItem_IsIsolated_AndCounted()
        {
            bool second = false;
            m_Dispatcher.Post(() => { throw new InvalidOperationException("boom"); });
            m_Dispatcher.Post(() => { second = true; });

            m_Dispatcher.Update(0f, 0f);

            Assert.IsTrue(second, "前一项抛异常不能影响后续项。");
            Assert.AreEqual(1, m_Dispatcher.FailedCount);
            Assert.AreEqual(1, m_Dispatcher.ExecutedCount);
        }

        [Test]
        public void SelfReposting_DoesNotSpinForever_RunsNextFrame()
        {
            int runs = 0;
            Action loop = null;
            loop = () => { runs++; m_Dispatcher.Post(loop); };
            m_Dispatcher.Post(loop);

            m_Dispatcher.Update(0f, 0f);
            Assert.AreEqual(1, runs, "执行中新投递的项应留到下一帧。");
            m_Dispatcher.Update(0f, 0f);
            Assert.AreEqual(2, runs);
        }

        [Test]
        public void MaxDrainMilliseconds_SpreadsBurstAcrossFrames()
        {
            m_Dispatcher.MaxDrainMilliseconds = 1f;
            int executed = 0;
            for (int i = 0; i < 20; i++)
            {
                m_Dispatcher.Post(() => { executed++; Thread.SpinWait(200000); });   // 每项 ≈ 若干百微秒
            }

            m_Dispatcher.Update(0f, 0f);
            int firstFrame = executed;
            Assert.Greater(firstFrame, 0);
            Assert.Less(firstFrame, 20, "1 ms 预算下 20 个耗时项不应在一帧内全部执行。");

            for (int frame = 0; frame < 50 && executed < 20; frame++) m_Dispatcher.Update(0f, 0f);
            Assert.AreEqual(20, executed, "剩余项应在后续帧按序执行完。");
        }

        [Test]
        public void Shutdown_ReleasesPendingWorkItems_WithoutExecuting()
        {
            int before = CounterWork.TotalExecuted;
            CounterWork work = ReferencePool.Acquire<CounterWork>();
            work.Target = new int[1];
            m_Dispatcher.Post(work);
            m_Dispatcher.Shutdown();

            Assert.AreEqual(before, CounterWork.TotalExecuted);
            Assert.IsNull(work.Target, "关闭时未执行的工作项应被归还（Clear）。");
            Assert.AreEqual(0, m_Dispatcher.PendingCount);
        }

        [Test]
        public void IsMainThread_ReflectsMarkedThread()
        {
            Assert.IsTrue(m_Dispatcher.IsMainThread);
            bool onWorker = true;
            var t = new Thread(() => { onWorker = m_Dispatcher.IsMainThread; });
            t.Start();
            t.Join();
            Assert.IsFalse(onWorker);
        }

        [Test]
        public void PostWork_And_Drain_SteadyState_DoNotAllocate()
        {
            TestDelegate body = () =>
            {
                for (int i = 0; i < 16; i++)
                {
                    CounterWork w = ReferencePool.Acquire<CounterWork>();
                    m_Dispatcher.Post(w);
                }

                m_Dispatcher.Update(0f, 0f);
            };
            body();
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        private sealed class CounterWork : IMainThreadWork
        {
            public static int TotalExecuted;
            public int[] Target;

            public void Execute()
            {
                TotalExecuted++;
                if (Target != null) Target[0]++;
            }

            public void Clear()
            {
                Target = null;
            }
        }
    }
}
