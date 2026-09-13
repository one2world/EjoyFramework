//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Attributes
{
    /// <summary>
    /// 属性修饰器，描述对某个属性施加的一次数值变更（如装备加成、Buff 效果等）。
    /// 为不可变只读结构体，便于在热路径上无额外分配地传递。
    /// </summary>
    public readonly struct AttributeModifier
    {
        /// <summary>
        /// 目标属性的唯一标识。
        /// </summary>
        public string AttributeId { get; }

        /// <summary>
        /// 修饰器的运算类型。
        /// </summary>
        public ModifierOp Op { get; }

        /// <summary>
        /// 修饰器的数值大小（含义随 <see cref="Op"/> 不同：Flat 为固定增量，
        /// PercentAdd/PercentMult 为百分比，Override 为直接覆盖值）。
        /// </summary>
        public float Magnitude { get; }

        /// <summary>
        /// 修饰器的来源对象（可为空），用于按来源批量移除（例如某个 Buff 实例）。
        /// </summary>
        public object Source { get; }

        /// <summary>
        /// 优先级，仅对 Override 生效：数值越高越优先；默认 0。
        /// </summary>
        public int Priority { get; }

        /// <summary>
        /// 构造一个属性修饰器。
        /// </summary>
        /// <param name="attributeId">目标属性标识。</param>
        /// <param name="op">运算类型。</param>
        /// <param name="magnitude">数值大小。</param>
        /// <param name="source">来源对象，可为空。</param>
        /// <param name="priority">优先级，仅对 Override 生效，默认 0。</param>
        public AttributeModifier(string attributeId, ModifierOp op, float magnitude, object source = null, int priority = 0)
        {
            AttributeId = attributeId;
            Op = op;
            Magnitude = magnitude;
            Source = source;
            Priority = priority;
        }
    }
}
