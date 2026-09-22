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
    /// <summary>WS1-M4：帧预算调度器。</summary>
    public sealed class FrameBudgetSchedulerTests
    {
        private FrameBudgetScheduler m_Scheduler;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Scheduler = new FrameBudgetScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            m_Scheduler.Shutdown();
        }

        [Test]
        public void Schedule_ProgressesAcrossFrames_AndCompletes()
        {
            var task = new CountingTask(steps: 5);
            ScheduledTask handle = m_Scheduler.Schedule(task);
            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(ScheduledTaskStatus.Pending, m_Scheduler.GetStatus(handle));

            m_Scheduler.BudgetMilliseconds = 0f;   // 每帧只保证 1 步
            for (int i = 0; i < 4; i++)
            {
                m_Scheduler.Update(0f, 0f);
                Assert.AreEqual(ScheduledTaskStatus.Pending, m_Scheduler.GetStatus(handle));
            }

            m_Scheduler.Update(0f, 0f);
            Assert.AreEqual(ScheduledTaskStatus.Completed, m_Scheduler.GetStatus(handle));
            Assert.AreEqual(5, task.Steps);
            Assert.AreEqual(0, m_Scheduler.PendingCount);
            Assert.AreEqual(1, m_Scheduler.CompletedCount);
        }

        [Test]
        public void Budget_LimitsWorkPerFrame_ButAlwaysMakesProgress()
        {
            var slow = new SlowTask(steps: 10, spinPerStep: 300000);   // 每步若干百微秒
            m_Scheduler.Schedule(slow);
            m_Scheduler.BudgetMilliseconds = 0.5f;

            m_Scheduler.Update(0f, 0f);
            Assert.GreaterOrEqual(m_Scheduler.LastFrameSteps, 1, "至少推进一步。");
            Assert.Less(slow.Steps, 10, "预算内不应一帧做完。");
            Assert.Greater(m_Scheduler.LastFrameMilliseconds, 0f);

            for (int i = 0; i < 100 && slow.Steps < 10; i++) m_Scheduler.Update(0f, 0f);
            Assert.AreEqual(10, slow.Steps);
        }

        [Test]
        public void HigherPriority_RunsToCompletionBeforeLower_SameFifo()
        {
            var order = new List<string>();
            m_Scheduler.Schedule(new LogTask("low-a", 2, order), priority: 10);
            m_Scheduler.Schedule(new LogTask("high", 2, order), priority: 0);
            m_Scheduler.Schedule(new LogTask("low-b", 2, order), priority: 10);

            m_Scheduler.BudgetMilliseconds = 100f;
            m_Scheduler.Update(0f, 0f);

            CollectionAssert.AreEqual(new[] { "high", "high", "low-a", "low-a", "low-b", "low-b" }, order);
        }

        [Test]
        public void Cancel_RemovesTask_InvokesOnCancelled_AndInvalidatesHandle()
        {
            var task = new CountingTask(steps: 3);
            ScheduledTask handle = m_Scheduler.Schedule(task);

            Assert.IsTrue(m_Scheduler.Cancel(handle));
            Assert.IsTrue(task.Cancelled);
            Assert.AreEqual(ScheduledTaskStatus.Cancelled, m_Scheduler.GetStatus(handle));
            Assert.IsFalse(m_Scheduler.Cancel(handle), "重复取消应返回 false。");

            // 槽位复用后，旧句柄不得指向新任务。
            ScheduledTask reused = m_Scheduler.Schedule(new CountingTask(1));
            Assert.AreEqual(handle.Id, reused.Id, "应复用同一槽位（测试前提）。");
            Assert.AreNotEqual(handle.Version, reused.Version);
            Assert.AreEqual(ScheduledTaskStatus.None, m_Scheduler.GetStatus(handle));
            Assert.AreEqual(ScheduledTaskStatus.Pending, m_Scheduler.GetStatus(reused));
        }

        [Test]
        public void ThrowingStep_FailsTask_OthersContinue()
        {
            var bad = new ThrowingTask();
            var good = new CountingTask(steps: 1);
            ScheduledTask badHandle = m_Scheduler.Schedule(bad, 0);
            ScheduledTask goodHandle = m_Scheduler.Schedule(good, 1);

            m_Scheduler.BudgetMilliseconds = 100f;
            m_Scheduler.Update(0f, 0f);

            Assert.AreEqual(ScheduledTaskStatus.Failed, m_Scheduler.GetStatus(badHandle));
            Assert.AreEqual(ScheduledTaskStatus.Completed, m_Scheduler.GetStatus(goodHandle));
            Assert.AreEqual(1, m_Scheduler.FailedCount);
        }

        [Test]
        public void RunToCompletion_IgnoresBudget()
        {
            var task = new CountingTask(steps: 50);
            ScheduledTask handle = m_Scheduler.Schedule(task);
            m_Scheduler.BudgetMilliseconds = 0f;

            Assert.AreEqual(ScheduledTaskStatus.Completed, m_Scheduler.RunToCompletion(handle));
            Assert.AreEqual(50, task.Steps);
            Assert.AreEqual(ScheduledTaskStatus.None, m_Scheduler.RunToCompletion(ScheduledTask.None));
        }

        [Test]
        public void StepThatCancelsItself_IsNotReportedCompleted()
        {
            SelfCancellingTask task = null;
            task = new SelfCancellingTask(() => m_Scheduler.Cancel(task.Handle));
            task.Handle = m_Scheduler.Schedule(task);
            m_Scheduler.Update(0f, 0f);
            Assert.AreEqual(ScheduledTaskStatus.Cancelled, m_Scheduler.GetStatus(task.Handle));
            Assert.AreEqual(0, m_Scheduler.CompletedCount);
        }

        [Test]
        public void Shutdown_CancelsPending()
        {
            var task = new CountingTask(steps: 3);
            m_Scheduler.Schedule(task);
            m_Scheduler.Shutdown();
            Assert.IsTrue(task.Cancelled);
            Assert.AreEqual(0, m_Scheduler.PendingCount);
        }

        [Test]
        public void ScheduleStepComplete_SteadyState_DoesNotAllocate()
        {
            var tasks = new CountingTask[8];
            for (int i = 0; i < tasks.Length; i++) tasks[i] = new CountingTask(steps: 2);
            m_Scheduler.BudgetMilliseconds = 100f;

            TestDelegate body = () =>
            {
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i].Reset();
                    m_Scheduler.Schedule(tasks[i], i % 3);
                }

                m_Scheduler.Update(0f, 0f);
            };
            body();
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
            Assert.AreEqual(0, m_Scheduler.PendingCount);
        }

        // ================================================================
        //  test doubles
        // ================================================================

        private class CountingTask : IBudgetedTask
        {
            private readonly int m_Total;
            public int Steps;
            public bool Cancelled;

            public CountingTask(int steps) { m_Total = steps; }
            public void Reset() { Steps = 0; Cancelled = false; }
            public virtual bool Step() { Steps++; return Steps >= m_Total; }
            public void OnCancelled() { Cancelled = true; }
        }

        private sealed class SlowTask : CountingTask
        {
            private readonly int m_Spin;
            public SlowTask(int steps, int spinPerStep) : base(steps) { m_Spin = spinPerStep; }
            public override bool Step() { Thread.SpinWait(m_Spin); return base.Step(); }
        }

        private sealed class LogTask : IBudgetedTask
        {
            private readonly string m_Name;
            private readonly int m_Total;
            private readonly List<string> m_Log;
            private int m_Done;

            public LogTask(string name, int total, List<string> log) { m_Name = name; m_Total = total; m_Log = log; }
            public bool Step() { m_Log.Add(m_Name); return ++m_Done >= m_Total; }
            public void OnCancelled() { }
        }

        private sealed class ThrowingTask : IBudgetedTask
        {
            public bool Step() { throw new InvalidOperationException("boom"); }
            public void OnCancelled() { }
        }

        private sealed class SelfCancellingTask : IBudgetedTask
        {
            private readonly Action m_OnStep;
            public ScheduledTask Handle;
            public SelfCancellingTask(Action onStep) { m_OnStep = onStep; }
            public bool Step() { m_OnStep(); return true; }
            public void OnCancelled() { }
        }
    }
}
