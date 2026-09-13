//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Loot
{
    /// <summary>
    /// 一次掉落结果：命中的物品标识及其本次掉落数量（不可变值类型）。
    /// </summary>
    public readonly struct LootDrop
    {
        private readonly string m_ItemId;
        private readonly int m_Count;

        /// <summary>
        /// 构造一次掉落结果。
        /// </summary>
        /// <param name="itemId">命中的物品标识。</param>
        /// <param name="count">本次掉落数量。</param>
        public LootDrop(string itemId, int count)
        {
            m_ItemId = itemId;
            m_Count = count;
        }

        /// <summary>
        /// 命中的物品标识。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 本次掉落数量。
        /// </summary>
        public int Count
        {
            get { return m_Count; }
        }
    }
}
