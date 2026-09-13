//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;
using EjoyFramework.GamePlay.Attributes;

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 技能/效果系统（GAS 风格）。挂接到一个 <see cref="AttributeSet"/>，
    /// 负责技能授予、激活（消耗/冷却/标签门槛）、效果应用与到期、以及周期性结算。
    /// 每个拥有者（如一个单位）持有一个实例；由拥有者逐帧调用 <see cref="Tick(float)"/> 推进，
    /// 不是全局 Framework 模块。
    /// </summary>
    /// <remarks>
    /// 效果语义：
    /// - Instant：立即对目标属性的 BaseValue 做一次永久修改（见 ApplyInstantModifier 的运算约定）。
    /// - Duration/Infinite：以本激活效果实例为 Source 向 AttributeSet 添加可移除修饰器；
    ///   到期或手动移除时通过 RemoveModifiersFromSource 精确撤销，并同步增删授予标签。
    /// - Period>0：每隔 Period 秒，将效果内各修饰器按其 ModifierOp 施加到 BaseValue（DOT/HOT），
    ///   支持 Flat / PercentAdd / PercentMult；不支持 Override（每 tick 覆盖会破坏周期语义，ApplyEffect 入口告警并跳过）。
    /// 冷却在 Tick 中递减并钳制在 0。
    /// </remarks>
    public sealed class AbilitySystem : IAbilitySystem
    {
        private AttributeSet m_Attributes;
        private readonly GameplayTagSet m_Tags = new GameplayTagSet();

        // 授予标签引用计数：同一标签可被多个激活效果同时授予。
        // 仅在计数 0->1 时真正写入 m_Tags，1->0 时才真正移除，
        // 避免某个效果先到期时误删仍被其它效果授予的共享标签。
        private readonly Dictionary<string, int> m_GrantedTagCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, AbilityDefinition> m_Abilities =
            new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> m_Cooldowns =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<ActiveGameplayEffect> m_ActiveEffects = new List<ActiveGameplayEffect>();

        // 在 Tick 中收集到期效果，避免在遍历时修改集合。
        private readonly List<ActiveGameplayEffect> m_ExpiredBuffer = new List<ActiveGameplayEffect>();

        // 在 Tick 中快照冷却键，避免在遍历字典时修改它（Mono/IL2CPP 下即便更新已存在键的值也会
        // 改变集合版本号，导致 foreach 抛 "Collection was modified"）。复用以避免热路径分配。
        private readonly List<string> m_CooldownKeyBuffer = new List<string>();

        /// <summary>
        /// 无参构造：默认挂接一个空的 <see cref="AttributeSet"/>，可随后通过
        /// <see cref="SetAttributes(AttributeSet)"/> 注入业务属性集。
        /// </summary>
        public AbilitySystem()
        {
            m_Attributes = new AttributeSet();
        }

        /// <summary>
        /// 以已有属性集构造技能系统（每个拥有者一个实例的常用方式）。
        /// </summary>
        /// <param name="attributes">承载属性的 AttributeSet，不可为空。</param>
        public AbilitySystem(AttributeSet attributes)
        {
            m_Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        /// <summary>
        /// 承载的属性集。
        /// </summary>
        public AttributeSet Attributes
        {
            get { return m_Attributes; }
        }

        /// <summary>
        /// （可选）重设承载属性的 <see cref="AttributeSet"/>。
        /// </summary>
        /// <param name="attributes">承载属性的 AttributeSet，不可为空。</param>
        public void SetAttributes(AttributeSet attributes)
        {
            m_Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        /// <summary>
        /// 拥有者的标签集合（受效果授予标签影响）。
        /// </summary>
        public GameplayTagSet Tags
        {
            get { return m_Tags; }
        }

        /// <summary>
        /// 当前所有激活效果（只读迭代）。
        /// </summary>
        public IEnumerable<ActiveGameplayEffect> ActiveEffects
        {
            get { return m_ActiveEffects; }
        }

        /// <summary>
        /// 技能被成功激活时触发：（系统, 技能Id）。
        /// </summary>
        public event Action<IAbilitySystem, string> OnAbilityActivated;

        /// <summary>
        /// 一个持续/无限效果被应用时触发。
        /// </summary>
        public event Action<IAbilitySystem, ActiveGameplayEffect> OnEffectApplied;

        /// <summary>
        /// 一个持续/无限效果到期或被移除时触发。
        /// </summary>
        public event Action<IAbilitySystem, ActiveGameplayEffect> OnEffectExpired;

        /// <summary>
        /// 授予一个技能。同 Id 会覆盖原定义。
        /// </summary>
        /// <param name="ability">技能定义。</param>
        public void GrantAbility(AbilityDefinition ability)
        {
            if (ability == null)
            {
                throw new ArgumentNullException(nameof(ability));
            }

            m_Abilities[ability.Id] = ability;
        }

        /// <summary>
        /// 移除一个技能（同时清除其冷却记录）。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>存在并被移除返回 true。</returns>
        public bool RemoveAbility(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId))
            {
                return false;
            }

            m_Cooldowns.Remove(abilityId);
            return m_Abilities.Remove(abilityId);
        }

        /// <summary>
        /// 是否已授予某技能。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>已授予返回 true。</returns>
        public bool HasAbility(string abilityId)
        {
            return !string.IsNullOrEmpty(abilityId) && m_Abilities.ContainsKey(abilityId);
        }

        /// <summary>
        /// 尝试激活技能。依次校验：是否授予、标签门槛、冷却、资源；
        /// 全部通过后扣除消耗、施加激活效果并开始冷却。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>激活结果。</returns>
        public AbilityActivationResult TryActivate(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId) || !m_Abilities.TryGetValue(abilityId, out AbilityDefinition ability))
            {
                return AbilityActivationResult.NotGranted;
            }

            // 标签门槛：必需标签须全部满足，禁止标签须一个都不持有。
            if (!m_Tags.HasAll(ability.RequiredTags) || m_Tags.HasAny(ability.BlockedByTags))
            {
                return AbilityActivationResult.Blocked;
            }

            if (IsOnCooldown(abilityId))
            {
                return AbilityActivationResult.OnCooldown;
            }

            if (!CanAfford(ability))
            {
                return AbilityActivationResult.InsufficientResource;
            }

            // 支付消耗：永久从 BaseValue 扣除。
            PayCosts(ability);

            // 施加激活效果。
            IReadOnlyList<GameplayEffectDefinition> effects = ability.EffectsOnActivate;
            for (int i = 0; i < effects.Count; i++)
            {
                ApplyEffect(effects[i], this);
            }

            // 开始冷却。
            if (ability.Cooldown > 0f)
            {
                m_Cooldowns[abilityId] = ability.Cooldown;
            }

            OnAbilityActivated?.Invoke(this, abilityId);
            return AbilityActivationResult.Success;
        }

        /// <summary>
        /// 应用一个效果。Instant 立即修改 BaseValue 并返回 null；
        /// Duration/Infinite 创建并跟踪一个激活实例并授予标签。
        /// 非周期效果会以激活实例为 Source 添加可移除修饰器（到期撤销）；
        /// 周期效果（Period&gt;0）不添加常驻修饰器，而是每个周期按各修饰器的 ModifierOp 施加到 BaseValue（DOT/HOT，
        /// 见 ApplyPeriodicTick）；周期效果不支持 Override 运算，入口处会告警并在结算时跳过。
        /// </summary>
        /// <param name="effect">效果定义。</param>
        /// <param name="source">效果来源，可为空。</param>
        /// <returns>Duration/Infinite 返回激活实例；Instant 返回 null。</returns>
        public ActiveGameplayEffect ApplyEffect(GameplayEffectDefinition effect, object source = null)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            if (effect.DurationType == EffectDurationType.Instant)
            {
                ApplyInstantModifiers(effect);
                return null;
            }

            ActiveGameplayEffect active = new ActiveGameplayEffect(effect, source);

            // 周期效果按周期把各修饰器按其 ModifierOp 施加到 BaseValue（见 ApplyPeriodicTick），
            // 不添加常驻修饰器（避免与周期结算重复计数）；此处仅对其 Override 运算做一次性校验告警。
            // 非周期的持续/无限效果，以激活实例为 Source 添加可移除修饰器，到期统一撤销。
            if (effect.Period <= 0f)
            {
                IReadOnlyList<AttributeModifier> modifiers = effect.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    AttributeModifier template = modifiers[i];
                    m_Attributes.AddModifier(new AttributeModifier(
                        template.AttributeId, template.Op, template.Magnitude, active, template.Priority));
                }
            }
            else
            {
                // 周期效果（DOT/HOT）支持 Flat / PercentAdd / PercentMult（见 ApplyPeriodicTick）。
                // Override 每 tick 覆盖会无视累积、破坏周期语义，不支持：此处告警，运行期由 ApplyPeriodicTick 跳过。
                IReadOnlyList<AttributeModifier> modifiers = effect.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (modifiers[i].Op == ModifierOp.Override)
                    {
                        FrameworkLog.Warning(
                            "AbilitySystem.ApplyEffect: 周期效果 '{0}' 的修饰器 '{1}' 使用了不支持的 Override 运算，将被跳过。",
                            effect.Id, modifiers[i].AttributeId);
                    }
                }
            }

            // 授予标签（引用计数，支持多效果共享同一标签）。
            IReadOnlyList<string> grantedTags = effect.GrantedTags;
            for (int i = 0; i < grantedTags.Count; i++)
            {
                AddGrantedTag(grantedTags[i]);
            }

            m_ActiveEffects.Add(active);
            OnEffectApplied?.Invoke(this, active);
            return active;
        }

        /// <summary>
        /// 移除一个激活效果（撤销其修饰器、收回授予标签）。
        /// </summary>
        /// <param name="effect">激活效果实例。</param>
        /// <returns>存在并被移除返回 true。</returns>
        public bool RemoveEffect(ActiveGameplayEffect effect)
        {
            if (effect == null)
            {
                return false;
            }

            int index = m_ActiveEffects.IndexOf(effect);
            if (index < 0)
            {
                return false;
            }

            m_ActiveEffects.RemoveAt(index);
            EndEffect(effect);
            return true;
        }

        /// <summary>
        /// 获取技能剩余冷却时间，无冷却返回 0。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>剩余冷却（秒）。</returns>
        public float GetCooldownRemaining(string abilityId)
        {
            if (!string.IsNullOrEmpty(abilityId) && m_Cooldowns.TryGetValue(abilityId, out float remaining))
            {
                return remaining > 0f ? remaining : 0f;
            }

            return 0f;
        }

        /// <summary>
        /// 技能是否处于冷却中。
        /// </summary>
        /// <param name="abilityId">技能 Id。</param>
        /// <returns>冷却中返回 true。</returns>
        public bool IsOnCooldown(string abilityId)
        {
            return GetCooldownRemaining(abilityId) > 0f;
        }

        /// <summary>
        /// 清理运行时状态：技能、冷却、激活效果、标签与内部缓冲（拥有者销毁时调用）。
        /// </summary>
        public void Shutdown()
        {
            m_Abilities.Clear();
            m_Cooldowns.Clear();
            m_ActiveEffects.Clear();

            // GameplayTagSet 为 POCO（无 Clear），快照后逐个移除以清空持有标签。
            List<string> heldTags = new List<string>(m_Tags.Tags);
            for (int i = 0; i < heldTags.Count; i++)
            {
                m_Tags.Remove(heldTags[i]);
            }

            m_GrantedTagCounts.Clear();

            m_ExpiredBuffer.Clear();
            m_CooldownKeyBuffer.Clear();

            OnAbilityActivated = null;
            OnEffectApplied = null;
            OnEffectExpired = null;
        }

        /// <summary>
        /// 推进系统时间：递减冷却、推进激活效果（周期结算与到期回收）。
        /// 热路径避免分配；到期效果先入缓冲再统一处理。
        /// </summary>
        /// <param name="deltaTime">本次时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            TickCooldowns(deltaTime);
            TickEffects(deltaTime);
        }

        private void TickCooldowns(float deltaTime)
        {
            if (m_Cooldowns.Count == 0)
            {
                return;
            }

            // 先快照键（迭代 Keys 不修改字典），再遍历快照对字典自由增删改，避免“遍历中修改”。
            m_CooldownKeyBuffer.Clear();
            foreach (string key in m_Cooldowns.Keys)
            {
                m_CooldownKeyBuffer.Add(key);
            }

            for (int i = 0; i < m_CooldownKeyBuffer.Count; i++)
            {
                string key = m_CooldownKeyBuffer[i];
                float remaining = m_Cooldowns[key] - deltaTime;
                if (remaining <= 0f)
                {
                    m_Cooldowns.Remove(key);
                }
                else
                {
                    m_Cooldowns[key] = remaining;
                }
            }

            m_CooldownKeyBuffer.Clear();
        }

        private void TickEffects(float deltaTime)
        {
            if (m_ActiveEffects.Count == 0)
            {
                return;
            }

            m_ExpiredBuffer.Clear();

            for (int i = 0; i < m_ActiveEffects.Count; i++)
            {
                ActiveGameplayEffect active = m_ActiveEffects[i];
                GameplayEffectDefinition def = active.Definition;

                // 周期结算（DOT/HOT）。对持续型效果，本帧只在「剩余存活时间」内累积周期，
                // 避免效果在本帧中途到期后，仍按整帧 deltaTime 多结算一次到期之后的周期。
                if (def.Period > 0f)
                {
                    float periodAdvance = deltaTime;
                    if (def.DurationType == EffectDurationType.Duration && active.m_RemainingTime < deltaTime)
                    {
                        periodAdvance = active.m_RemainingTime > 0f ? active.m_RemainingTime : 0f;
                    }
                    active.m_PeriodAccumulator += periodAdvance;
                    while (active.m_PeriodAccumulator >= def.Period)
                    {
                        active.m_PeriodAccumulator -= def.Period;
                        ApplyPeriodicTick(def);
                    }
                }

                // 持续型效果倒计时。
                if (def.DurationType == EffectDurationType.Duration)
                {
                    active.m_RemainingTime -= deltaTime;
                    if (active.m_RemainingTime <= 0f)
                    {
                        active.m_RemainingTime = 0f;
                        active.m_Expired = true;
                        m_ExpiredBuffer.Add(active);
                    }
                }
            }

            // 统一回收到期效果。
            for (int i = 0; i < m_ExpiredBuffer.Count; i++)
            {
                ActiveGameplayEffect expired = m_ExpiredBuffer[i];
                int index = m_ActiveEffects.IndexOf(expired);
                if (index >= 0)
                {
                    m_ActiveEffects.RemoveAt(index);
                }

                EndEffect(expired);
            }

            m_ExpiredBuffer.Clear();
        }

        /// <summary>
        /// 结束一个激活效果：撤销其修饰器、收回授予标签并触发到期事件。
        /// </summary>
        private void EndEffect(ActiveGameplayEffect effect)
        {
            m_Attributes.RemoveModifiersFromSource(effect);

            IReadOnlyList<string> grantedTags = effect.Definition.GrantedTags;
            for (int i = 0; i < grantedTags.Count; i++)
            {
                ReleaseGrantedTag(grantedTags[i]);
            }

            OnEffectExpired?.Invoke(this, effect);
        }

        /// <summary>
        /// 按引用计数授予一个标签：仅在计数 0-&gt;1 时真正写入标签集合。
        /// 空白标签忽略（与 <see cref="GameplayTagSet.Add(string)"/> 语义一致）。
        /// </summary>
        private void AddGrantedTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            if (m_GrantedTagCounts.TryGetValue(tag, out int count))
            {
                m_GrantedTagCounts[tag] = count + 1;
            }
            else
            {
                m_GrantedTagCounts[tag] = 1;
                m_Tags.Add(tag);
            }
        }

        /// <summary>
        /// 按引用计数释放一个标签：仅在计数 1-&gt;0 时真正从标签集合移除，
        /// 确保仍被其它激活效果授予的共享标签不被误删。
        /// </summary>
        private void ReleaseGrantedTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !m_GrantedTagCounts.TryGetValue(tag, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                m_GrantedTagCounts.Remove(tag);
                m_Tags.Remove(tag);
            }
            else
            {
                m_GrantedTagCounts[tag] = count - 1;
            }
        }

        /// <summary>
        /// 校验技能是否可负担其全部消耗。
        /// </summary>
        private bool CanAfford(AbilityDefinition ability)
        {
            IReadOnlyList<AbilityCost> costs = ability.Costs;
            for (int i = 0; i < costs.Count; i++)
            {
                AbilityCost cost = costs[i];
                if (m_Attributes.GetValue(cost.AttributeId) < cost.Amount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 支付技能消耗：从对应属性 BaseValue 永久扣除。
        /// </summary>
        private void PayCosts(AbilityDefinition ability)
        {
            IReadOnlyList<AbilityCost> costs = ability.Costs;
            for (int i = 0; i < costs.Count; i++)
            {
                AbilityCost cost = costs[i];
                if (m_Attributes.TryGet(cost.AttributeId, out GameplayAttribute attr))
                {
                    attr.BaseValue -= cost.Amount;
                }
            }
        }

        /// <summary>
        /// 应用瞬时效果的全部修饰器到 BaseValue。
        /// 运算约定（简单且明确）：
        /// - Flat：BaseValue += Magnitude
        /// - Override：BaseValue = Magnitude
        /// - PercentAdd / PercentMult：BaseValue *= (1 + Magnitude)
        /// </summary>
        private void ApplyInstantModifiers(GameplayEffectDefinition effect)
        {
            IReadOnlyList<AttributeModifier> modifiers = effect.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                AttributeModifier modifier = modifiers[i];
                if (!m_Attributes.TryGet(modifier.AttributeId, out GameplayAttribute attr))
                {
                    continue;
                }

                switch (modifier.Op)
                {
                    case ModifierOp.Flat:
                        attr.BaseValue += modifier.Magnitude;
                        break;
                    case ModifierOp.Override:
                        attr.BaseValue = modifier.Magnitude;
                        break;
                    case ModifierOp.PercentAdd:
                    case ModifierOp.PercentMult:
                        attr.BaseValue *= 1f + modifier.Magnitude;
                        break;
                    default:
                        attr.BaseValue += modifier.Magnitude;
                        break;
                }
            }
        }

        /// <summary>
        /// 周期结算一次：将效果内各修饰器按其 <see cref="ModifierOp"/> 逐个施加到 BaseValue（DOT/HOT），
        /// 与 <see cref="ApplyInstantModifiers"/> 的运算约定保持一致：
        /// - Flat：BaseValue += Magnitude
        /// - PercentAdd / PercentMult：BaseValue *= (1 + Magnitude)
        /// Override 不在此支持（每 tick 覆盖会无视累积值、破坏 DOT/HOT 语义），
        /// 已在 <see cref="ApplyEffect"/> 入口对周期效果做过校验并跳过，故这里遇到 Override 仅防御性跳过。
        /// </summary>
        private void ApplyPeriodicTick(GameplayEffectDefinition effect)
        {
            IReadOnlyList<AttributeModifier> modifiers = effect.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                AttributeModifier modifier = modifiers[i];
                if (!m_Attributes.TryGet(modifier.AttributeId, out GameplayAttribute attr))
                {
                    continue;
                }

                switch (modifier.Op)
                {
                    case ModifierOp.Flat:
                        attr.BaseValue += modifier.Magnitude;
                        break;
                    case ModifierOp.PercentAdd:
                    case ModifierOp.PercentMult:
                        attr.BaseValue *= 1f + modifier.Magnitude;
                        break;
                    case ModifierOp.Override:
                        // 周期效果不支持 Override（见 ApplyEffect 校验）；防御性跳过。
                        break;
                    default:
                        attr.BaseValue += modifier.Magnitude;
                        break;
                }
            }
        }
    }
}
