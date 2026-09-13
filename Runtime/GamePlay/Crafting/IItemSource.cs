//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 玩家物品仓储的抽象。制作系统通过该接口读写物料，从而与具体背包实现解耦。
    /// </summary>
    /// <remarks>
    /// 游戏侧需把自己的背包/仓库适配到该接口；接口故意不依赖任何引擎类型，
    /// 因此既能对接真实背包，也能用字典等内存结构在单元测试中模拟，便于精确断言。
    /// </remarks>
    public interface IItemSource
    {
        /// <summary>
        /// 查询某物品当前持有数量。
        /// </summary>
        /// <param name="itemId">物品唯一标识。</param>
        /// <returns>持有数量；不存在时约定返回 0。</returns>
        int GetCount(string itemId);

        /// <summary>
        /// 尝试扣除指定数量的物品。
        /// </summary>
        /// <param name="itemId">物品唯一标识。</param>
        /// <param name="count">需要扣除的数量。</param>
        /// <returns>持有量不足时返回 <c>false</c> 且不做任何扣除；成功扣除返回 <c>true</c>。</returns>
        /// <remarks>单次调用必须是原子的：要么完整扣除，要么不扣除。</remarks>
        bool Consume(string itemId, int count);

        /// <summary>
        /// 向仓储中增加指定数量的物品。
        /// </summary>
        /// <param name="itemId">物品唯一标识。</param>
        /// <param name="count">要增加的数量。</param>
        void Add(string itemId, int count);
    }
}
