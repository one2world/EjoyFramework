//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 制作规则的共享纯函数，供 <see cref="RecipeBook"/> 与 <see cref="CraftingService"/> 复用，
    /// 确保「工作台匹配」「材料是否充足」的判定在各处保持一致。
    /// </summary>
    internal static class CraftingRules
    {
        /// <summary>
        /// 判断配方所需工作台是否与当前工作台匹配。
        /// </summary>
        /// <param name="recipe">配方，不可为 null。</param>
        /// <param name="station">当前工作台标识，可为 null。</param>
        /// <returns>配方未要求工作台（Station 为 null）则恒为 <c>true</c>；否则要求按序数比较相等。</returns>
        internal static bool StationMatches(Recipe recipe, string station)
        {
            if (recipe.Station == null)
            {
                return true;
            }

            return string.Equals(recipe.Station, station, StringComparison.Ordinal);
        }

        /// <summary>
        /// 判断物料来源是否满足配方的全部输入需求。
        /// </summary>
        /// <param name="recipe">配方，不可为 null。</param>
        /// <param name="source">物料来源，不可为 null。</param>
        /// <returns>每一项输入的持有量都不少于需求量时为 <c>true</c>。</returns>
        internal static bool HasAllInputs(Recipe recipe, IItemSource source)
        {
            IReadOnlyList<ItemAmount> inputs = recipe.Inputs;
            for (int i = 0; i < inputs.Count; i++)
            {
                ItemAmount input = inputs[i];
                if (source.GetCount(input.ItemId) < input.Count)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
