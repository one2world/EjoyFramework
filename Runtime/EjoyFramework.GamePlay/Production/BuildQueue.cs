//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Production
{
    /// <summary>
    /// 建造队列：提供有限个并发建造槽位，超出槽位的任务按 FIFO 排队等待空闲槽位。
    /// </summary>
    /// <remarks>
    /// 纯逻辑、与引擎无关。时间通过 <c>Func&lt;long&gt;</c>（返回纪元毫秒）注入，便于确定性测试。
    /// <para>
    /// <b>开工时机</b>：<see cref="Enqueue"/> 时若有空闲槽位则立即开工（StartedEpochMs=now），否则入队等待。
    /// </para>
    /// <para>
    /// <b>完成结算</b>：完成是被动结算的——<see cref="CompletePending"/> 找出所有已完成的在建任务并移出（触发
    /// <see cref="OnJobCompleted"/> 并追加到 finishedInto），随后按 FIFO 把排队任务拉入空出的槽位并以 now 开工；
    /// 若某个刚开工的任务时长为 0（已即刻完成）则在同一次调用内继续结算，直到稳定。
    /// </para>
    /// <para>
    /// <b><see cref="FinishNow"/></b>：将任务标记为相对 now 立即完成，但<em>不</em>就地移出，由下一次
    /// <see cref="CompletePending"/> 统一收割（保证完成事件与槽位晋升集中、顺序一致）。
    /// </para>
    /// </remarks>
    public sealed class BuildQueue
    {
        private readonly int m_Slots;
        private readonly Func<long> m_NowProvider;

        // 在建任务（已开工），按 jobId 索引；最多 m_Slots 个。
        private readonly Dictionary<string, BuildJob> m_Active;

        // 在建任务的开工顺序（用于确定性遍历，最早开工在前）。
        private readonly List<string> m_ActiveOrder;

        // 排队任务（未开工），FIFO。
        private readonly List<BuildJob> m_Queued;

        // 全部已知 jobId（在建 + 排队），用于去重判定。
        private readonly HashSet<string> m_KnownIds;

        /// <summary>
        /// 构造建造队列。
        /// </summary>
        /// <param name="slots">并发建造槽位数，必须 &gt;= 1。</param>
        /// <param name="nowEpochMsProvider">返回当前纪元毫秒的时间提供器，不可为空。</param>
        /// <exception cref="ArgumentNullException"><paramref name="nowEpochMsProvider"/> 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slots"/> 小于 1。</exception>
        public BuildQueue(int slots, Func<long> nowEpochMsProvider)
        {
            if (nowEpochMsProvider == null)
            {
                throw new ArgumentNullException(nameof(nowEpochMsProvider));
            }

            if (slots < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(slots), "slots 必须 >= 1。");
            }

            m_Slots = slots;
            m_NowProvider = nowEpochMsProvider;
            m_Active = new Dictionary<string, BuildJob>(slots);
            m_ActiveOrder = new List<string>(slots);
            m_Queued = new List<BuildJob>();
            m_KnownIds = new HashSet<string>();
        }

        /// <summary>
        /// 某个建造任务在 <see cref="CompletePending"/> 收割完成时触发；参数为队列自身与完成的任务标识。
        /// </summary>
        public event Action<BuildQueue, string> OnJobCompleted;

        /// <summary>
        /// 并发建造槽位数。
        /// </summary>
        public int Slots
        {
            get { return m_Slots; }
        }

        /// <summary>
        /// 当前在建任务数。
        /// </summary>
        public int ActiveCount
        {
            get { return m_Active.Count; }
        }

        /// <summary>
        /// 当前排队等待任务数。
        /// </summary>
        public int QueuedCount
        {
            get { return m_Queued.Count; }
        }

        /// <summary>
        /// 在建任务标识（按开工顺序，最早在前）。
        /// </summary>
        public IEnumerable<string> ActiveJobIds
        {
            get
            {
                // 返回快照，避免调用方在遍历期间被内部变更影响（Mono/IL2CPP 下边遍历边改字典/列表有风险）。
                return new List<string>(m_ActiveOrder);
            }
        }

        /// <summary>
        /// 排队任务标识（FIFO，队首在前）。
        /// </summary>
        public IEnumerable<string> QueuedJobIds
        {
            get
            {
                List<string> ids = new List<string>(m_Queued.Count);
                for (int i = 0; i < m_Queued.Count; i++)
                {
                    ids.Add(m_Queued[i].Id);
                }

                return ids;
            }
        }

        /// <summary>
        /// 入队一个建造任务：有空闲槽位则立即开工，否则 FIFO 排队。
        /// </summary>
        /// <param name="jobId">任务标识，不可为空且不可与现有（在建或排队）任务重复。</param>
        /// <param name="durationMs">建造时长（毫秒），负值按 0 处理。</param>
        /// <returns>成功入队/开工返回 true；标识为空或重复返回 false。</returns>
        public bool Enqueue(string jobId, long durationMs)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return false;
            }

            if (m_KnownIds.Contains(jobId))
            {
                return false;
            }

            BuildJob job = new BuildJob(jobId, durationMs);
            m_KnownIds.Add(jobId);

            if (m_Active.Count < m_Slots)
            {
                long now = m_NowProvider();
                job.Start(now);
                m_Active.Add(jobId, job);
                m_ActiveOrder.Add(jobId);
            }
            else
            {
                m_Queued.Add(job);
            }

            return true;
        }

        /// <summary>
        /// 缩短某任务的剩余时间。在建任务等价于把开工时间前移；排队任务直接削减时长。下限均为 0。
        /// </summary>
        /// <param name="jobId">任务标识。</param>
        /// <param name="reduceMs">缩短的毫秒数，非正值不产生效果但仍视任务存在与否返回。</param>
        /// <returns>任务存在返回 true；不存在返回 false。</returns>
        public bool SpeedUp(string jobId, long reduceMs)
        {
            BuildJob job = GetJob(jobId);
            if (job == null)
            {
                return false;
            }

            job.Reduce(reduceMs, m_NowProvider());
            return true;
        }

        /// <summary>
        /// 强制完成一个在建任务：标记为相对 now 立即完成，由下一次 <see cref="CompletePending"/> 收割。
        /// </summary>
        /// <param name="jobId">任务标识，必须是在建任务。</param>
        /// <returns>该任务为在建任务并被标记完成返回 true；否则（不存在或仍在排队）返回 false。</returns>
        public bool FinishNow(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return false;
            }

            BuildJob job;
            if (!m_Active.TryGetValue(jobId, out job))
            {
                return false;
            }

            job.ForceComplete(m_NowProvider());
            return true;
        }

        /// <summary>
        /// 结算所有已完成的在建任务：移出已完成任务（触发 <see cref="OnJobCompleted"/> 并追加到
        /// <paramref name="finishedInto"/>），随后按 FIFO 把排队任务拉入空出槽位并以 now 开工；
        /// 若新开工任务已即刻完成（时长 0）则继续结算直至稳定。
        /// </summary>
        /// <param name="finishedInto">用于追加本次完成的任务标识的列表，可为空（仅计数）。</param>
        /// <returns>本次完成的任务数量。</returns>
        public int CompletePending(List<string> finishedInto)
        {
            int finishedCount = 0;

            while (true)
            {
                long now = m_NowProvider();

                // 1) 收割已完成的在建任务。对开工顺序的快照遍历，避免边遍历边改集合。
                List<string> completedThisRound = null;
                List<string> orderSnapshot = new List<string>(m_ActiveOrder);
                for (int i = 0; i < orderSnapshot.Count; i++)
                {
                    string id = orderSnapshot[i];
                    BuildJob job;
                    if (!m_Active.TryGetValue(id, out job))
                    {
                        continue;
                    }

                    if (job.IsComplete(now))
                    {
                        if (completedThisRound == null)
                        {
                            completedThisRound = new List<string>();
                        }

                        completedThisRound.Add(id);
                    }
                }

                if (completedThisRound != null)
                {
                    for (int i = 0; i < completedThisRound.Count; i++)
                    {
                        string id = completedThisRound[i];
                        m_Active.Remove(id);
                        m_ActiveOrder.Remove(id);
                        m_KnownIds.Remove(id);
                        finishedCount++;

                        if (finishedInto != null)
                        {
                            finishedInto.Add(id);
                        }

                        Action<BuildQueue, string> handler = OnJobCompleted;
                        if (handler != null)
                        {
                            handler(this, id);
                        }
                    }
                }

                // 2) 把排队任务拉入空出的槽位并开工。
                bool promotedAny = false;
                while (m_Active.Count < m_Slots && m_Queued.Count > 0)
                {
                    BuildJob next = m_Queued[0];
                    m_Queued.RemoveAt(0);
                    next.Start(now);
                    m_Active.Add(next.Id, next);
                    m_ActiveOrder.Add(next.Id);
                    promotedAny = true;
                }

                // 3) 若本轮有完成或有晋升，可能存在“晋升即完成（时长 0）”的连锁，继续循环直到稳定。
                bool madeProgress = (completedThisRound != null && completedThisRound.Count > 0) || promotedAny;
                if (!madeProgress)
                {
                    break;
                }
            }

            return finishedCount;
        }

        /// <summary>
        /// 按标识获取任务（在建或排队均可）。
        /// </summary>
        /// <param name="jobId">任务标识。</param>
        /// <returns>找到的任务；不存在返回 null。</returns>
        public BuildJob GetJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return null;
            }

            BuildJob job;
            if (m_Active.TryGetValue(jobId, out job))
            {
                return job;
            }

            for (int i = 0; i < m_Queued.Count; i++)
            {
                if (m_Queued[i].Id == jobId)
                {
                    return m_Queued[i];
                }
            }

            return null;
        }
    }
}
