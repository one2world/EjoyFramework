//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 掉落表中的一项配置：一个可掉落物品及其相对权重与数量区间。
    /// </summary>
    /// <remarks>
    /// <see cref="Weight"/> 为相对权重，参与同一张 <see cref="LootTable"/> 内的归一化抽取，必须大于 0。
    /// 单次命中该项时，掉落数量会在 <c>[MinCount, MaxCount]</c> 闭区间内均匀取值。
    /// </remarks>
    public sealed class LootEntry
    {
        private readonly string m_ItemId;
        private readonly float m_Weight;
        private readonly int m_MinCount;
        private readonly int m_MaxCount;

        /// <summary>
        /// 构造一项掉落配置。
        /// </summary>
        /// <param name="itemId">物品唯一标识，不可为 null 或空。</param>
        /// <param name="weight">相对权重，必须大于 0。</param>
        /// <param name="minCount">最小掉落数量（含），默认 1，必须大于 0。</param>
        /// <param name="maxCount">最大掉落数量（含），默认 1，必须不小于 <paramref name="minCount"/>。</param>
        /// <exception cref="ArgumentException">物品标识为空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">权重或数量区间非法时抛出。</exception>
        public LootEntry(string itemId, float weight, int minCount = 1, int maxCount = 1)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("LootEntry 的物品标识不能为空。", nameof(itemId));
            }

            if (weight <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "LootEntry 的权重必须大于 0。");
            }

            if (minCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minCount), minCount, "LootEntry 的最小数量必须大于 0。");
            }

            if (maxCount < minCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "LootEntry 的最大数量不能小于最小数量。");
            }

            m_ItemId = itemId;
            m_Weight = weight;
            m_MinCount = minCount;
            m_MaxCount = maxCount;
        }

        /// <summary>
        /// 物品唯一标识。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 相对权重（大于 0）。
        /// </summary>
        public float Weight
        {
            get { return m_Weight; }
        }

        /// <summary>
        /// 最小掉落数量（含）。
        /// </summary>
        public int MinCount
        {
            get { return m_MinCount; }
        }

        /// <summary>
        /// 最大掉落数量（含）。
        /// </summary>
        public int MaxCount
        {
            get { return m_MaxCount; }
        }
    }
}
