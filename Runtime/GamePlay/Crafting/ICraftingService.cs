//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 制作服务接口。校验配方、原子化扣除输入并产出物料，并在成功时派发事件。
    /// </summary>
    /// <remarks>
    /// 通过 <c>Framework.GetModule&lt;ICraftingService&gt;()</c> 访问。原子性保证：执行扣除前会先
    /// 确认每一项输入都买得起；任一不足即整体放弃，不会留下「扣了一半」的脏状态。
    /// <see cref="CanCraft"/> 与 <see cref="Craft"/> 共用同一套判定规则，避免校验逻辑漂移。
    /// </remarks>
    public interface ICraftingService
    {
        /// <summary>
        /// 本服务使用的配方表。
        /// </summary>
        RecipeBook Book { get; }

        /// <summary>
        /// 制作成功后派发：参数为本服务与刚刚制作完成的配方。
        /// </summary>
        event Action<ICraftingService, Recipe> OnCrafted;

        /// <summary>
        /// 判断指定配方在当前材料与工作台下是否可制作。
        /// </summary>
        /// <param name="recipeId">配方标识。</param>
        /// <param name="source">物料来源，不可为 null。</param>
        /// <param name="station">当前工作台标识，可为 null。</param>
        /// <returns>配方存在、工作台匹配且材料充足时为 <c>true</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null 时抛出。</exception>
        bool CanCraft(string recipeId, IItemSource source, string station = null);

        /// <summary>
        /// 执行制作：重新校验后，原子地扣除全部输入、增加全部产出，派发 <see cref="OnCrafted"/> 并返回结果。
        /// </summary>
        /// <param name="recipeId">配方标识。</param>
        /// <param name="source">物料来源，不可为 null。</param>
        /// <param name="station">当前工作台标识，可为 null。</param>
        /// <returns>
        /// 成功返回 <see cref="CraftOutcome.Success"/>；否则返回具体失败原因，且不改动 <paramref name="source"/>。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null 时抛出。</exception>
        CraftOutcome Craft(string recipeId, IItemSource source, string station = null);
    }
}
