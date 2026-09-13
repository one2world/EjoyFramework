//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Crafting;

namespace EjoyFramework.GamePlay.Tests.Crafting
{
    /// <summary>
    /// 字典支撑的 <see cref="IItemSource"/> 测试替身，便于在用例中精确断言扣除与产出。
    /// </summary>
    /// <remarks>
    /// <see cref="Consume"/> 严格原子：持有量不足时返回 <c>false</c> 且不改动任何状态；
    /// 数量归零时直接移除条目，使断言「物料是否被改动」更直观。
    /// </remarks>
    internal sealed class FakeItemSource : IItemSource
    {
        private readonly Dictionary<string, int> m_Counts;

        /// <summary>
        /// 构造一个空仓储。
        /// </summary>
        internal FakeItemSource()
        {
            m_Counts = new Dictionary<string, int>();
        }

        /// <summary>
        /// 设置（覆盖）某物品的持有量，便于测试初始化。
        /// </summary>
        internal FakeItemSource With(string itemId, int count)
        {
            if (count <= 0)
            {
                m_Counts.Remove(itemId);
            }
            else
            {
                m_Counts[itemId] = count;
            }

            return this;
        }

        /// <inheritdoc />
        public int GetCount(string itemId)
        {
            int count;
            return m_Counts.TryGetValue(itemId, out count) ? count : 0;
        }

        /// <inheritdoc />
        public bool Consume(string itemId, int count)
        {
            int current;
            if (!m_Counts.TryGetValue(itemId, out current) || current < count)
            {
                return false;
            }

            int remaining = current - count;
            if (remaining == 0)
            {
                m_Counts.Remove(itemId);
            }
            else
            {
                m_Counts[itemId] = remaining;
            }

            return true;
        }

        /// <inheritdoc />
        public void Add(string itemId, int count)
        {
            if (count <= 0)
            {
                return;
            }

            int current;
            m_Counts[itemId] = m_Counts.TryGetValue(itemId, out current) ? current + count : count;
        }
    }
}
