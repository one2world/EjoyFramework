//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 抽卡的一个稀有度档位：携带基础概率、是否为保底档，以及该档内具体物品的加权掉落池。
    /// </summary>
    /// <remarks>
    /// 各档位的 <see cref="BaseRate"/> 之和应约等于 1。保底（pity）机制只针对被标记为
    /// <see cref="IsPityTier"/> 的那个档位（通常是最高稀有度，如 SSR）。
    /// 命中某档位后，再从该档的 <see cref="Pool"/> 中加权抽取具体物品。
    /// </remarks>
    public sealed class GachaTier
    {
        private readonly string m_Id;
        private readonly float m_BaseRate;
        private readonly bool m_IsPityTier;
        private readonly LootTable m_Pool;

        /// <summary>
        /// 构造一个抽卡档位。
        /// </summary>
        /// <param name="id">档位标识（如 "SSR"、"SR"、"R"），不可为空。</param>
        /// <param name="baseRate">基础概率，取值应在 <c>[0, 1]</c>。</param>
        /// <param name="pool">该档内具体物品的加权掉落池，不可为 null。</param>
        /// <param name="isPityTier">是否为保底所保障的档位，默认 false。</param>
        /// <exception cref="ArgumentException">档位标识为空时抛出。</exception>
        /// <exception cref="ArgumentNullException">掉落池为 null 时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">基础概率不在 [0,1] 时抛出。</exception>
        public GachaTier(string id, float baseRate, LootTable pool, bool isPityTier = false)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("GachaTier 的档位标识不能为空。", nameof(id));
            }

            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (baseRate < 0f || baseRate > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(baseRate), baseRate, "GachaTier 的基础概率必须落在 [0,1]。");
            }

            m_Id = id;
            m_BaseRate = baseRate;
            m_IsPityTier = isPityTier;
            m_Pool = pool;
        }

        /// <summary>
        /// 档位标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 基础概率（[0,1]）。
        /// </summary>
        public float BaseRate
        {
            get { return m_BaseRate; }
        }

        /// <summary>
        /// 是否为保底所保障的档位。
        /// </summary>
        public bool IsPityTier
        {
            get { return m_IsPityTier; }
        }

        /// <summary>
        /// 该档内具体物品的加权掉落池。
        /// </summary>
        public LootTable Pool
        {
            get { return m_Pool; }
        }
    }
}
