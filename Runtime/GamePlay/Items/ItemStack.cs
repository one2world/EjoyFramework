//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 单个槽位的内容快照：物品标识与数量。不可变值类型，便于零分配地读取与传递。
    /// </summary>
    public readonly struct ItemStack
    {
        private readonly string m_ItemId;
        private readonly int m_Count;

        /// <summary>
        /// 构造一个物品堆。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="count">数量。</param>
        public ItemStack(string itemId, int count)
        {
            m_ItemId = itemId;
            m_Count = count;
        }

        /// <summary>
        /// 物品标识。空槽位为 null。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 数量。
        /// </summary>
        public int Count
        {
            get { return m_Count; }
        }

        /// <summary>
        /// 是否为空堆（无物品标识或数量非正）。
        /// </summary>
        public bool IsEmpty
        {
            get { return m_ItemId == null || m_Count <= 0; }
        }
    }
}
