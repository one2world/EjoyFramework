//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Attributes
{
    /// <summary>
    /// 属性集合，按标识管理一组 <see cref="GameplayAttribute"/>，并统一转发其变更事件，
    /// 同时支持按属性名路由修饰器与按来源跨属性批量移除。
    /// </summary>
    public sealed class AttributeSet
    {
        private readonly Dictionary<string, GameplayAttribute> m_Attributes;

        /// <summary>
        /// 构造一个空的属性集合。
        /// </summary>
        public AttributeSet()
        {
            m_Attributes = new Dictionary<string, GameplayAttribute>();
        }

        /// <summary>
        /// 集合内的所有属性。
        /// </summary>
        public IEnumerable<GameplayAttribute> Attributes
        {
            get { return m_Attributes.Values; }
        }

        /// <summary>
        /// 集合内的属性数量。
        /// </summary>
        public int Count
        {
            get { return m_Attributes.Count; }
        }

        /// <summary>
        /// 集合内任意属性的最终值发生变化时触发：参数依次为 (集合, 属性, 旧值, 新值)。
        /// </summary>
        public event Action<AttributeSet, GameplayAttribute, float, float> OnAttributeChanged;

        /// <summary>
        /// 新增一个属性。
        /// </summary>
        /// <param name="id">属性标识。</param>
        /// <param name="baseValue">初始基础值。</param>
        /// <returns>新建的属性实例。</returns>
        /// <exception cref="ArgumentException">当标识已存在时抛出。</exception>
        public GameplayAttribute Add(string id, float baseValue)
        {
            if (m_Attributes.ContainsKey(id))
            {
                throw new ArgumentException($"属性标识“{id}”已存在，无法重复添加。", nameof(id));
            }

            GameplayAttribute attribute = new GameplayAttribute(id, baseValue);
            attribute.OnValueChanged += OnChildValueChanged;
            m_Attributes.Add(id, attribute);
            return attribute;
        }

        /// <summary>
        /// 尝试按标识获取属性。
        /// </summary>
        /// <param name="id">属性标识。</param>
        /// <param name="attribute">输出获取到的属性，未找到为 null。</param>
        /// <returns>找到返回 true，否则 false。</returns>
        public bool TryGet(string id, out GameplayAttribute attribute)
        {
            return m_Attributes.TryGetValue(id, out attribute);
        }

        /// <summary>
        /// 按标识获取属性，未找到返回 null。
        /// </summary>
        /// <param name="id">属性标识。</param>
        /// <returns>属性实例或 null。</returns>
        public GameplayAttribute Get(string id)
        {
            m_Attributes.TryGetValue(id, out GameplayAttribute attribute);
            return attribute;
        }

        /// <summary>
        /// 判断集合是否包含给定标识的属性。
        /// </summary>
        /// <param name="id">属性标识。</param>
        /// <returns>包含返回 true。</returns>
        public bool Has(string id)
        {
            return m_Attributes.ContainsKey(id);
        }

        /// <summary>
        /// 获取属性的最终值；属性不存在时返回回退值。
        /// </summary>
        /// <param name="id">属性标识。</param>
        /// <param name="fallback">属性不存在时返回的回退值。</param>
        /// <returns>属性的最终值或回退值。</returns>
        public float GetValue(string id, float fallback = 0f)
        {
            return m_Attributes.TryGetValue(id, out GameplayAttribute attribute) ? attribute.CurrentValue : fallback;
        }

        /// <summary>
        /// 将修饰器路由到其目标属性；目标属性不存在时静默忽略。
        /// </summary>
        /// <param name="modifier">要添加的修饰器（以 AttributeId 决定目标属性）。</param>
        public void AddModifier(in AttributeModifier modifier)
        {
            if (m_Attributes.TryGetValue(modifier.AttributeId, out GameplayAttribute attribute))
            {
                attribute.AddModifier(modifier);
            }

            // 目标属性不存在：静默忽略。
        }

        /// <summary>
        /// 跨集合内所有属性移除指定来源的修饰器。
        /// </summary>
        /// <param name="source">来源对象（按引用相等比较）。</param>
        /// <returns>被移除的修饰器总数。</returns>
        public int RemoveModifiersFromSource(object source)
        {
            int total = 0;
            foreach (GameplayAttribute attribute in m_Attributes.Values)
            {
                total += attribute.RemoveModifiersFromSource(source);
            }

            return total;
        }

        /// <summary>
        /// 子属性变更回调，转发为集合级事件。
        /// </summary>
        private void OnChildValueChanged(GameplayAttribute attribute, float oldValue, float newValue)
        {
            Action<AttributeSet, GameplayAttribute, float, float> handler = OnAttributeChanged;
            if (handler != null)
            {
                handler(this, attribute, oldValue, newValue);
            }
        }
    }
}
