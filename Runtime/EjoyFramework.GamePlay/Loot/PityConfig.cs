//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 保底（pity）配置：软保底起点、硬保底次数与软保底每抽递增量。
    /// </summary>
    /// <remarks>
    /// 软保底：当“距上次保底档命中的抽数”达到 <see cref="SoftPityStart"/> 之后，
    /// 每多抽一次，保底档的有效概率就在基础概率之上叠加 <see cref="SoftPityRampPerPull"/>。
    /// 硬保底：当该计数达到 <see cref="HardPity"/> 时，强制命中保底档。
    /// </remarks>
    public sealed class PityConfig
    {
        private readonly int m_SoftPityStart;
        private readonly int m_HardPity;
        private readonly float m_SoftPityRampPerPull;

        /// <summary>
        /// 构造一份保底配置。
        /// </summary>
        /// <param name="softPityStart">软保底起点抽数（如 74）。</param>
        /// <param name="hardPity">硬保底抽数（如 90），需大于等于软保底起点。</param>
        /// <param name="softPityRampPerPull">软保底阶段每抽叠加的保底档概率（如 0.06）。</param>
        /// <exception cref="ArgumentOutOfRangeException">参数为负或硬保底小于软保底起点时抛出。</exception>
        public PityConfig(int softPityStart, int hardPity, float softPityRampPerPull)
        {
            if (softPityStart < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(softPityStart), softPityStart, "软保底起点不能为负。");
            }

            if (hardPity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(hardPity), hardPity, "硬保底抽数必须为正。");
            }

            if (hardPity < softPityStart)
            {
                throw new ArgumentOutOfRangeException(nameof(hardPity), hardPity, "硬保底抽数不能小于软保底起点。");
            }

            if (softPityRampPerPull < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(softPityRampPerPull), softPityRampPerPull, "软保底递增量不能为负。");
            }

            m_SoftPityStart = softPityStart;
            m_HardPity = hardPity;
            m_SoftPityRampPerPull = softPityRampPerPull;
        }

        /// <summary>
        /// 软保底起点抽数：达到该抽数后保底档概率开始按抽数递增。
        /// </summary>
        public int SoftPityStart
        {
            get { return m_SoftPityStart; }
        }

        /// <summary>
        /// 硬保底抽数：在累计这么多次未命中保底档后强制命中保底档。
        /// </summary>
        public int HardPity
        {
            get { return m_HardPity; }
        }

        /// <summary>
        /// 软保底阶段每多抽一次叠加到保底档上的概率增量。
        /// </summary>
        public float SoftPityRampPerPull
        {
            get { return m_SoftPityRampPerPull; }
        }
    }
}
