//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 一条「物品 + 数量」的不可变记录，用作配方的输入项或产出项。
    /// </summary>
    /// <remarks>
    /// 该结构体故意不依赖任何引擎类型，仅以字符串物品标识与整型数量描述一份物料，
    /// 因此既可用于纯逻辑层的配方表达，也可直接参与单元测试断言。
    /// </remarks>
    public readonly struct ItemAmount
    {
        private readonly string m_ItemId;
        private readonly int m_Count;

        /// <summary>
        /// 构造一条物品数量记录。
        /// </summary>
        /// <param name="itemId">物品唯一标识，不可为 null 或空。</param>
        /// <param name="count">数量，必须大于 0。</param>
        /// <exception cref="ArgumentException"><paramref name="itemId"/> 为 null 或空时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> 不大于 0 时抛出。</exception>
        public ItemAmount(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("ItemAmount 的物品标识不能为空。", nameof(itemId));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "ItemAmount 的数量必须大于 0。");
            }

            m_ItemId = itemId;
            m_Count = count;
        }

        /// <summary>
        /// 物品唯一标识。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 数量（大于 0）。
        /// </summary>
        public int Count
        {
            get { return m_Count; }
        }
    }
}
