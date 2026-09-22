//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;

namespace EjoyFramework.Core.Scheduling
{
    /// <summary>
    /// <see cref="IMainThreadDispatcher"/> 实现。
    ///
    /// 队列不用 <c>ConcurrentQueue</c>：它每 32 次入队分配一个新段，"稳态零分配"不成立。
    /// 这里用双缓冲数组：生产者在短临界区里追加到 pending 缓冲；消费者（Update）先把上一帧没跑完的
    /// drain 缓冲跑完，再在临界区内把 pending 与 drain 交换（O(1)），随后在锁外执行。
    /// 两个缓冲的容量只增不减，稳态无分配；执行完的槽位立即清空引用，不延长业务对象寿命。
    /// </summary>
    internal sealed class MainThreadDispatcher : FrameworkModule, IMainThreadDispatcher
    {
        private struct Item
        {
            public Action Action;
            public IMainThreadWork Work;
        }

        private readonly object m_Lock = new object();
        private Item[] m_Pending = new Item[64];
        private int m_PendingCount;
        private Item[] m_Drain = new Item[64];
        private int m_DrainCount;
        private int m_DrainIndex;
        private int m_TotalPending;   // pending + drain 中未执行的，Interlocked 维护供跨线程读
        private long m_ExecutedCount;
        private long m_FailedCount;
        private float m_MaxDrainMilliseconds;
        private bool m_Draining;

        /// <summary>早于所有业务模块执行：后台线程的结果应在本帧业务逻辑之前可见。</summary>
        public override int Priority
        {
            get { return 90; }
        }

        public int PendingCount
        {
            get { return Volatile.Read(ref m_TotalPending); }
        }

        public float MaxDrainMilliseconds
        {
            get { return m_MaxDrainMilliseconds; }
            set { m_MaxDrainMilliseconds = value; }
        }

        public long ExecutedCount
        {
            get { return Interlocked.Read(ref m_ExecutedCount); }
        }

        public long FailedCount
        {
            get { return Interlocked.Read(ref m_FailedCount); }
        }

        public bool IsMainThread
        {
            get { return Framework.IsMainThread(); }
        }

        public void Post(Action action)
        {
            if (action == null)
            {
                throw new FrameworkException("MainThreadDispatcher.Post：action 不能为 null。");
            }

            Enqueue(action, null);
        }

        public void Post(IMainThreadWork work)
        {
            if (work == null)
            {
                throw new FrameworkException("MainThreadDispatcher.Post：work 不能为 null。");
            }

            Enqueue(null, work);
        }

        private void Enqueue(Action action, IMainThreadWork work)
        {
            lock (m_Lock)
            {
                if (m_PendingCount == m_Pending.Length)
                {
                    Array.Resize(ref m_Pending, m_Pending.Length * 2);
                }

                m_Pending[m_PendingCount].Action = action;
                m_Pending[m_PendingCount].Work = work;
                m_PendingCount++;
            }

            Interlocked.Increment(ref m_TotalPending);
        }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_Draining || Volatile.Read(ref m_TotalPending) == 0)
            {
                return;
            }

            long deadlineTicks = m_MaxDrainMilliseconds > 0f
                ? Stopwatch.GetTimestamp() + (long)(m_MaxDrainMilliseconds * Stopwatch.Frequency / 1000.0)
                : long.MaxValue;

            m_Draining = true;
            try
            {
                // 上一帧因预算截断剩下的先跑完；随后**最多交换一次**拿到 pending 里的项。
                // 只交换一次是关键：执行期间新投递的（含自投递）落在新 pending 里留到下一帧，
                // 否则一个每次都再投递自己的工作项会让本帧永不结束。
                bool swapped = false;
                while (true)
                {
                    if (m_DrainIndex >= m_DrainCount)
                    {
                        if (swapped)
                        {
                            break;
                        }

                        SwapInPending();
                        swapped = true;
                        if (m_DrainCount == 0)
                        {
                            break;
                        }
                    }

                    int index = m_DrainIndex++;
                    Execute(ref m_Drain[index]);
                    m_Drain[index].Action = null;
                    m_Drain[index].Work = null;
                    Interlocked.Decrement(ref m_TotalPending);

                    if (Stopwatch.GetTimestamp() >= deadlineTicks)
                    {
                        break;
                    }
                }
            }
            finally
            {
                m_Draining = false;
            }
        }

        /// <summary>把 pending 缓冲整体换到 drain 侧（O(1)）；pending 为空时 drain 计数归零。</summary>
        private void SwapInPending()
        {
            lock (m_Lock)
            {
                Item[] filled = m_Pending;
                int count = m_PendingCount;
                m_Pending = m_Drain;      // 已执行完、引用已清空的旧 drain 缓冲变成新的 pending 缓冲
                m_PendingCount = 0;
                m_Drain = filled;
                m_DrainCount = count;
                m_DrainIndex = 0;
            }
        }

        private void Execute(ref Item item)
        {
            try
            {
                if (item.Work != null)
                {
                    item.Work.Execute();
                }
                else
                {
                    item.Action();
                }

                Interlocked.Increment(ref m_ExecutedCount);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref m_FailedCount);
                FrameworkLog.Error("MainThreadDispatcher：工作项执行抛出异常：{0}", ex);
            }
            finally
            {
                if (item.Work != null)
                {
                    try { ReferencePool.Release(item.Work); }
                    catch (Exception ex) { FrameworkLog.Error("MainThreadDispatcher：工作项归还抛出异常：{0}", ex); }
                }
            }
        }

        public override void Shutdown()
        {
            // 未执行的工作项直接丢弃（归还池化项，避免泄漏）；委托不执行——关闭时业务对象可能已失效。
            lock (m_Lock)
            {
                ReleaseRange(m_Pending, 0, m_PendingCount);
                m_PendingCount = 0;
            }

            ReleaseRange(m_Drain, m_DrainIndex, m_DrainCount);
            m_DrainCount = 0;
            m_DrainIndex = 0;
            Volatile.Write(ref m_TotalPending, 0);
        }

        private static void ReleaseRange(Item[] items, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (items[i].Work != null)
                {
                    try { ReferencePool.Release(items[i].Work); }
                    catch (Exception ex) { FrameworkLog.Error("MainThreadDispatcher：关闭时归还工作项抛出异常：{0}", ex); }
                }

                items[i].Action = null;
                items[i].Work = null;
            }
        }
    }
}
