//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>技能配置定义（业务通过 DataTable 配置）。</summary>
    [Serializable]
    public sealed class SkillDef
    {
        public int SkillId;
        public string Name;
        public float CooldownSeconds;
        public float CastTimeSeconds;     // 预留：当前未接入施法调度器，非 0 配置会在注册时拒绝
        public int BaseDamage;
        public float DamageScaling;       // attacker.Attack * DamageScaling = bonus
        public int ManaCost;              // 预留：当前未接入资源接口，非 0 配置会在注册时拒绝
        public TargetingMode Targeting;
        public int[] BuffsApplied;        // 命中目标后施加的 BuffId 列表
    }

    public enum TargetingMode
    {
        SingleTarget,
        AreaOfEffect,
        Self,
    }

    /// <summary>技能管理器接口。</summary>
    public interface ISkillManager
    {
        void RegisterSkill(SkillDef def);
        SkillDef GetSkill(int skillId);
        bool CanCast(IBattleEntity caster, int skillId);
        bool Cast(IBattleEntity caster, int skillId, IBattleEntity target, IBuffManager buffManager = null, IDamageCalculator damageCalculator = null);
        float GetRemainingCooldown(IBattleEntity caster, int skillId);

        event Action<IBattleEntity, SkillDef, IBattleEntity> SkillCast;
        event Action<IBattleEntity, SkillDef, IBattleEntity, int> SkillHit;   // caster, def, target, damage
    }

    /// <summary>伤害计算器抽象：业务可注入自家公式。</summary>
    public interface IDamageCalculator
    {
        int Calculate(IBattleEntity attacker, IBattleEntity defender, SkillDef skill);
    }

    /// <summary>默认伤害公式：(baseDamage + attacker.Attack * scaling) * (1 - defense/(defense+100))，向下取整 1+。</summary>
    public sealed class DefaultDamageCalculator : IDamageCalculator
    {
        /// <summary>共享无状态实例：Cast 未注入计算器时复用此实例，避免每次施法分配。</summary>
        public static readonly DefaultDamageCalculator Instance = new DefaultDamageCalculator();

        public int Calculate(IBattleEntity attacker, IBattleEntity defender, SkillDef skill)
        {
            int rawAttack = (int)(skill.BaseDamage + attacker.Attack * skill.DamageScaling);
            float reduction = defender.Defense / (defender.Defense + 100f);
            int dmg = (int)(rawAttack * (1f - reduction));
            return dmg < 1 ? 1 : dmg;
        }
    }

    public sealed class SkillManager : FrameworkModule, ISkillManager
    {
        private readonly Dictionary<int, SkillDef> m_Skills = new Dictionary<int, SkillDef>();
        // (casterEntityId, skillId) → lastCastTime
        private readonly Dictionary<long, float> m_LastCastTime = new Dictionary<long, float>();
        private readonly List<long> m_ExpiredCooldownKeys = new List<long>();
        private float m_NowSeconds;

        public event Action<IBattleEntity, SkillDef, IBattleEntity> SkillCast;
        public event Action<IBattleEntity, SkillDef, IBattleEntity, int> SkillHit;

        public override int Priority { get { return 0; } }
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_NowSeconds += elapseSeconds;
            if (m_LastCastTime.Count == 0) return;

            m_ExpiredCooldownKeys.Clear();
            foreach (var pair in m_LastCastTime)
            {
                int skillId = unchecked((int)(uint)pair.Key);
                if (!m_Skills.TryGetValue(skillId, out var def)
                    || pair.Value + def.CooldownSeconds <= m_NowSeconds)
                {
                    m_ExpiredCooldownKeys.Add(pair.Key);
                }
            }
            for (int i = 0; i < m_ExpiredCooldownKeys.Count; i++)
            {
                m_LastCastTime.Remove(m_ExpiredCooldownKeys[i]);
            }
            m_ExpiredCooldownKeys.Clear();
        }

        public override void Shutdown()
        {
            m_Skills.Clear();
            m_LastCastTime.Clear();
            m_ExpiredCooldownKeys.Clear();
            m_NowSeconds = 0f;
        }

        public void RegisterSkill(SkillDef def)
        {
            Framework.EnsureMainThread(nameof(RegisterSkill));
            if (def == null) throw new FrameworkException("def is null.");
            ValidateSupportedDefinition(def);
            m_Skills[def.SkillId] = def;
        }

        private static void ValidateSupportedDefinition(SkillDef def)
        {
            if (def.ManaCost != 0)
            {
                throw new FrameworkException(
                    "SkillDef.ManaCost is not supported until a battle resource contract is configured; use 0.");
            }
            if (def.CastTimeSeconds != 0f)
            {
                throw new FrameworkException(
                    "SkillDef.CastTimeSeconds is not supported until cast scheduling/interruption is configured; use 0.");
            }
            if (def.Targeting == TargetingMode.AreaOfEffect)
            {
                throw new FrameworkException(
                    "SkillDef.Targeting AreaOfEffect is not supported until an area target resolver is configured.");
            }
        }

        public SkillDef GetSkill(int skillId)
        {
            return m_Skills.TryGetValue(skillId, out var d) ? d : null;
        }

        public bool CanCast(IBattleEntity caster, int skillId)
        {
            if (caster == null || !caster.IsAlive) return false;
            if (!m_Skills.TryGetValue(skillId, out var def)) return false;
            return GetRemainingCooldown(caster, skillId) <= 0f;
        }

        public float GetRemainingCooldown(IBattleEntity caster, int skillId)
        {
            if (caster == null) return 0f;
            if (!m_Skills.TryGetValue(skillId, out var def)) return 0f;
            long key = MakeKey(caster.EntityId, skillId);
            if (!m_LastCastTime.TryGetValue(key, out float last)) return 0f;
            float remaining = (last + def.CooldownSeconds) - m_NowSeconds;
            return remaining > 0f ? remaining : 0f;
        }

        public bool Cast(IBattleEntity caster, int skillId, IBattleEntity target,
            IBuffManager buffManager = null, IDamageCalculator damageCalculator = null)
        {
            Framework.EnsureMainThread(nameof(Cast));
            if (!CanCast(caster, skillId)) return false;
            var def = m_Skills[skillId];
            m_LastCastTime[MakeKey(caster.EntityId, skillId)] = m_NowSeconds;

            try { SkillCast?.Invoke(caster, def, target); }
            catch (Exception ex) { FrameworkLog.Error("SkillCast handler threw: {0}", ex); }

            // 伤害结算
            if (target != null && target.IsAlive && def.Targeting != TargetingMode.Self)
            {
                var calc = damageCalculator ?? DefaultDamageCalculator.Instance;
                int dmg = calc.Calculate(caster, target, def);
                target.ApplyDamage(dmg, caster);
                try { SkillHit?.Invoke(caster, def, target, dmg); }
                catch (Exception ex) { FrameworkLog.Error("SkillHit handler threw: {0}", ex); }
            }

            // 施加 buff
            if (buffManager != null && def.BuffsApplied != null)
            {
                IBattleEntity applyTo = def.Targeting == TargetingMode.Self ? caster : target;
                foreach (var buffId in def.BuffsApplied) buffManager.ApplyBuff(applyTo, buffId, caster);
            }

            return true;
        }

        private static long MakeKey(int entityId, int skillId) => ((long)entityId << 32) | (uint)skillId;
    }
}
