//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Production
{
    /// <summary>
    /// 资源产出器（挂机/离线累积）：按 <see cref="RatePerSec"/> 随真实流逝时间向缓冲区累积，
    /// 上限为 <see cref="Capacity"/>。<see cref="Collect"/> 一次性领取并清空累积。
    /// </summary>
    /// <remarks>
    /// 纯逻辑、与引擎无关。时间通过 <c>Func&lt;long&gt;</c>（返回纪元毫秒）注入，便于离线/挂机累积的确定性测试。
    /// <para>
    /// <b>累积模型（carry model）</b>：内部维护两部分——
    /// <list type="bullet">
    /// <item>m_Carried：已“封存”的待领取量（来自上一次 <see cref="SetRate"/>/<see cref="SetCapacity"/> 之前累积的部分）；</item>
    /// <item>实时累积量：<c>RatePerSec * 自基线以来的秒数</c>。</item>
    /// </list>
    /// <see cref="Pending"/> = min(Capacity, m_Carried + 实时累积量)，即两部分之和再统一按 <see cref="Capacity"/> 封顶。
    /// </para>
    /// <para>
    /// <b><see cref="SetRate"/> 重定基</b>：先把当前 <see cref="Pending"/>（已封顶）封存到 m_Carried，
    /// 再把基线重置为 now，之后新累积按新速率叠加在 m_Carried 之上（整体仍按 <see cref="Capacity"/> 封顶）。
    /// 这样变更速率不会丢失已累积量，也不会因变更而瞬间溢出。
    /// </para>
    /// <para>
    /// <b><see cref="OnFull"/> 触发点</b>：在任意一次会重新计算 <see cref="Pending"/> 的读取
    /// （即读取 <see cref="Pending"/>/<see cref="IsFull"/>，或调用 <see cref="Collect"/>/<see cref="SetRate"/>/<see cref="SetCapacity"/>）
    /// 中，当 <see cref="Pending"/> 首次达到 <see cref="Capacity"/> 时触发一次；在被领取/扩容而重新低于上限之前不会重复触发。
    /// </para>
    /// </remarks>
    public sealed class ResourceProducer
    {
        private readonly Func<long> m_NowProvider;
        private double m_RatePerSec;
        private double m_Capacity;
        private double m_Carried;
        private long m_BaselineEpochMs;
        private bool m_FullFired;

        /// <summary>
        /// 构造资源产出器。
        /// </summary>
        /// <param name="ratePerSec">每秒产出速率，必须 &gt;= 0。</param>
        /// <param name="capacity">缓冲区容量上限，必须 &gt;= 0。</param>
        /// <param name="nowEpochMsProvider">返回当前纪元毫秒的时间提供器，不可为空。</param>
        /// <exception cref="ArgumentNullException"><paramref name="nowEpochMsProvider"/> 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ratePerSec"/> 或 <paramref name="capacity"/> 为负。</exception>
        public ResourceProducer(double ratePerSec, double capacity, Func<long> nowEpochMsProvider)
        {
            if (nowEpochMsProvider == null)
            {
                throw new ArgumentNullException(nameof(nowEpochMsProvider));
            }

            if (ratePerSec < 0d || double.IsNaN(ratePerSec))
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSec), "ratePerSec 必须 >= 0。");
            }

            if (capacity < 0d || double.IsNaN(capacity))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity 必须 >= 0。");
            }

            m_NowProvider = nowEpochMsProvider;
            m_RatePerSec = ratePerSec;
            m_Capacity = capacity;
            m_Carried = 0d;
            m_BaselineEpochMs = m_NowProvider();
            m_FullFired = false;
        }

        /// <summary>
        /// 当 <see cref="Pending"/> 首次达到 <see cref="Capacity"/> 时触发（在一次重新计算 Pending 的读取中触发一次）。
        /// </summary>
        public event Action<ResourceProducer> OnFull;

        /// <summary>
        /// 每秒产出速率。
        /// </summary>
        public double RatePerSec
        {
            get { return m_RatePerSec; }
        }

        /// <summary>
        /// 缓冲区容量上限。
        /// </summary>
        public double Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>
        /// 截至当前 now、已累积但尚未领取的量（按 <see cref="Capacity"/> 封顶）。
        /// </summary>
        /// <remarks>读取本属性会基于当前时间重新计算，并可能触发 <see cref="OnFull"/>。</remarks>
        public double Pending
        {
            get { return ComputePending(); }
        }

        /// <summary>
        /// 当前累积量是否已达到 <see cref="Capacity"/>。
        /// </summary>
        /// <remarks>读取本属性会基于当前时间重新计算，并可能触发 <see cref="OnFull"/>。</remarks>
        public bool IsFull
        {
            get { return ComputePending() >= m_Capacity; }
        }

        /// <summary>
        /// 领取当前 <see cref="Pending"/>：返回该值并把累积基线重置为 now（清空已领取量）。
        /// </summary>
        /// <returns>本次领取的量。</returns>
        public double Collect()
        {
            double pending = ComputePending();
            m_Carried = 0d;
            m_BaselineEpochMs = m_NowProvider();
            m_FullFired = false;
            return pending;
        }

        /// <summary>
        /// 设置新的每秒产出速率：先封存当前 <see cref="Pending"/> 再重定基线，已累积量不丢失。
        /// </summary>
        /// <param name="ratePerSec">新的每秒产出速率，必须 &gt;= 0。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="ratePerSec"/> 为负或 NaN。</exception>
        public void SetRate(double ratePerSec)
        {
            if (ratePerSec < 0d || double.IsNaN(ratePerSec))
            {
                throw new ArgumentOutOfRangeException(nameof(ratePerSec), "ratePerSec 必须 >= 0。");
            }

            // 先把当前 Pending（已封顶）封存为携带量，再重定基线，最后切换速率。
            m_Carried = ComputePending();
            m_BaselineEpochMs = m_NowProvider();
            m_RatePerSec = ratePerSec;
        }

        /// <summary>
        /// 设置新的容量上限：先封存当前 <see cref="Pending"/> 再重定基线（按新上限重新封顶）。
        /// </summary>
        /// <param name="capacity">新的容量上限，必须 &gt;= 0。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> 为负或 NaN。</exception>
        public void SetCapacity(double capacity)
        {
            if (capacity < 0d || double.IsNaN(capacity))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity 必须 >= 0。");
            }

            // 先按旧上限封存当前 Pending，再切换上限；若扩容则给满状态留出继续累积的空间。
            m_Carried = ComputePending();
            m_BaselineEpochMs = m_NowProvider();
            m_Capacity = capacity;

            // 扩容后可能已不再满，允许 OnFull 再次触发。
            if (m_Carried < m_Capacity)
            {
                m_FullFired = false;
            }
        }

        /// <summary>
        /// 基于当前 now 计算 <see cref="Pending"/>，并在首次达到上限时触发 <see cref="OnFull"/>。
        /// </summary>
        private double ComputePending()
        {
            long now = m_NowProvider();
            long elapsedMs = now - m_BaselineEpochMs;
            if (elapsedMs < 0L)
            {
                // now 回退（时钟异常）时按 0 处理，避免负累积。
                elapsedMs = 0L;
            }

            double accrued = m_Carried + m_RatePerSec * (elapsedMs / 1000d);
            double pending = accrued;
            if (pending > m_Capacity)
            {
                pending = m_Capacity;
            }

            if (pending >= m_Capacity && m_Capacity > 0d)
            {
                if (!m_FullFired)
                {
                    m_FullFired = true;
                    Action<ResourceProducer> handler = OnFull;
                    if (handler != null)
                    {
                        handler(this);
                    }
                }
            }

            return pending;
        }
    }
}
