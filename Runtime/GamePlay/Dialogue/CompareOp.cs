//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Dialogue
{
    /// <summary>
    /// 条件比较运算符。数值类型（Int/Float）支持全部 6 种；
    /// 布尔/字符串类型仅 Equal/NotEqual 有意义，其余运算符按“不相等即不通过”降级处理（见 <see cref="DialogueCondition"/>）。
    /// </summary>
    public enum CompareOp
    {
        /// <summary>等于。</summary>
        Equal,

        /// <summary>不等于。</summary>
        NotEqual,

        /// <summary>小于。</summary>
        Less,

        /// <summary>小于等于。</summary>
        LessEqual,

        /// <summary>大于。</summary>
        Greater,

        /// <summary>大于等于。</summary>
        GreaterEqual,
    }

    /// <summary>
    /// 变量写入运算符。Add 仅对数值类型（Int/Float）有意义。
    /// </summary>
    public enum VarOp
    {
        /// <summary>直接赋值。</summary>
        Set,

        /// <summary>累加（仅数值）。</summary>
        Add,
    }
}
