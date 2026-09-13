//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Items
{
    /// <summary>
    /// 物品目录的只读访问接口。<see cref="Inventory"/> 仅依赖此抽象查询堆叠上限，
    /// 便于替换为静态数据表、远端配置或测试桩。
    /// </summary>
    public interface IItemCatalog
    {
        /// <summary>
        /// 尝试获取指定物品的定义。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="definition">命中时返回的物品定义，否则为 null。</param>
        /// <returns>命中返回 true。</returns>
        bool TryGet(string itemId, out ItemDefinition definition);

        /// <summary>
        /// 获取指定物品的定义。
        /// </summary>
        /// <param name="itemId">物品标识。</param>
        /// <returns>对应的物品定义；不存在时返回 null。</returns>
        ItemDefinition Get(string itemId);
    }
}
