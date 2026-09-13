//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 基于内存字典的物品目录默认实现，同时作为框架模块经 <see cref="Framework.GetModule{T}"/> 暴露。
    /// 线程不安全，预期在单线程游戏逻辑中按需注册并读取。
    /// </summary>
    /// <remarks>无构造依赖，无每帧逻辑；仅提供注册/查询能力。</remarks>
    public sealed class ItemCatalog : FrameworkModule, IItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> m_Definitions =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

        /// <summary>
        /// 获取游戏框架模块优先级。物品目录为纯数据查询，使用默认优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 已注册的物品定义数量。
        /// </summary>
        public int Count
        {
            get { return m_Definitions.Count; }
        }

        /// <summary>
        /// 注册一个物品定义。
        /// </summary>
        /// <param name="definition">要注册的物品定义，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 definition 为空时抛出。</exception>
        /// <exception cref="ArgumentException">当存在相同 Id 的定义时抛出。</exception>
        public void Register(ItemDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (m_Definitions.ContainsKey(definition.Id))
            {
                throw new ArgumentException(
                    string.Format("物品定义 '{0}' 已注册，不允许重复。", definition.Id), nameof(definition));
            }

            m_Definitions.Add(definition.Id, definition);
        }

        /// <summary>
        /// 尝试获取指定物品的定义。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="definition">命中时返回的物品定义，否则为 null。</param>
        /// <returns>命中返回 true。</returns>
        public bool TryGet(string itemId, out ItemDefinition definition)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                definition = null;
                return false;
            }

            return m_Definitions.TryGetValue(itemId, out definition);
        }

        /// <summary>
        /// 获取指定物品的定义。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>对应的物品定义；不存在时返回 null。</returns>
        public ItemDefinition Get(string itemId)
        {
            ItemDefinition definition;
            TryGet(itemId, out definition);
            return definition;
        }

        /// <summary>
        /// 游戏框架模块轮询。物品目录无每帧逻辑，空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间，以秒为单位。</param>
        /// <param name="realElapseSeconds">真实流逝时间，以秒为单位。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理游戏框架模块，移除全部已注册的物品定义。
        /// </summary>
        public override void Shutdown()
        {
            m_Definitions.Clear();
        }
    }
}
