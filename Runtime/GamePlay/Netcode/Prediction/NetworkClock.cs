//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Netcode.Prediction
{
    /// <summary>
    /// 引擎无关的「网络时钟」：把本地时间映射到服务器时间（以及渲染/插值时间），并平滑 RTT。
    /// 它不依赖任何传输层，只消费外部喂入的「服务器时间样本」，因此可纯逻辑测试。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 核心量是<b>偏移 offset = serverTime - localTime</b>。每次收到样本时，
    /// 用「样本服务器时间 + 单程时延(rtt/2)」推算「样本到达本地时刻所对应的服务器时间」，
    /// 再减去本地时间得到瞬时 offset，并用指数滑动平均（EMA）平滑，抑制抖动。
    /// </para>
    /// <para>
    /// <see cref="EstimatedServerTimeSec"/> = localTime + 平滑后的 offset；
    /// <see cref="RenderTimeSec"/> = 估计服务器时间 - 插值延迟（为远端实体插值预留缓冲）。
    /// </para>
    /// <para>EMA 系数 <see cref="m_SmoothingFactor"/> 越大越「跟手」、越小越平滑；首个样本直接作为初值（无历史可平滑）。</para>
    /// </remarks>
    public sealed class NetworkClock
    {
        // EMA 平滑系数（new = alpha*sample + (1-alpha)*old）。取值偏小以获得稳定时钟。
        private const double DefaultSmoothingFactor = 0.1;

        private readonly double m_TickRateHz;
        private readonly double m_TickIntervalSec;
        private readonly double m_SmoothingFactor;

        private double m_SmoothedOffsetSec;
        private double m_SmoothedRttSec;
        private bool m_HasSample;

        /// <summary>
        /// 构造网络时钟。
        /// </summary>
        /// <param name="tickRateHz">服务器每秒模拟 tick 数（必须为正），默认 30Hz。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRateHz"/> 不为正时抛出。</exception>
        public NetworkClock(double tickRateHz = 30)
        {
            if (tickRateHz <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRateHz), "tick 频率必须为正数。");
            }

            m_TickRateHz = tickRateHz;
            m_TickIntervalSec = 1d / tickRateHz;
            m_SmoothingFactor = DefaultSmoothingFactor;
            m_SmoothedOffsetSec = 0d;
            m_SmoothedRttSec = 0d;
            m_HasSample = false;
        }

        /// <summary>
        /// 服务器 tick 频率（Hz）。
        /// </summary>
        public double TickRateHz
        {
            get { return m_TickRateHz; }
        }

        /// <summary>
        /// 单个 tick 的时长（秒），等于 1 / <see cref="TickRateHz"/>。
        /// </summary>
        public double TickIntervalSec
        {
            get { return m_TickIntervalSec; }
        }

        /// <summary>
        /// 当前平滑后的 RTT（秒）。
        /// </summary>
        public double SmoothedRttSec
        {
            get { return m_SmoothedRttSec; }
        }

        /// <summary>
        /// 喂入一个「在本地时刻 <paramref name="localTimeSec"/> 观察到的服务器时间样本」（如来自快照），
        /// 用以估计本地→服务器的时间偏移与 RTT。
        /// 单程时延按 <c>rtt/2</c> 估计：样本到达本地时，服务器实际已推进到 <c>serverTime + rtt/2</c>。
        /// 首个样本直接作为初值，之后以 EMA 平滑。
        /// </summary>
        /// <param name="serverTimeSec">样本携带的服务器时间（秒）。</param>
        /// <param name="localTimeSec">本地收到该样本时的本地时间（秒）。</param>
        /// <param name="rttSec">本次测得的往返时延（秒），非负。</param>
        public void OnServerTimeReceived(double serverTimeSec, double localTimeSec, double rttSec)
        {
            if (rttSec < 0d)
            {
                rttSec = 0d;
            }

            // 样本到达本地此刻，服务器时间约为 serverTime + 单程时延。
            double estimatedServerNow = serverTimeSec + rttSec * 0.5d;
            double instantOffset = estimatedServerNow - localTimeSec;

            if (!m_HasSample)
            {
                // 无历史，首样本直接作为初值。
                m_SmoothedOffsetSec = instantOffset;
                m_SmoothedRttSec = rttSec;
                m_HasSample = true;
                return;
            }

            // EMA 平滑：new = alpha*sample + (1-alpha)*old。
            m_SmoothedOffsetSec = m_SmoothingFactor * instantOffset + (1d - m_SmoothingFactor) * m_SmoothedOffsetSec;
            m_SmoothedRttSec = m_SmoothingFactor * rttSec + (1d - m_SmoothingFactor) * m_SmoothedRttSec;
        }

        /// <summary>
        /// 估计给定本地时间对应的服务器时间：<c>localTime + 平滑后的 offset</c>。
        /// 尚未收到任何样本时 offset 视为 0，直接返回本地时间。
        /// </summary>
        /// <param name="localTimeSec">本地时间（秒）。</param>
        /// <returns>估计的服务器时间（秒）。</returns>
        public double EstimatedServerTimeSec(double localTimeSec)
        {
            return localTimeSec + m_SmoothedOffsetSec;
        }

        /// <summary>
        /// 远端实体的渲染/插值时间：<c>估计服务器时间 - 插值延迟</c>。
        /// 插值延迟用于在已收到的快照之间留出缓冲，使远端插值平滑（典型 1~2 个快照间隔）。
        /// </summary>
        /// <param name="localTimeSec">本地时间（秒）。</param>
        /// <param name="interpolationDelaySec">插值延迟（秒），通常为正。</param>
        /// <returns>渲染时间（秒）。</returns>
        public double RenderTimeSec(double localTimeSec, double interpolationDelaySec)
        {
            return EstimatedServerTimeSec(localTimeSec) - interpolationDelaySec;
        }

        /// <summary>
        /// 把服务器时间换算为 tick 序号：<c>floor(serverTime / tickInterval)</c>，并钳到非负。
        /// </summary>
        /// <param name="serverTimeSec">服务器时间（秒）。</param>
        /// <returns>对应的 tick 序号。</returns>
        public uint TimeToTick(double serverTimeSec)
        {
            if (serverTimeSec <= 0d)
            {
                return 0u;
            }

            double tick = Math.Floor(serverTimeSec / m_TickIntervalSec);

            // 钳到 uint 范围，防止极端时间溢出。
            if (tick >= uint.MaxValue)
            {
                return uint.MaxValue;
            }

            return (uint)tick;
        }
    }
}
