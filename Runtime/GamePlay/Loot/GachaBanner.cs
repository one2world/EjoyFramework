//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 抽卡卡池：聚合若干 <see cref="GachaTier"/>，实现带软/硬保底的单抽与多抽。
    /// </summary>
    /// <remarks>
    /// <para>单次抽卡流程（<see cref="Pull"/>）：</para>
    /// <list type="number">
    /// <item>
    /// 计算保底档的有效概率：
    /// <c>effective = BaseRate(pityTier) + max(0, (PullsSincePityTier + 1) - SoftPityStart) * SoftPityRampPerPull</c>，
    /// 并裁剪到 <c>[0, 1]</c>。其中 <c>PullsSincePityTier + 1</c> 表示“本抽是第几抽”。
    /// </item>
    /// <item>
    /// 若 <c>PullsSincePityTier + 1 &gt;= HardPity</c>，强制命中保底档，<see cref="GachaResult.WasPity"/> 置为 true。
    /// </item>
    /// <item>
    /// 否则掷一个 <c>[0,1)</c> 的均匀随机数：落入保底档有效概率区间则命中保底档；
    /// 余下概率按各非保底档的 <see cref="GachaTier.BaseRate"/> 比例分配给非保底档。
    /// </item>
    /// <item>命中保底档后 <see cref="PityState.PullsSincePityTier"/> 归 0；命中非保底档则 +1。</item>
    /// <item>命中档位后，再用该档的 <see cref="GachaTier.Pool"/> 加权抽出具体物品。</item>
    /// </list>
    /// <para>
    /// <b>WasPity 的取值约定</b>：本实现仅在“由硬保底强制命中”时将 WasPity 置为 true。
    /// 软保底阶段（概率被 ramp 抬高后）自然命中保底档，WasPity 仍为 false——
    /// 因为这只是概率提升后的正常命中，并非被系统强制保底。该选择简单、可预测、易于断言。
    /// </para>
    /// <para>所有随机性均来自注入的 <see cref="IRandomSource"/>，脚本化随机源可使结果完全确定。</para>
    /// </remarks>
    public sealed class GachaBanner
    {
        // 校验所有档位基础概率之和时容忍的浮点误差上限。
        private const float RATE_SUM_EPSILON = 1e-4f;

        private readonly List<GachaTier> m_Tiers;
        private readonly PityConfig m_Pity;
        private readonly int m_PityTierIndex;
        private readonly float m_NonPityBaseRateSum;

        /// <summary>
        /// 构造一个抽卡卡池。
        /// </summary>
        /// <param name="tiers">档位列表，不可为 null 且至少包含一个档位。</param>
        /// <param name="pity">保底配置；为 null 时表示该卡池无保底机制。</param>
        /// <exception cref="ArgumentNullException"><paramref name="tiers"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">
        /// 出现以下情形时抛出：档位列表为空；配置了保底却没有任何保底档；存在保底档却没有任何
        /// 非保底档可分配剩余概率（非保底档基础概率之和不为正，否则未命中保底档时无处落点会
        /// 错误回退到保底档，破坏保底计数）；或所有档位基础概率之和超过 1（概率配置不自洽）。
        /// </exception>
        public GachaBanner(IReadOnlyList<GachaTier> tiers, PityConfig pity = null)
        {
            if (tiers == null)
            {
                throw new ArgumentNullException(nameof(tiers));
            }

            if (tiers.Count == 0)
            {
                throw new ArgumentException("GachaBanner 至少需要一个档位。", nameof(tiers));
            }

            m_Tiers = new List<GachaTier>(tiers);
            m_Pity = pity;

            m_PityTierIndex = -1;
            m_NonPityBaseRateSum = 0f;
            float totalBaseRateSum = 0f;
            for (int i = 0; i < m_Tiers.Count; i++)
            {
                totalBaseRateSum += m_Tiers[i].BaseRate;
                if (m_Tiers[i].IsPityTier)
                {
                    if (m_PityTierIndex < 0)
                    {
                        m_PityTierIndex = i;
                    }
                }
                else
                {
                    m_NonPityBaseRateSum += m_Tiers[i].BaseRate;
                }
            }

            if (m_Pity != null && m_PityTierIndex < 0)
            {
                throw new ArgumentException("配置了保底但档位列表中没有任何被标记为保底档（IsPityTier）的档位。", nameof(tiers));
            }

            // 存在保底档时，未命中保底档的掷点需要按非保底档基础概率比例分配；若非保底档基础概率之和不为正，
            // 则未命中时无处落点，只能错误回退到保底档——而调用方会据此把保底计数 +1，导致“本抽实际命中保底档却仍
            // 累加计数”的保底失同步。这里在构造期就拒绝该配置，使所有抽卡路径的保底计数保持自洽。
            if (m_PityTierIndex >= 0 && m_NonPityBaseRateSum <= 0f)
            {
                throw new ArgumentException(
                    "存在保底档时，非保底档的基础概率之和必须为正，否则未命中保底档时无处落点会破坏保底计数。",
                    nameof(tiers));
            }

            // 概率自洽性校验：所有档位基础概率之和不应超过 1（容忍浮点误差）。超出会静默扭曲分布，故让其在构造期显式失败。
            if (totalBaseRateSum > 1f + RATE_SUM_EPSILON)
            {
                throw new ArgumentException(
                    "所有档位的基础概率之和不能超过 1（当前为 " + totalBaseRateSum.ToString("0.######") + "）。",
                    nameof(tiers));
            }
        }

        /// <summary>
        /// 当前卡池的所有档位（只读）。
        /// </summary>
        public IReadOnlyList<GachaTier> Tiers
        {
            get { return m_Tiers; }
        }

        /// <summary>
        /// 执行一次抽卡。会按规则修改 <paramref name="state"/> 的计数。
        /// </summary>
        /// <param name="random">随机源，不可为 null。</param>
        /// <param name="state">保底计数状态，不可为 null；命中保底档归 0，否则 +1。</param>
        /// <returns>本次抽卡结果。</returns>
        /// <exception cref="ArgumentNullException">任一参数为 null 时抛出。</exception>
        public GachaResult Pull(IRandomSource random, PityState state)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            // 无保底档：退化为纯按基础概率分布抽档。
            if (m_PityTierIndex < 0)
            {
                GachaTier plainTier = SelectByBaseRate(random);
                return BuildResult(plainTier, random, false);
            }

            GachaTier pityTier = m_Tiers[m_PityTierIndex];

            // “本抽是第几抽”——计数从 0 起，+1 表示把当前这一抽计入。
            int pullOrdinal = state.PullsSincePityTier + 1;

            bool hardPityForced = m_Pity != null && pullOrdinal >= m_Pity.HardPity;
            if (hardPityForced)
            {
                state.PullsSincePityTier = 0;
                return BuildResult(pityTier, random, true);
            }

            float effectivePityRate = ComputeEffectivePityRate(pityTier.BaseRate, pullOrdinal);

            double roll = random.NextDouble();
            if (roll < effectivePityRate)
            {
                state.PullsSincePityTier = 0;
                // 软保底 ramp 自然命中并非强制保底，WasPity 仍为 false（见类型文档约定）。
                return BuildResult(pityTier, random, false);
            }

            // 未命中保底档：在剩余概率内按非保底档的基础概率比例分配（构造期已保证必定命中非保底档）。
            GachaTier chosen = SelectNonPityTier(roll, effectivePityRate);
            state.PullsSincePityTier++;
            return BuildResult(chosen, random, false);
        }

        /// <summary>
        /// 连续执行 <paramref name="count"/> 次抽卡，状态在多抽之间贯穿累计。
        /// </summary>
        /// <param name="count">抽卡次数；不大于 0 时返回空列表。</param>
        /// <param name="random">随机源，不可为 null。</param>
        /// <param name="state">保底计数状态，不可为 null。</param>
        /// <returns>每次抽卡的结果列表。</returns>
        public IList<GachaResult> PullMany(int count, IRandomSource random, PityState state)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            List<GachaResult> results = new List<GachaResult>();
            for (int i = 0; i < count; i++)
            {
                results.Add(Pull(random, state));
            }

            return results;
        }

        /// <summary>
        /// 按保底档基础概率与软保底 ramp 计算有效保底概率，裁剪到 [0,1]。
        /// </summary>
        private float ComputeEffectivePityRate(float baseRate, int pullOrdinal)
        {
            float rate = baseRate;
            if (m_Pity != null)
            {
                int beyond = pullOrdinal - m_Pity.SoftPityStart;
                if (beyond > 0)
                {
                    rate += beyond * m_Pity.SoftPityRampPerPull;
                }
            }

            if (rate < 0f)
            {
                return 0f;
            }

            if (rate > 1f)
            {
                return 1f;
            }

            return rate;
        }

        /// <summary>
        /// 在保底档未命中的前提下，把剩余概率按各非保底档基础概率比例分配，抽出一个非保底档。
        /// </summary>
        /// <param name="roll">本抽已掷出的 [0,1) 随机点（保底档未命中，主随机点由其复用）。</param>
        /// <param name="effectivePityRate">保底档有效概率（roll 已确认不小于该值）。</param>
        /// <remarks>
        /// 调用前提：存在保底档时构造期已保证 <see cref="m_NonPityBaseRateSum"/> 为正，故本方法必定返回某个
        /// 非保底档，绝不会回退到保底档——这保证调用方对保底计数的 +1 永远只发生在真正命中非保底档时。
        /// </remarks>
        private GachaTier SelectNonPityTier(double roll, double effectivePityRate)
        {
            // 将 roll 落点从保底区间之后平移回 [0, remaining)，再按非保底档基础概率比例归一化定位。
            double remaining = 1d - effectivePityRate;
            double offset = roll - effectivePityRate;
            if (offset < 0d)
            {
                offset = 0d;
            }

            // 归一化到 [0, NonPityBaseRateSum) 进行分段定位，避免各非保底档基础概率之和不为 remaining 时定位错位。
            double scaled = remaining > 0d ? offset / remaining * m_NonPityBaseRateSum : 0d;

            double accumulated = 0d;
            GachaTier last = null;
            for (int i = 0; i < m_Tiers.Count; i++)
            {
                GachaTier tier = m_Tiers[i];
                if (tier.IsPityTier)
                {
                    continue;
                }

                last = tier;
                accumulated += tier.BaseRate;
                if (scaled < accumulated)
                {
                    return tier;
                }
            }

            // 浮点兜底：归到最后一个非保底档。
            return last;
        }

        /// <summary>
        /// 纯按各档基础概率分布抽出一个档位（无保底档时使用）。
        /// </summary>
        private GachaTier SelectByBaseRate(IRandomSource random)
        {
            float total = 0f;
            for (int i = 0; i < m_Tiers.Count; i++)
            {
                total += m_Tiers[i].BaseRate;
            }

            double point = total > 0f ? random.NextDouble() * total : 0d;
            double accumulated = 0d;
            for (int i = 0; i < m_Tiers.Count; i++)
            {
                accumulated += m_Tiers[i].BaseRate;
                if (point < accumulated)
                {
                    return m_Tiers[i];
                }
            }

            return m_Tiers[m_Tiers.Count - 1];
        }

        /// <summary>
        /// 在命中的档位内抽取具体物品并打包为 <see cref="GachaResult"/>。
        /// </summary>
        private static GachaResult BuildResult(GachaTier tier, IRandomSource random, bool wasPity)
        {
            LootDrop drop = tier.Pool.RollOnce(random);
            return new GachaResult(tier.Id, drop.ItemId, wasPity);
        }
    }
}
