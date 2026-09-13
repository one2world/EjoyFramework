//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 配方表。按标识登记并查询 <see cref="Recipe"/>，并能依据物料与工作台筛选当前可制作的配方。
    /// </summary>
    /// <remarks>
    /// 配方标识唯一；重复登记会抛出异常以尽早暴露配置错误。
    /// 本类不持有任何引擎状态，可在纯逻辑层与测试中直接构造。
    /// </remarks>
    public sealed class RecipeBook
    {
        private readonly Dictionary<string, Recipe> m_Recipes;

        /// <summary>
        /// 构造一张空的配方表。
        /// </summary>
        public RecipeBook()
        {
            m_Recipes = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        }

        /// <summary>
        /// 已登记的配方数量。
        /// </summary>
        public int Count
        {
            get { return m_Recipes.Count; }
        }

        /// <summary>
        /// 枚举所有已登记的配方（顺序不保证稳定）。
        /// </summary>
        public IEnumerable<Recipe> Recipes
        {
            get { return m_Recipes.Values; }
        }

        /// <summary>
        /// 登记一条配方。
        /// </summary>
        /// <param name="recipe">要登记的配方，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="recipe"/> 为 null 时抛出。</exception>
        /// <exception cref="ArgumentException">已存在同标识配方时抛出。</exception>
        public void Add(Recipe recipe)
        {
            if (recipe == null)
            {
                throw new ArgumentNullException(nameof(recipe));
            }

            if (m_Recipes.ContainsKey(recipe.Id))
            {
                throw new ArgumentException(
                    string.Format("配方表中已存在标识为「{0}」的配方。", recipe.Id), nameof(recipe));
            }

            m_Recipes.Add(recipe.Id, recipe);
        }

        /// <summary>
        /// 按标识取得配方。
        /// </summary>
        /// <param name="recipeId">配方标识。</param>
        /// <returns>对应配方；不存在时返回 <c>null</c>。</returns>
        public Recipe Get(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId))
            {
                return null;
            }

            Recipe recipe;
            return m_Recipes.TryGetValue(recipeId, out recipe) ? recipe : null;
        }

        /// <summary>
        /// 判断是否登记了指定标识的配方。
        /// </summary>
        /// <param name="recipeId">配方标识。</param>
        /// <returns>存在则为 <c>true</c>。</returns>
        public bool Has(string recipeId)
        {
            return !string.IsNullOrEmpty(recipeId) && m_Recipes.ContainsKey(recipeId);
        }

        /// <summary>
        /// 列出当前材料（且工作台匹配）即可制作的全部配方。
        /// </summary>
        /// <param name="source">物料来源，不可为 null。</param>
        /// <param name="station">
        /// 当前工作台标识。配方未要求工作台（<see cref="Recipe.Station"/> 为 null）时永远匹配；
        /// 否则要求与本参数相等。
        /// </param>
        /// <returns>满足材料与工作台条件的配方列表（可能为空）。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> 为 null 时抛出。</exception>
        public List<Recipe> CraftableWith(IItemSource source, string station = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            List<Recipe> craftable = new List<Recipe>();
            foreach (Recipe recipe in m_Recipes.Values)
            {
                if (CraftingRules.StationMatches(recipe, station) && CraftingRules.HasAllInputs(recipe, source))
                {
                    craftable.Add(recipe);
                }
            }

            return craftable;
        }
    }
}
