//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Attributes
{
    /// <summary>
    /// 游戏属性，承载一个基础值并叠加一组修饰器，对外暴露聚合后的最终值。
    /// </summary>
    /// <remarks>
    /// 最终值 <see cref="CurrentValue"/> 的聚合规则如下：
    /// <list type="number">
    /// <item>
    /// 若存在任意 <see cref="ModifierOp.Override"/> 修饰器，则最终值取其中
    /// <c>Priority</c> 最高者的 <c>Magnitude</c>；优先级相同时取最后添加者。其余步骤跳过。
    /// </item>
    /// <item>
    /// 否则按下式计算：
    /// <c>final = (BaseValue + sumOfFlat) * (1 + sumOfPercentAdd) * product(1 + eachPercentMult)</c>
    /// <list type="bullet">
    /// <item><c>sumOfFlat</c> = 所有 <see cref="ModifierOp.Flat"/> 的 Magnitude 之和。</item>
    /// <item><c>sumOfPercentAdd</c> = 所有 <see cref="ModifierOp.PercentAdd"/> 的 Magnitude 之和（加法叠加，例如两个 +0.10 → ×1.20）。</item>
    /// <item><c>product(1+PercentMult)</c> = 所有 <see cref="ModifierOp.PercentMult"/> 的 (1 + Magnitude) 之连乘（乘法叠加，例如两个 +0.10 → ×1.21）。</item>
    /// </list>
    /// </item>
    /// </list>
    /// 任意改动（基础值变更、增删修饰器）后会立即重算最终值；仅当计算结果实际发生变化时才触发
    /// <see cref="OnValueChanged"/>。
    /// </remarks>
    public sealed class GameplayAttribute
    {
        private readonly string m_Id;
        private readonly List<AttributeModifier> m_Modifiers;

        private float m_BaseValue;
        private float m_CurrentValue;

        /// <summary>
        /// 构造一个游戏属性。
        /// </summary>
        /// <param name="id">属性唯一标识。</param>
        /// <param name="baseValue">初始基础值。</param>
        public GameplayAttribute(string id, float baseValue)
        {
            m_Id = id;
            m_BaseValue = baseValue;
            m_Modifiers = new List<AttributeModifier>();
            m_CurrentValue = baseValue;
        }

        /// <summary>
        /// 属性唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 基础值。赋值会触发重算并在结果变化时派发变更事件。
        /// </summary>
        public float BaseValue
        {
            get { return m_BaseValue; }
            set
            {
                if (m_BaseValue == value)
                {
                    return;
                }

                m_BaseValue = value;
                Recompute();
            }
        }

        /// <summary>
        /// 聚合后的最终值（缓存，随任意改动重算）。
        /// </summary>
        public float CurrentValue
        {
            get { return m_CurrentValue; }
        }

        /// <summary>
        /// 当前作用于该属性的所有修饰器（只读）。
        /// </summary>
        public IReadOnlyList<AttributeModifier> Modifiers
        {
            get { return m_Modifiers; }
        }

        /// <summary>
        /// 最终值发生变化时触发：参数依次为 (属性, 旧值, 新值)。
        /// </summary>
        public event Action<GameplayAttribute, float, float> OnValueChanged;

        /// <summary>
        /// 添加一个修饰器并重算最终值。
        /// </summary>
        /// <param name="modifier">要添加的修饰器。</param>
        public void AddModifier(in AttributeModifier modifier)
        {
            m_Modifiers.Add(modifier);
            Recompute();
        }

        /// <summary>
        /// 移除一个与给定修饰器逐字段相等的修饰器（仅移除首个匹配项）。
        /// </summary>
        /// <param name="modifier">待移除的修饰器。</param>
        /// <returns>成功移除返回 true，未找到返回 false。</returns>
        public bool RemoveModifier(in AttributeModifier modifier)
        {
            for (int i = 0; i < m_Modifiers.Count; i++)
            {
                if (ModifiersEqual(m_Modifiers[i], modifier))
                {
                    m_Modifiers.RemoveAt(i);
                    Recompute();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 移除所有来源等于给定对象的修饰器。
        /// </summary>
        /// <param name="source">来源对象（按引用相等比较）。</param>
        /// <returns>被移除的修饰器数量。</returns>
        public int RemoveModifiersFromSource(object source)
        {
            int removed = 0;
            for (int i = m_Modifiers.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(m_Modifiers[i].Source, source))
                {
                    m_Modifiers.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                Recompute();
            }

            return removed;
        }

        /// <summary>
        /// 清空所有修饰器并重算最终值。
        /// </summary>
        public void ClearModifiers()
        {
            if (m_Modifiers.Count == 0)
            {
                return;
            }

            m_Modifiers.Clear();
            Recompute();
        }

        /// <summary>
        /// 按聚合规则重算最终值，并在结果实际变化时派发 <see cref="OnValueChanged"/>。
        /// 热路径上直接遍历修饰器列表，避免 LINQ 与额外分配。
        /// </summary>
        private void Recompute()
        {
            float newValue = ComputeValue();
            if (m_CurrentValue == newValue)
            {
                return;
            }

            float oldValue = m_CurrentValue;
            m_CurrentValue = newValue;

            Action<GameplayAttribute, float, float> handler = OnValueChanged;
            if (handler != null)
            {
                handler(this, oldValue, newValue);
            }
        }

        /// <summary>
        /// 根据当前基础值与修饰器集合计算最终值，不产生副作用。
        /// </summary>
        private float ComputeValue()
        {
            bool hasOverride = false;
            float overrideValue = 0f;
            int overridePriority = 0;

            float sumOfFlat = 0f;
            float sumOfPercentAdd = 0f;
            float percentMultProduct = 1f;

            for (int i = 0; i < m_Modifiers.Count; i++)
            {
                AttributeModifier modifier = m_Modifiers[i];
                switch (modifier.Op)
                {
                    case ModifierOp.Override:
                        // 取优先级最高者；优先级相同则后添加者覆盖前者（>= 保证“最后添加”胜出）。
                        if (!hasOverride || modifier.Priority >= overridePriority)
                        {
                            hasOverride = true;
                            overrideValue = modifier.Magnitude;
                            overridePriority = modifier.Priority;
                        }

                        break;

                    case ModifierOp.Flat:
                        sumOfFlat += modifier.Magnitude;
                        break;

                    case ModifierOp.PercentAdd:
                        sumOfPercentAdd += modifier.Magnitude;
                        break;

                    case ModifierOp.PercentMult:
                        percentMultProduct *= 1f + modifier.Magnitude;
                        break;
                }
            }

            if (hasOverride)
            {
                return overrideValue;
            }

            return (m_BaseValue + sumOfFlat) * (1f + sumOfPercentAdd) * percentMultProduct;
        }

        /// <summary>
        /// 逐字段比较两个修饰器是否相等（用于精确移除单个修饰器）。
        /// </summary>
        private static bool ModifiersEqual(in AttributeModifier a, in AttributeModifier b)
        {
            return a.Op == b.Op
                && a.Magnitude == b.Magnitude
                && a.Priority == b.Priority
                && string.Equals(a.AttributeId, b.AttributeId)
                && ReferenceEquals(a.Source, b.Source);
        }
    }
}
