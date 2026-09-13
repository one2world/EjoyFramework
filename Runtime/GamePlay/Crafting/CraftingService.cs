//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 制作服务。校验配方、原子化扣除输入并产出物料，并在成功时派发事件。
    /// </summary>
    /// <remarks>
    /// 作为框架模块经 <c>Framework.GetModule&lt;ICraftingService&gt;()</c> 访问。
    /// 原子性保证：执行扣除前会先确认每一项输入都买得起；任一不足即整体放弃，
    /// 不会留下「扣了一半」的脏状态。<see cref="CanCraft"/> 与 <see cref="Craft"/>
    /// 共用同一套判定规则（见 <see cref="CraftingRules"/>），避免校验逻辑漂移。
    /// </remarks>
    public sealed class CraftingService : FrameworkModule, ICraftingService
    {
        private RecipeBook m_Book;

        /// <summary>
        /// 构造制作服务并配以一张空配方表。
        /// </summary>
        /// <remarks>
        /// 无参构造供 <c>Framework.GetModule</c> 经 Activator 实例化；随后可经
        /// <see cref="SetRecipeBook"/> 注入业务配方表，或直接向 <see cref="Book"/> 登记配方。
        /// </remarks>
        public CraftingService()
        {
            m_Book = new RecipeBook();
        }

        /// <summary>
        /// 用给定配方表构造制作服务（测试便捷构造）。
        /// </summary>
        /// <param name="book">配方表，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="book"/> 为 null 时抛出。</exception>
        internal CraftingService(RecipeBook book)
        {
            if (book == null)
            {
                throw new ArgumentNullException(nameof(book));
            }

            m_Book = book;
        }

        /// <summary>
        /// 本服务使用的配方表。
        /// </summary>
        public RecipeBook Book
        {
            get { return m_Book; }
        }

        /// <summary>
        /// 制作成功后派发：参数为本服务与刚刚制作完成的配方。
        /// </summary>
        public event Action<ICraftingService, Recipe> OnCrafted;

        /// <summary>
        /// 获取制作服务模块优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 注入配方表，替换当前实例。
        /// </summary>
        /// <param name="book">配方表，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="book"/> 为 null 时抛出。</exception>
        public void SetRecipeBook(RecipeBook book)
        {
            if (book == null)
            {
                throw new ArgumentNullException(nameof(book));
            }

            m_Book = book;
        }

        /// <summary>
        /// 制作服务无逐帧工作，留空。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭模块：清空配方表与事件订阅。
        /// </summary>
        public override void Shutdown()
        {
            m_Book = new RecipeBook();
            OnCrafted = null;
        }

        /// <summary>
        /// 判断指定配方在当前材料与工作台下是否可制作。
        /// </summary>
        /// <param name="recipeId">配方标识。</param>
        /// <param name="source">物料来源，不可为 null。</param>
        /// <param name="station">当前工作台标识，可为 null。</param>
        /// <returns>配方存在、工作台匹配且材料充足时为 <c>true</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null 时抛出。</exception>
        public bool CanCraft(string recipeId, IItemSource source, string station = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            return Evaluate(recipeId, source, station) == CraftOutcome.Success;
        }

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
        public CraftOutcome Craft(string recipeId, IItemSource source, string station = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            CraftOutcome outcome = Evaluate(recipeId, source, station);
            if (outcome != CraftOutcome.Success)
            {
                return outcome;
            }

            // 此处 Evaluate 已确认配方存在；Get 不会返回 null。
            Recipe recipe = m_Book.Get(recipeId);

            // 扣除前再次逐项确认买得起，确保「全有或全无」——绝不在扣到一半时失败。
            if (!CraftingRules.HasAllInputs(recipe, source))
            {
                return CraftOutcome.MissingIngredients;
            }

            ConsumeInputs(recipe, source);
            AddOutputs(recipe, source);

            Action<ICraftingService, Recipe> handler = OnCrafted;
            if (handler != null)
            {
                handler(this, recipe);
            }

            return CraftOutcome.Success;
        }

        /// <summary>
        /// 不改动任何状态地评估一次制作请求的结果分类。
        /// </summary>
        private CraftOutcome Evaluate(string recipeId, IItemSource source, string station)
        {
            Recipe recipe = m_Book.Get(recipeId);
            if (recipe == null)
            {
                return CraftOutcome.UnknownRecipe;
            }

            if (!CraftingRules.StationMatches(recipe, station))
            {
                return CraftOutcome.WrongStation;
            }

            if (!CraftingRules.HasAllInputs(recipe, source))
            {
                return CraftOutcome.MissingIngredients;
            }

            return CraftOutcome.Success;
        }

        /// <summary>
        /// 扣除配方的全部输入物料。调用前应已确认材料充足。
        /// </summary>
        private static void ConsumeInputs(Recipe recipe, IItemSource source)
        {
            IReadOnlyList<ItemAmount> inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                ItemAmount input = inputs[i];
                source.Consume(input.ItemId, input.Count);
            }
        }

        /// <summary>
        /// 增加配方的全部产出物料。
        /// </summary>
        private static void AddOutputs(Recipe recipe, IItemSource source)
        {
            IReadOnlyList<ItemAmount> outputs = recipe.Outputs;
            for (int i = 0; i < outputs.Count; i++)
            {
                ItemAmount output = outputs[i];
                source.Add(output.ItemId, output.Count);
            }
        }
    }
}
