//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 背包：以槽位（slot）为单位存放物品堆，按目录中的 <see cref="ItemDefinition.MaxStack"/> 限制每槽堆叠上限。
    /// 槽位采用“紧凑”表示：<see cref="Slots"/> 仅包含非空槽位，按填充顺序排列，移除后会去除空洞。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class Inventory
    {
        private readonly IItemCatalog m_Catalog;
        private readonly int m_Capacity;

        // 紧凑槽位列表：每个元素均为非空堆。同一物品可占用多个相邻或不相邻的槽位。
        private readonly List<ItemStack> m_Slots = new List<ItemStack>();

        /// <summary>
        /// 内容发生实际变化时触发一次（每个产生变更的调用至多触发一次）。
        /// </summary>
        public event Action<Inventory> OnChanged;

        /// <summary>
        /// 构造背包。
        /// </summary>
        /// <param name="catalog">物品目录，用于查询堆叠上限；为空时所有物品视为无限堆叠。</param>
        /// <param name="capacity">槽位容量；小于等于 0 表示无限槽位（按需增长），大于 0 表示固定槽位数。</param>
        public Inventory(IItemCatalog catalog = null, int capacity = 0)
        {
            m_Catalog = catalog;
            m_Capacity = capacity <= 0 ? 0 : capacity;
        }

        /// <summary>
        /// 槽位容量。0 表示无限。
        /// </summary>
        public int Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>
        /// 当前已占用的槽位数量。
        /// </summary>
        public int UsedSlots
        {
            get { return m_Slots.Count; }
        }

        /// <summary>
        /// 当前槽位的只读快照视图（仅非空槽位，按填充顺序排列）。
        /// </summary>
        public IReadOnlyList<ItemStack> Slots
        {
            get { return m_Slots; }
        }

        /// <summary>
        /// 向背包加入指定数量的物品。先填充同物品的未满槽位，再按堆叠上限分配新槽位。
        /// 固定容量耗尽时返回未能放入的剩余数量。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="count">要加入的数量。</param>
        /// <returns>未能放入的剩余数量（全部放入则为 0）。</returns>
        public int AddItem(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0)
            {
                return count <= 0 ? 0 : count;
            }

            int maxStack = GetMaxStack(itemId);
            int remaining = count;
            bool changed = false;

            // 阶段一：填充已存在的同物品未满槽位。
            for (int i = 0; i < m_Slots.Count && remaining > 0; i++)
            {
                ItemStack slot = m_Slots[i];
                if (!string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                int room = maxStack - slot.Count;
                if (room <= 0)
                {
                    continue;
                }

                int put = room < remaining ? room : remaining;
                m_Slots[i] = new ItemStack(itemId, slot.Count + put);
                remaining -= put;
                changed = true;
            }

            // 阶段二：为剩余数量分配新槽位（受固定容量约束）。
            while (remaining > 0 && !IsFull())
            {
                int put = maxStack < remaining ? maxStack : remaining;
                m_Slots.Add(new ItemStack(itemId, put));
                remaining -= put;
                changed = true;
            }

            if (changed)
            {
                RaiseChanged();
            }

            return remaining;
        }

        /// <summary>
        /// 从背包移除指定数量的物品。全有或全无：当持有总量不足时不做任何修改并返回 false。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="count">要移除的数量。</param>
        /// <returns>成功移除全部数量返回 true；不足或参数非法返回 false。</returns>
        public bool RemoveItem(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || count <= 0)
            {
                return false;
            }

            if (GetCount(itemId) < count)
            {
                return false;
            }

            int remaining = count;

            // 从后向前移除，便于安全地删除被清空的槽位。
            for (int i = m_Slots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                ItemStack slot = m_Slots[i];
                if (!string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                int take = slot.Count < remaining ? slot.Count : remaining;
                int left = slot.Count - take;
                remaining -= take;

                if (left > 0)
                {
                    m_Slots[i] = new ItemStack(itemId, left);
                }
                else
                {
                    m_Slots.RemoveAt(i);
                }
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 统计某物品在所有槽位中的总数量。读取路径不产生堆分配。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>总数量。</returns>
        public int GetCount(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < m_Slots.Count; i++)
            {
                ItemStack slot = m_Slots[i];
                if (string.Equals(slot.ItemId, itemId, StringComparison.Ordinal))
                {
                    total += slot.Count;
                }
            }

            return total;
        }

        /// <summary>
        /// 是否持有至少指定数量的某物品。读取路径不产生堆分配。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="count">需要的数量，默认为 1。</param>
        /// <returns>满足返回 true。</returns>
        public bool Has(string itemId, int count = 1)
        {
            if (count <= 0)
            {
                return true;
            }

            return GetCount(itemId) >= count;
        }

        /// <summary>
        /// 清空背包。仅在原本非空时触发 <see cref="OnChanged"/>。
        /// </summary>
        public void Clear()
        {
            if (m_Slots.Count == 0)
            {
                return;
            }

            m_Slots.Clear();
            RaiseChanged();
        }

        /// <summary>
        /// 查询某物品的单槽堆叠上限：目录命中则取其 MaxStack，否则视为无限。
        /// </summary>
        private int GetMaxStack(string itemId)
        {
            if (m_Catalog != null)
            {
                ItemDefinition definition;
                if (m_Catalog.TryGet(itemId, out definition))
                {
                    return definition.MaxStack;
                }
            }

            return int.MaxValue;
        }

        /// <summary>
        /// 固定容量下槽位是否已满。无限容量恒为 false。
        /// </summary>
        private bool IsFull()
        {
            return m_Capacity > 0 && m_Slots.Count >= m_Capacity;
        }

        private void RaiseChanged()
        {
            Action<Inventory> handler = OnChanged;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
