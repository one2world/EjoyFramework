//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Crafting
{
    /// <summary>
    /// 单次制作请求的结果分类。
    /// </summary>
    public enum CraftOutcome
    {
        /// <summary>
        /// 制作成功：已扣除全部输入并产出全部物料。
        /// </summary>
        Success,

        /// <summary>
        /// 材料不足：至少有一项输入物料数量不够，仓储未被改动。
        /// </summary>
        MissingIngredients,

        /// <summary>
        /// 未知配方：配方表中不存在该标识。
        /// </summary>
        UnknownRecipe,

        /// <summary>
        /// 工作台不匹配：配方要求特定工作台，但当前工作台不符。
        /// </summary>
        WrongStation,
    }
}
