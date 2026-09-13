//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using EjoyFramework.GamePlay.Attributes;

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 技能系统的 Unity 驱动组件。持有 AttributeSet 与 AbilitySystem，
    /// 并在 Update 中按 Time.deltaTime 推进系统时间。纯逻辑位于 .GamePlay 程序集，本组件仅负责驱动与暴露。
    /// </summary>
    public sealed class AbilitySystemBehaviour : MonoBehaviour
    {
        private AttributeSet m_Attributes;
        private AbilitySystem m_AbilitySystem;

        /// <summary>
        /// 承载属性集（初始化前为 null）。
        /// </summary>
        public AttributeSet Attributes
        {
            get { return m_Attributes; }
        }

        /// <summary>
        /// 技能系统实例（初始化前为 null）。
        /// </summary>
        public AbilitySystem System
        {
            get { return m_AbilitySystem; }
        }

        /// <summary>
        /// 用已有的属性集初始化技能系统。
        /// </summary>
        /// <param name="attributes">外部构建好的属性集，不可为空。</param>
        /// <returns>初始化得到的技能系统实例。</returns>
        public AbilitySystem Initialize(AttributeSet attributes)
        {
            if (attributes == null)
            {
                Debug.LogError("[AbilitySystemBehaviour] Initialize 收到空的 AttributeSet。");
                return null;
            }

            m_Attributes = attributes;
            // 直接以业务属性集构造，避免无参构造先分配一个随即被替换的空 AttributeSet。
            m_AbilitySystem = new AbilitySystem(attributes);
            return m_AbilitySystem;
        }

        /// <summary>
        /// 注入一个已构建的技能系统（用于外部完全托管的场景）。
        /// </summary>
        /// <param name="abilitySystem">技能系统实例。</param>
        public void Assign(AbilitySystem abilitySystem)
        {
            if (abilitySystem == null)
            {
                Debug.LogError("[AbilitySystemBehaviour] Assign 收到空的 AbilitySystem。");
                return;
            }

            m_AbilitySystem = abilitySystem;
            m_Attributes = abilitySystem.Attributes;
        }

        /// <summary>
        /// 每帧推进技能系统时间（仅在已初始化时）。
        /// </summary>
        private void Update()
        {
            if (m_AbilitySystem != null)
            {
                m_AbilitySystem.Tick(Time.deltaTime);
            }
        }
    }
}
