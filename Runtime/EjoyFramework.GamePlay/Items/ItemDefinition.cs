//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 物品元数据定义。属于静态目录数据，<see cref="Inventory"/> 据此判定堆叠上限。
    /// 不可变值对象，注册后不应再修改。
    /// </summary>
    public sealed class ItemDefinition
    {
        private readonly string m_Id;
        private readonly int m_MaxStack;
        private readonly string m_Category;
        private readonly object m_Payload;

        /// <summary>
        /// 构造物品定义。
        /// </summary>
        /// <param name="id">物品唯一标识，不可为空。</param>
        /// <param name="maxStack">单个槽位的最大堆叠数；小于等于 0 或等于 int.MaxValue 视为无限堆叠。</param>
        /// <param name="category">可选的分类标签（如 "weapon"、"material"）。</param>
        /// <param name="payload">可选的游戏侧不透明数据。</param>
        /// <exception cref="ArgumentException">当 id 为空时抛出。</exception>
        public ItemDefinition(string id, int maxStack = int.MaxValue, string category = null, object payload = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("物品定义的 id 不能为空。", nameof(id));
            }

            m_Id = id;
            // 归一化：非正数统一记为无限堆叠（int.MaxValue），便于内部直接当作上限使用。
            m_MaxStack = maxStack <= 0 ? int.MaxValue : maxStack;
            m_Category = category;
            m_Payload = payload;
        }

        /// <summary>
        /// 物品唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 单个槽位的最大堆叠数。已归一化：int.MaxValue 表示无限堆叠。
        /// </summary>
        public int MaxStack
        {
            get { return m_MaxStack; }
        }

        /// <summary>
        /// 可选的分类标签。
        /// </summary>
        public string Category
        {
            get { return m_Category; }
        }

        /// <summary>
        /// 可选的游戏侧不透明数据。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }
    }
}
