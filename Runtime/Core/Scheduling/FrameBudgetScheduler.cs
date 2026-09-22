//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EjoyFramework.Core.Scheduling
{
    /// <summary>
    /// <see cref="IFrameBudgetScheduler"/> 实现。
    ///
    /// 数据结构：槽位数组（id = 槽位下标，版本号防 ABA）+ 按优先级排序的活动列表（插入时按优先级/序号找位置，
    /// 任务数通常在几十以内，线性插入比堆更简单且缓存友好；完成/取消从头部或任意位置 RemoveAt）。
    /// 稳态零分配：槽位与活动列表容量复用，空闲槽位走空闲栈。
    /// </summary>
    internal sealed class FrameBudgetScheduler : FrameworkModule, IFrameBudgetScheduler
    {
        private struct Slot
        {
            public IBudgetedTask Task;
            public int Version;
            public int Priority;
            public long Sequence;
            public ScheduledTaskStatus Status;
        }

        private Slot[] m_Slots = new Slot[16];
        private readonly Stack<int> m_FreeSlots = new Stack<int>();
        private readonly List<int> m_Active = new List<int>();   // 槽位下标，按 (Priority, Sequence) 升序
        private int m_SlotCount;
        private long m_NextSequence;
        private float m_BudgetMilliseconds = 2f;
        private float m_LastFrameMilliseconds;
        private int m_LastFrameSteps;
        private long m_CompletedCount;
        private long m_FailedCount;
        private bool m_Stepping;

        /// <summary>晚于派发器、早于业务：业务模块本帧提交的任务下一帧才推进，行为可预测。</summary>
        public override int Priority
        {
            get { return 80; }
        }

        public float BudgetMilliseconds
        {
            get { return m_BudgetMilliseconds; }
            set { m_BudgetMilliseconds = value < 0f ? 0f : value; }
        }

        public int PendingCount
        {
            get { return m_Active.Count; }
        }

        public float LastFrameMilliseconds
        {
            get { return m_LastFrameMilliseconds; }
        }

        public int LastFrameSteps
        {
            get { return m_LastFrameSteps; }
        }

        public long CompletedCount
        {
            get { return m_CompletedCount; }
        }

        public long FailedCount
        {
            get { return m_FailedCount; }
        }

        // ================================================================
        //  提交 / 取消 / 查询
        // ================================================================

        public ScheduledTask Schedule(IBudgetedTask task, int priority = 0)
        {
            Framework.EnsureMainThread("FrameBudgetScheduler.Schedule");
            if (task == null)
            {
                throw new FrameworkException("FrameBudgetScheduler.Schedule：task 不能为 null。");
            }

            int id = AcquireSlot();
            ref Slot slot = ref m_Slots[id];
            slot.Task = task;
            slot.Priority = priority;
            slot.Sequence = m_NextSequence++;
            slot.Status = ScheduledTaskStatus.Pending;
            // 版本号从 1 起且永不为 0：default(ScheduledTask) 永远无效。
            slot.Version = slot.Version == int.MaxValue ? 1 : slot.Version + 1;

            InsertActive(id);
            return new ScheduledTask(id, slot.Version);
        }

        public bool Cancel(ScheduledTask handle)
        {
            Framework.EnsureMainThread("FrameBudgetScheduler.Cancel");
            if (!TryResolve(handle, out int id) || m_Slots[id].Status != ScheduledTaskStatus.Pending)
            {
                return false;
            }

            IBudgetedTask task = m_Slots[id].Task;
            Finish(id, ScheduledTaskStatus.Cancelled);
            try { task.OnCancelled(); }
            catch (Exception ex) { FrameworkLog.Error("FrameBudgetScheduler：OnCancelled 抛出异常：{0}", ex); }
            return true;
        }

        public ScheduledTaskStatus GetStatus(ScheduledTask handle)
        {
            return TryResolve(handle, out int id) ? m_Slots[id].Status : ScheduledTaskStatus.None;
        }

        public ScheduledTaskStatus RunToCompletion(ScheduledTask handle)
        {
            Framework.EnsureMainThread("FrameBudgetScheduler.RunToCompletion");
            if (!TryResolve(handle, out int id))
            {
                return ScheduledTaskStatus.None;
            }

            if (m_Stepping)
            {
                throw new FrameworkException("FrameBudgetScheduler.RunToCompletion：不能在任务 Step 内部重入调用。");
            }

            m_Stepping = true;
            try
            {
                while (m_Slots[id].Status == ScheduledTaskStatus.Pending && m_Slots[id].Version == handle.Version)
                {
                    StepOne(id);
                }
            }
            finally
            {
                m_Stepping = false;
            }

            return GetStatus(handle);
        }

        // ================================================================
        //  推进
        // ================================================================

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_LastFrameSteps = 0;
            if (m_Active.Count == 0 || m_Stepping)
            {
                m_LastFrameMilliseconds = 0f;
                return;
            }

            long start = Stopwatch.GetTimestamp();
            long budgetTicks = (long)(m_BudgetMilliseconds * Stopwatch.Frequency / 1000.0);
            long deadline = start + budgetTicks;

            m_Stepping = true;
            try
            {
                // 至少推进一步（进度保证），之后只要还有预算就继续；每步之后重新取头部（任务完成后头部会变）。
                do
                {
                    if (m_Active.Count == 0)
                    {
                        break;
                    }

                    StepOne(m_Active[0]);
                    m_LastFrameSteps++;
                }
                while (Stopwatch.GetTimestamp() < deadline);
            }
            finally
            {
                m_Stepping = false;
            }

            m_LastFrameMilliseconds = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }

        private void StepOne(int id)
        {
            IBudgetedTask task = m_Slots[id].Task;
            bool done;
            try
            {
                done = task.Step();
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("FrameBudgetScheduler：任务 Step 抛出异常，任务已移除：{0}", ex);
                m_FailedCount++;
                Finish(id, ScheduledTaskStatus.Failed);
                return;
            }

            // Step 内部可能取消了自己或别的任务：只有仍在 Pending 的才按完成处理。
            if (done && m_Slots[id].Status == ScheduledTaskStatus.Pending)
            {
                m_CompletedCount++;
                Finish(id, ScheduledTaskStatus.Completed);
            }
        }

        // ================================================================
        //  槽位
        // ================================================================

        private int AcquireSlot()
        {
            if (m_FreeSlots.Count > 0)
            {
                return m_FreeSlots.Pop();
            }

            if (m_SlotCount == m_Slots.Length)
            {
                Array.Resize(ref m_Slots, m_Slots.Length * 2);
            }

            return m_SlotCount++;
        }

        private void InsertActive(int id)
        {
            int priority = m_Slots[id].Priority;
            long sequence = m_Slots[id].Sequence;
            int index = m_Active.Count;
            // 从尾部向前找到第一个 (Priority, Sequence) 更小的位置之后插入：同优先级按提交顺序。
            while (index > 0)
            {
                ref Slot prev = ref m_Slots[m_Active[index - 1]];
                if (prev.Priority < priority || (prev.Priority == priority && prev.Sequence < sequence))
                {
                    break;
                }

                index--;
            }

            m_Active.Insert(index, id);
        }

        /// <summary>置终态并释放槽位（版本号保留，让旧句柄查询得到 None 而不是复用后的新任务）。</summary>
        private void Finish(int id, ScheduledTaskStatus status)
        {
            ref Slot slot = ref m_Slots[id];
            slot.Status = status;
            slot.Task = null;
            int index = m_Active.IndexOf(id);
            if (index >= 0)
            {
                m_Active.RemoveAt(index);
            }

            m_FreeSlots.Push(id);
        }

        private bool TryResolve(ScheduledTask handle, out int id)
        {
            id = handle.Id;
            return handle.Version != 0 && id >= 0 && id < m_SlotCount && m_Slots[id].Version == handle.Version;
        }

        public override void Shutdown()
        {
            for (int i = 0; i < m_Active.Count; i++)
            {
                IBudgetedTask task = m_Slots[m_Active[i]].Task;
                m_Slots[m_Active[i]].Status = ScheduledTaskStatus.Cancelled;
                m_Slots[m_Active[i]].Task = null;
                try { task.OnCancelled(); }
                catch (Exception ex) { FrameworkLog.Error("FrameBudgetScheduler：关闭时 OnCancelled 抛出异常：{0}", ex); }
            }

            m_Active.Clear();
            m_FreeSlots.Clear();
            m_SlotCount = 0;
        }
    }
}
