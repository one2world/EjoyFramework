//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Attributes
{
    /// <summary>
    /// 属性修饰器的运算类型，决定该修饰器如何参与最终值的聚合计算。
    /// </summary>
    public enum ModifierOp
    {
        /// <summary>
        /// 覆盖：直接以修饰器的数值作为最终值，忽略基础值与其它修饰器。
        /// 当存在多个 Override 时，取 Priority 最高者；优先级相同则取最后添加者。
        /// </summary>
        Override,

        /// <summary>
        /// 平加：在基础值之上加上一个固定数值（所有 Flat 求和后参与计算）。
        /// </summary>
        Flat,

        /// <summary>
        /// 加法百分比：以加法方式叠加的百分比，多个 PercentAdd 先求和再乘算。
        /// 例如两个 +0.10 等价于 ×1.20。
        /// </summary>
        PercentAdd,

        /// <summary>
        /// 乘法百分比：以连乘方式叠加的百分比，每个 PercentMult 独立相乘。
        /// 例如两个 +0.10 等价于 ×1.21。
        /// </summary>
        PercentMult,
    }
}
