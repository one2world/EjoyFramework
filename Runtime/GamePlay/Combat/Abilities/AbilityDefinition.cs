//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 技能消耗：激活技能时需扣除指定属性的指定数量（如法力、耐力）。
    /// </summary>
    public sealed class AbilityCost
    {
        private readonly string m_AttributeId;
        private readonly float m_Amount;

        /// <summary>
        /// 构造技能消耗。
        /// </summary>
        /// <param name="attributeId">被消耗的属性 Id。</param>
        /// <param name="amount">消耗数量。</param>
        public AbilityCost(string attributeId, float amount)
        {
            if (string.IsNullOrEmpty(attributeId))
            {
                throw new ArgumentException("消耗的属性 Id 不能为空。", nameof(attributeId));
            }

            m_AttributeId = attributeId;
            m_Amount = amount;
        }

        /// <summary>
        /// 被消耗的属性 Id。
        /// </summary>
        public string AttributeId
        {
            get { return m_AttributeId; }
        }

        /// <summary>
        /// 消耗数量。
        /// </summary>
        public float Amount
        {
            get { return m_Amount; }
        }
    }

    /// <summary>
    /// 技能激活结果。
    /// </summary>
    public enum AbilityActivationResult
    {
        /// <summary>
        /// 激活成功。
        /// </summary>
        Success,

        /// <summary>
        /// 技能未被授予。
        /// </summary>
        NotGranted,

        /// <summary>
        /// 技能处于冷却中。
        /// </summary>
        OnCooldown,

        /// <summary>
        /// 资源不足，无法支付消耗。
        /// </summary>
        InsufficientResource,

        /// <summary>
        /// 被标签条件阻挡（缺少必需标签或持有禁止标签）。
        /// </summary>
        Blocked,
    }

    /// <summary>
    /// 技能定义（模板）。描述冷却、消耗、激活时施加的效果与标签门槛。
    /// </summary>
    public sealed class AbilityDefinition
    {
        private readonly string m_Id;
        private readonly float m_Cooldown;
        private readonly IReadOnlyList<AbilityCost> m_Costs;
        private readonly IReadOnlyList<GameplayEffectDefinition> m_EffectsOnActivate;
        private readonly IReadOnlyList<string> m_RequiredTags;
        private readonly IReadOnlyList<string> m_BlockedByTags;

        /// <summary>
        /// 构造技能定义。
        /// </summary>
        /// <param name="id">技能唯一标识。</param>
        /// <param name="cooldown">冷却时长（秒）。</param>
        /// <param name="costs">消耗列表。</param>
        /// <param name="effectsOnActivate">激活时施加的效果列表。</param>
        /// <param name="requiredTags">必需标签（拥有者须全部满足）。</param>
        /// <param name="blockedByTags">禁止标签（拥有者须一个都不持有）。</param>
        public AbilityDefinition(
            string id,
            float cooldown = 0f,
            IReadOnlyList<AbilityCost> costs = null,
            IReadOnlyList<GameplayEffectDefinition> effectsOnActivate = null,
            IReadOnlyList<string> requiredTags = null,
            IReadOnlyList<string> blockedByTags = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("技能 Id 不能为空。", nameof(id));
            }

            m_Id = id;
            m_Cooldown = cooldown < 0f ? 0f : cooldown;
            m_Costs = costs ?? Array.Empty<AbilityCost>();
            m_EffectsOnActivate = effectsOnActivate ?? Array.Empty<GameplayEffectDefinition>();
            m_RequiredTags = requiredTags ?? Array.Empty<string>();
            m_BlockedByTags = blockedByTags ?? Array.Empty<string>();
        }

        /// <summary>
        /// 技能唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 冷却时长（秒）。
        /// </summary>
        public float Cooldown
        {
            get { return m_Cooldown; }
        }

        /// <summary>
        /// 消耗列表。
        /// </summary>
        public IReadOnlyList<AbilityCost> Costs
        {
            get { return m_Costs; }
        }

        /// <summary>
        /// 激活时施加的效果列表。
        /// </summary>
        public IReadOnlyList<GameplayEffectDefinition> EffectsOnActivate
        {
            get { return m_EffectsOnActivate; }
        }

        /// <summary>
        /// 必需标签（拥有者须全部满足）。
        /// </summary>
        public IReadOnlyList<string> RequiredTags
        {
            get { return m_RequiredTags; }
        }

        /// <summary>
        /// 禁止标签（拥有者须一个都不持有）。
        /// </summary>
        public IReadOnlyList<string> BlockedByTags
        {
            get { return m_BlockedByTags; }
        }

        /// <summary>
        /// 创建一个技能定义构建器。
        /// </summary>
        /// <param name="id">技能唯一标识。</param>
        /// <returns>构建器实例。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// 技能定义的链式构建器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private float m_Cooldown;
            private readonly List<AbilityCost> m_Costs = new List<AbilityCost>();
            private readonly List<GameplayEffectDefinition> m_Effects = new List<GameplayEffectDefinition>();
            private readonly List<string> m_RequiredTags = new List<string>();
            private readonly List<string> m_BlockedByTags = new List<string>();

            /// <summary>
            /// 构造构建器。
            /// </summary>
            /// <param name="id">技能唯一标识。</param>
            public Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 设置冷却时长。
            /// </summary>
            /// <param name="cooldown">冷却时长（秒）。</param>
            /// <returns>构建器自身。</returns>
            public Builder WithCooldown(float cooldown)
            {
                m_Cooldown = cooldown;
                return this;
            }

            /// <summary>
            /// 添加一项消耗。
            /// </summary>
            /// <param name="attributeId">被消耗属性 Id。</param>
            /// <param name="amount">消耗数量。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddCost(string attributeId, float amount)
            {
                m_Costs.Add(new AbilityCost(attributeId, amount));
                return this;
            }

            /// <summary>
            /// 添加一个激活时施加的效果。
            /// </summary>
            /// <param name="effect">效果定义。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddEffect(GameplayEffectDefinition effect)
            {
                if (effect != null)
                {
                    m_Effects.Add(effect);
                }

                return this;
            }

            /// <summary>
            /// 添加一个必需标签。
            /// </summary>
            /// <param name="tag">标签。</param>
            /// <returns>构建器自身。</returns>
            public Builder RequireTag(string tag)
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    m_RequiredTags.Add(tag);
                }

                return this;
            }

            /// <summary>
            /// 添加一个禁止标签。
            /// </summary>
            /// <param name="tag">标签。</param>
            /// <returns>构建器自身。</returns>
            public Builder BlockedBy(string tag)
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    m_BlockedByTags.Add(tag);
                }

                return this;
            }

            /// <summary>
            /// 构建技能定义。
            /// </summary>
            /// <returns>不可变的技能定义实例。</returns>
            public AbilityDefinition Build()
            {
                return new AbilityDefinition(
                    m_Id,
                    m_Cooldown,
                    m_Costs.ToArray(),
                    m_Effects.ToArray(),
                    m_RequiredTags.ToArray(),
                    m_BlockedByTags.ToArray());
            }
        }
    }
}
