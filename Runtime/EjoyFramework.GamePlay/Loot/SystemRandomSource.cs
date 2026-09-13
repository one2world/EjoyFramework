//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 基于 <see cref="System.Random"/> 的默认随机源实现。
    /// </summary>
    /// <remarks>
    /// 使用无参构造将得到一个时间种子（不可复现）的随机序列；
    /// 使用带种子构造则得到确定性序列，相同种子保证产生完全一致的序列，便于复现与测试。
    /// </remarks>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random m_Random;

        /// <summary>
        /// 以时间种子构造一个随机源（结果不可复现）。
        /// </summary>
        public SystemRandomSource()
        {
            m_Random = new Random();
        }

        /// <summary>
        /// 以指定种子构造一个确定性随机源，相同种子产生一致的序列。
        /// </summary>
        /// <param name="seed">随机种子。</param>
        public SystemRandomSource(int seed)
        {
            m_Random = new Random(seed);
        }

        /// <summary>
        /// 返回区间 <c>[0, maxExclusive)</c> 内的非负整数。
        /// 当 <paramref name="maxExclusive"/> 不大于 0 时直接返回 0，避免抛出底层异常。
        /// </summary>
        /// <param name="maxExclusive">上界（不含）。</param>
        /// <returns>落在 <c>[0, maxExclusive)</c> 的整数。</returns>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                return 0;
            }

            return m_Random.Next(maxExclusive);
        }

        /// <summary>
        /// 返回区间 <c>[0.0, 1.0)</c> 内的双精度浮点数。
        /// </summary>
        /// <returns>落在 <c>[0.0, 1.0)</c> 的浮点数。</returns>
        public double NextDouble()
        {
            return m_Random.NextDouble();
        }
    }
}
