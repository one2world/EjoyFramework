//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 加权掉落表。聚合若干 <see cref="LootEntry"/>，支持单次加权抽取与多次抽取（可选无放回去重）。
    /// </summary>
    /// <remarks>
    /// 抽取算法：在 <c>[0, TotalWeight)</c> 内取一个随机点，按条目顺序累加权重，
    /// 落入哪一段即命中对应条目；命中后数量在 <c>[MinCount, MaxCount]</c> 闭区间内均匀取值。
    /// 所有随机性都来自注入的 <see cref="IRandomSource"/>，因此脚本化随机源可使结果完全确定。
    /// </remarks>
    public sealed class LootTable
    {
        private readonly List<LootEntry> m_Entries;
        private float m_TotalWeight;

        /// <summary>
        /// 构造一张空的掉落表。
        /// </summary>
        public LootTable()
        {
            m_Entries = new List<LootEntry>();
            m_TotalWeight = 0f;
        }

        /// <summary>
        /// 当前所有掉落条目（只读）。
        /// </summary>
        public IReadOnlyList<LootEntry> Entries
        {
            get { return m_Entries; }
        }

        /// <summary>
        /// 所有条目的权重之和。
        /// </summary>
        public float TotalWeight
        {
            get { return m_TotalWeight; }
        }

        /// <summary>
        /// 添加一项掉落配置（流式调用，返回自身）。
        /// </summary>
        /// <param name="entry">要添加的掉落条目，不可为 null。</param>
        /// <returns>当前掉落表自身。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entry"/> 为 null 时抛出。</exception>
        public LootTable Add(LootEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            m_Entries.Add(entry);
            m_TotalWeight += entry.Weight;
            return this;
        }

        /// <summary>
        /// 以参数形式添加一项掉落配置（流式调用，返回自身）。
        /// </summary>
        /// <param name="itemId">物品唯一标识。</param>
        /// <param name="weight">相对权重，必须大于 0。</param>
        /// <param name="minCount">最小掉落数量（含），默认 1。</param>
        /// <param name="maxCount">最大掉落数量（含），默认 1。</param>
        /// <returns>当前掉落表自身。</returns>
        public LootTable Add(string itemId, float weight, int minCount = 1, int maxCount = 1)
        {
            return Add(new LootEntry(itemId, weight, minCount, maxCount));
        }

        /// <summary>
        /// 加权抽取一次，返回命中条目及随机数量。
        /// </summary>
        /// <param name="random">随机源，不可为 null。</param>
        /// <returns>命中条目对应的 <see cref="LootDrop"/>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">掉落表为空时抛出。</exception>
        public LootDrop RollOnce(IRandomSource random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (m_Entries.Count == 0)
            {
                throw new InvalidOperationException("无法在空的掉落表上执行抽取。");
            }

            LootEntry picked = PickWeighted(m_Entries, m_TotalWeight, random);
            return BuildDrop(picked, random);
        }

        /// <summary>
        /// 抽取 <paramref name="count"/> 次。
        /// </summary>
        /// <param name="count">抽取次数；不大于 0 时返回空列表。</param>
        /// <param name="random">随机源，不可为 null。</param>
        /// <param name="withReplacement">
        /// 是否有放回。为 <c>true</c>（默认）时每次都从全部条目抽取，结果可能重复；
        /// 为 <c>false</c> 时本次调用内已命中的条目将被移出后续抽取（无重复 ItemId），
        /// 且抽取次数会被裁剪到不超过条目总数。
        /// </param>
        /// <returns>抽取结果列表。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">掉落表为空且 <paramref name="count"/> 为正时抛出。</exception>
        public IList<LootDrop> Roll(int count, IRandomSource random, bool withReplacement = true)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            List<LootDrop> results = new List<LootDrop>();
            if (count <= 0)
            {
                return results;
            }

            if (m_Entries.Count == 0)
            {
                throw new InvalidOperationException("无法在空的掉落表上执行抽取。");
            }

            if (withReplacement)
            {
                for (int i = 0; i < count; i++)
                {
                    LootEntry picked = PickWeighted(m_Entries, m_TotalWeight, random);
                    results.Add(BuildDrop(picked, random));
                }

                return results;
            }

            // 无放回：复制一份可变工作集，命中后移除，权重同步递减。
            List<LootEntry> pool = new List<LootEntry>(m_Entries);
            float poolWeight = m_TotalWeight;
            int picks = count < pool.Count ? count : pool.Count;

            for (int i = 0; i < picks; i++)
            {
                int index = PickWeightedIndex(pool, poolWeight, random);
                LootEntry picked = pool[index];
                results.Add(BuildDrop(picked, random));

                poolWeight -= picked.Weight;
                pool.RemoveAt(index);
            }

            return results;
        }

        /// <summary>
        /// 在给定条目集合内执行一次加权抽取，返回命中条目。
        /// </summary>
        private static LootEntry PickWeighted(List<LootEntry> entries, float totalWeight, IRandomSource random)
        {
            int index = PickWeightedIndex(entries, totalWeight, random);
            return entries[index];
        }

        /// <summary>
        /// 在给定条目集合内执行一次加权抽取，返回命中条目的索引。
        /// </summary>
        private static int PickWeightedIndex(List<LootEntry> entries, float totalWeight, IRandomSource random)
        {
            double point = random.NextDouble() * totalWeight;
            double accumulated = 0d;

            for (int i = 0; i < entries.Count; i++)
            {
                accumulated += entries[i].Weight;
                if (point < accumulated)
                {
                    return i;
                }
            }

            // 浮点误差兜底：随机点恰好等于或极接近总权重时，归到最后一个条目。
            return entries.Count - 1;
        }

        /// <summary>
        /// 为命中条目生成 <see cref="LootDrop"/>，数量在闭区间内均匀取值。
        /// </summary>
        private static LootDrop BuildDrop(LootEntry entry, IRandomSource random)
        {
            int span = entry.MaxCount - entry.MinCount + 1;
            int count = entry.MinCount;
            if (span > 1)
            {
                count += random.NextInt(span);
            }

            return new LootDrop(entry.ItemId, count);
        }
    }
}
