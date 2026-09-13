//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 运行时的激活效果实例。由 <see cref="AbilitySystem"/> 创建并跟踪。
    /// 作为 AttributeSet 修饰器的 Source 句柄，到期时用于精确移除其全部修饰器。
    /// 对外暴露只读状态；内部时间推进由 AbilitySystem 驱动。
    /// </summary>
    public sealed class ActiveGameplayEffect
    {
        private readonly GameplayEffectDefinition m_Definition;
        private readonly object m_Source;

        // 内部时间状态，仅由 AbilitySystem 推进。
        internal float m_RemainingTime;
        internal float m_PeriodAccumulator;
        internal bool m_Expired;

        /// <summary>
        /// 构造激活效果实例。
        /// </summary>
        /// <param name="definition">来源的效果定义。</param>
        /// <param name="source">效果来源（施法者/技能等），可为空。</param>
        internal ActiveGameplayEffect(GameplayEffectDefinition definition, object source)
        {
            m_Definition = definition;
            m_Source = source;

            if (definition.DurationType == EffectDurationType.Duration)
            {
                m_RemainingTime = definition.Duration;
            }
            else
            {
                // 无限/瞬时效果没有剩余时间倒计概念。
                m_RemainingTime = 0f;
            }

            m_PeriodAccumulator = 0f;
            m_Expired = false;
        }

        /// <summary>
        /// 来源的效果定义。
        /// </summary>
        public GameplayEffectDefinition Definition
        {
            get { return m_Definition; }
        }

        /// <summary>
        /// 剩余持续时间（仅 Duration 类型有意义；其它类型为 0）。
        /// </summary>
        public float RemainingTime
        {
            get { return m_RemainingTime; }
        }

        /// <summary>
        /// 效果来源。
        /// </summary>
        public object Source
        {
            get { return m_Source; }
        }
    }
}
