//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Production
{
    /// <summary>
    /// 一个建造任务：记录其标识、时长及（若已开工）开工时间。完成与否相对传入的 now 计算。
    /// </summary>
    /// <remarks>
    /// 纯逻辑、与引擎无关。任务由 <see cref="BuildQueue"/> 创建与管理，本类型对外只读，
    /// 状态变更（开工、加速、强制完成）经由 <see cref="BuildQueue"/> 的内部方法。
    /// </remarks>
    public sealed class BuildJob
    {
        private readonly string m_Id;
        private long m_DurationMs;
        private long m_StartedEpochMs;
        private bool m_IsStarted;

        /// <summary>
        /// 构造一个建造任务（默认处于排队未开工状态）。
        /// </summary>
        /// <param name="id">任务标识。</param>
        /// <param name="durationMs">建造时长（毫秒），负值按 0 处理。</param>
        internal BuildJob(string id, long durationMs)
        {
            m_Id = id;
            m_DurationMs = durationMs < 0L ? 0L : durationMs;
            m_StartedEpochMs = 0L;
            m_IsStarted = false;
        }

        /// <summary>
        /// 任务标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 建造时长（毫秒）。
        /// </summary>
        public long DurationMs
        {
            get { return m_DurationMs; }
        }

        /// <summary>
        /// 开工时间（纪元毫秒）；排队未开工时为 0。
        /// </summary>
        public long StartedEpochMs
        {
            get { return m_StartedEpochMs; }
        }

        /// <summary>
        /// 是否已开工（已占用一个建造槽位）。
        /// </summary>
        public bool IsStarted
        {
            get { return m_IsStarted; }
        }

        /// <summary>
        /// 相对给定 now 判断任务是否已完成。
        /// </summary>
        /// <param name="nowEpochMs">当前纪元毫秒。</param>
        /// <returns>已开工且到达预定完成时刻则为 true；未开工恒为 false。</returns>
        public bool IsComplete(long nowEpochMs)
        {
            if (!m_IsStarted)
            {
                return false;
            }

            return nowEpochMs - m_StartedEpochMs >= m_DurationMs;
        }

        /// <summary>
        /// 相对给定 now 计算剩余毫秒数。
        /// </summary>
        /// <param name="nowEpochMs">当前纪元毫秒。</param>
        /// <returns>未开工时返回完整 <see cref="DurationMs"/>；已开工时返回不小于 0 的剩余量。</returns>
        public long RemainingMs(long nowEpochMs)
        {
            if (!m_IsStarted)
            {
                return m_DurationMs;
            }

            long remaining = m_StartedEpochMs + m_DurationMs - nowEpochMs;
            return remaining < 0L ? 0L : remaining;
        }

        /// <summary>
        /// 以 <paramref name="nowEpochMs"/> 为开工时间开工。
        /// </summary>
        internal void Start(long nowEpochMs)
        {
            m_StartedEpochMs = nowEpochMs;
            m_IsStarted = true;
        }

        /// <summary>
        /// 缩短剩余时间：在保持已开工状态时，等价于把开工时间前移，使剩余量减少 <paramref name="reduceMs"/>，下限为 0。
        /// 未开工时直接削减 <see cref="DurationMs"/>，下限为 0。
        /// </summary>
        internal void Reduce(long reduceMs, long nowEpochMs)
        {
            if (reduceMs <= 0L)
            {
                return;
            }

            if (!m_IsStarted)
            {
                m_DurationMs -= reduceMs;
                if (m_DurationMs < 0L)
                {
                    m_DurationMs = 0L;
                }

                return;
            }

            // 将开工时间前移 reduceMs（即剩余量减少 reduceMs），但开工时间不早于使其立即完成的时刻。
            long earliestStart = nowEpochMs - m_DurationMs;
            long newStart = m_StartedEpochMs - reduceMs;
            if (newStart < earliestStart)
            {
                newStart = earliestStart;
            }

            m_StartedEpochMs = newStart;
        }

        /// <summary>
        /// 强制完成：把开工时间前移到使其相对 <paramref name="nowEpochMs"/> 恰好完成。
        /// 若尚未开工，则先以该时刻开工再令其完成。
        /// </summary>
        internal void ForceComplete(long nowEpochMs)
        {
            m_IsStarted = true;
            m_StartedEpochMs = nowEpochMs - m_DurationMs;
        }
    }
}
