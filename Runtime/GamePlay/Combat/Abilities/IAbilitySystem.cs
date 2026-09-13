//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Attributes;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 技能/效果系统（GAS 风格）的对外抽象。挂接到一个 <see cref="AttributeSet"/>，
    /// 负责技能授予、激活（消耗/冷却/标签门槛）、效果应用与到期、以及周期性结算。
    /// 每个拥有者（如一个单位）持有一个实例，由拥有者逐帧调用 <see cref="Tick(float)"/> 推进
    /// （不是全局 Framework 模块；不要用 <c>Framework.GetModule</c> 获取）。
    /// </summary>
    public interface IAbilitySystem : ITickable
    {
        /// <summary>
        /// 承载的属性集。
        /// </summary>
        AttributeSet Attributes { get; }

        /// <summary>
        /// 拥有者的标签集合（受效果授予标签影响）。
        /// </summary>
        GameplayTagSet Tags { get; }

        /// <summary>
        /// 当前所有激活效果（只读迭代）。
        /// </summary>
        IEnumerable<ActiveGameplayEffect> ActiveEffects { get; }

        /// <summary>
        /// 技能被成功激活时触发：（系统, 技能Id）。
        /// </summary>
        event Action<IAbilitySystem, string> OnAbilityActivated;

        /// <summary>
        /// 一个持续/无限效果被应用时触发。
        /// </summary>
        event Action<IAbilitySystem, ActiveGameplayEffect> OnEffectApplied;

        /// <summary>
        /// 一个持续/无限效果到期或被移除时触发。
        /// </summary>
        event Action<IAbilitySystem, ActiveGameplayEffect> OnEffectExpired;

        /// <summary>
        /// （可选）重设承载属性的 <see cref="AttributeSet"/>。
        /// </summary>
        /// <param name="attributes">承载属性的 AttributeSet，不可为空。</param>
        void SetAttributes(AttributeSet attributes);

        /// <summary>
        /// 授予一个技能。同 Id 会覆盖原定义。
        /// </summary>
        /// <param name="ability">技能定义。</param>
        void GrantAbility(AbilityDefinition ability);

        /// <summary>
        /// 移除一个技能（同时清除其冷却记录）。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>存在并被移除返回 true。</returns>
        bool RemoveAbility(string abilityId);

        /// <summary>
        /// 是否已授予某技能。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>已授予返回 true。</returns>
        bool HasAbility(string abilityId);

        /// <summary>
        /// 尝试激活技能。依次校验：是否授予、标签门槛、冷却、资源；
        /// 全部通过后扣除消耗、施加激活效果并开始冷却。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>激活结果。</returns>
        AbilityActivationResult TryActivate(string abilityId);

        /// <summary>
        /// 应用一个效果。Instant 立即修改 BaseValue 并返回 null；
        /// Duration/Infinite 创建并跟踪一个激活实例并授予标签。
        /// </summary>
        /// <param name="effect">效果定义。</param>
        /// <param name="source">效果来源，可为空。</param>
        /// <returns>Duration/Infinite 返回激活实例；Instant 返回 null。</returns>
        ActiveGameplayEffect ApplyEffect(GameplayEffectDefinition effect, object source = null);

        /// <summary>
        /// 移除一个激活效果（撤销其修饰器、收回授予标签）。
        /// </summary>
        /// <param name="effect">激活效果实例。</param>
        /// <returns>存在并被移除返回 true。</returns>
        bool RemoveEffect(ActiveGameplayEffect effect);

        /// <summary>
        /// 获取技能剩余冷却时间，无冷却返回 0。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>剩余冷却（秒）。</returns>
        float GetCooldownRemaining(string abilityId);

        /// <summary>
        /// 技能是否处于冷却中。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>冷却中返回 true。</returns>
        bool IsOnCooldown(string abilityId);

        // 推进系统时间（递减冷却、推进激活效果的周期结算与到期回收）由 ITickable.Tick(float) 提供。
    }
}
