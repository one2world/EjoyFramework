//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Attributes;

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 游戏效果的持续类型。
    /// </summary>
    public enum EffectDurationType
    {
        /// <summary>
        /// 瞬时：立即对 BaseValue 做一次永久修改，不留下运行时实例。
        /// </summary>
        Instant,

        /// <summary>
        /// 持续：在一段时间内添加可移除的修饰器，到期后移除。
        /// </summary>
        Duration,

        /// <summary>
        /// 无限：永久存在，需手动移除（如光环）。
        /// </summary>
        Infinite,
    }

    /// <summary>
    /// 游戏效果定义（模板）。描述一组属性修饰、授予标签、持续与周期信息。
    /// 修饰器中的 Source 字段被忽略，将在应用时由系统填充为对应的运行时效果实例。
    /// </summary>
    public sealed class GameplayEffectDefinition
    {
        private readonly string m_Id;
        private readonly EffectDurationType m_DurationType;
        private readonly float m_Duration;
        private readonly float m_Period;
        private readonly IReadOnlyList<AttributeModifier> m_Modifiers;
        private readonly IReadOnlyList<string> m_GrantedTags;

        /// <summary>
        /// 构造游戏效果定义。
        /// </summary>
        /// <param name="id">效果唯一标识。</param>
        /// <param name="durationType">持续类型。</param>
        /// <param name="duration">持续时长（仅 Duration 类型有效，单位秒）。</param>
        /// <param name="period">周期（>0 表示每隔该秒数应用一次，用于 DOT/HOT；0 表示无周期）。</param>
        /// <param name="modifiers">属性修饰器模板列表。</param>
        /// <param name="grantedTags">激活期间授予的标签列表。</param>
        public GameplayEffectDefinition(
            string id,
            EffectDurationType durationType,
            float duration = 0f,
            float period = 0f,
            IReadOnlyList<AttributeModifier> modifiers = null,
            IReadOnlyList<string> grantedTags = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("效果 Id 不能为空。", nameof(id));
            }

            m_Id = id;
            m_DurationType = durationType;
            m_Duration = duration < 0f ? 0f : duration;
            m_Period = period < 0f ? 0f : period;
            m_Modifiers = modifiers ?? Array.Empty<AttributeModifier>();
            m_GrantedTags = grantedTags ?? Array.Empty<string>();
        }

        /// <summary>
        /// 效果唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 持续类型。
        /// </summary>
        public EffectDurationType DurationType
        {
            get { return m_DurationType; }
        }

        /// <summary>
        /// 持续时长（仅 Duration 类型有效，单位秒）。
        /// </summary>
        public float Duration
        {
            get { return m_Duration; }
        }

        /// <summary>
        /// 周期，>0 表示每隔该秒数应用一次（DOT/HOT）；0 表示无周期。
        /// </summary>
        public float Period
        {
            get { return m_Period; }
        }

        /// <summary>
        /// 属性修饰器模板列表（AttributeId+Op+Magnitude；Source 在应用时填充）。
        /// </summary>
        public IReadOnlyList<AttributeModifier> Modifiers
        {
            get { return m_Modifiers; }
        }

        /// <summary>
        /// 激活期间授予的标签列表。
        /// </summary>
        public IReadOnlyList<string> GrantedTags
        {
            get { return m_GrantedTags; }
        }

        /// <summary>
        /// 创建一个效果定义构建器。
        /// </summary>
        /// <param name="id">效果唯一标识。</param>
        /// <returns>构建器实例。</returns>
        public static Builder Create(string id)
        {
            return new Builder(id);
        }

        /// <summary>
        /// 游戏效果定义的链式构建器。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private EffectDurationType m_DurationType = EffectDurationType.Instant;
            private float m_Duration;
            private float m_Period;
            private readonly List<AttributeModifier> m_Modifiers = new List<AttributeModifier>();
            private readonly List<string> m_GrantedTags = new List<string>();

            /// <summary>
            /// 构造构建器。
            /// </summary>
            /// <param name="id">效果唯一标识。</param>
            public Builder(string id)
            {
                m_Id = id;
            }

            /// <summary>
            /// 设为瞬时效果。
            /// </summary>
            /// <returns>构建器自身。</returns>
            public Builder Instant()
            {
                m_DurationType = EffectDurationType.Instant;
                m_Duration = 0f;
                return this;
            }

            /// <summary>
            /// 设为持续效果。
            /// </summary>
            /// <param name="duration">持续时长（秒）。</param>
            /// <returns>构建器自身。</returns>
            public Builder ForDuration(float duration)
            {
                m_DurationType = EffectDurationType.Duration;
                m_Duration = duration;
                return this;
            }

            /// <summary>
            /// 设为无限效果。
            /// </summary>
            /// <returns>构建器自身。</returns>
            public Builder Infinite()
            {
                m_DurationType = EffectDurationType.Infinite;
                m_Duration = 0f;
                return this;
            }

            /// <summary>
            /// 设置周期（>0 表示每隔该秒数应用一次）。
            /// </summary>
            /// <param name="period">周期（秒）。</param>
            /// <returns>构建器自身。</returns>
            public Builder WithPeriod(float period)
            {
                m_Period = period;
                return this;
            }

            /// <summary>
            /// 添加一个属性修饰器模板。
            /// </summary>
            /// <param name="attributeId">目标属性 Id。</param>
            /// <param name="op">修饰运算类型。</param>
            /// <param name="magnitude">修饰量。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddModifier(string attributeId, ModifierOp op, float magnitude)
            {
                m_Modifiers.Add(new AttributeModifier(attributeId, op, magnitude));
                return this;
            }

            /// <summary>
            /// 添加一个属性修饰器模板。
            /// </summary>
            /// <param name="modifier">修饰器（Source 将在应用时被覆盖）。</param>
            /// <returns>构建器自身。</returns>
            public Builder AddModifier(in AttributeModifier modifier)
            {
                m_Modifiers.Add(modifier);
                return this;
            }

            /// <summary>
            /// 添加一个激活期间授予的标签。
            /// </summary>
            /// <param name="tag">标签。</param>
            /// <returns>构建器自身。</returns>
            public Builder GrantTag(string tag)
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    m_GrantedTags.Add(tag);
                }

                return this;
            }

            /// <summary>
            /// 构建效果定义。
            /// </summary>
            /// <returns>不可变的效果定义实例。</returns>
            public GameplayEffectDefinition Build()
            {
                return new GameplayEffectDefinition(
                    m_Id,
                    m_DurationType,
                    m_Duration,
                    m_Period,
                    m_Modifiers.ToArray(),
                    m_GrantedTags.ToArray());
            }
        }
    }
}
