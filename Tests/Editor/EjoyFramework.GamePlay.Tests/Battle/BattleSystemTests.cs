//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.GamePlay.Battle;
using System.Collections;
using System.Reflection;

namespace EjoyFramework.GamePlay.Tests
{
    public class BattleSystemTests
    {
        private sealed class TestEntity : IBattleEntity
        {
            public int EntityId { get; set; }
            public bool IsAlive => CurrentHp > 0;
            public int CurrentHp { get; set; } = 100;
            public int MaxHp { get; set; } = 100;
            public int Attack { get; set; } = 50;
            public int Defense { get; set; } = 0;
            public int Camp { get; set; } = 0;
            public int LastDamage;
            public int LastHeal;
            public void ApplyDamage(int amount, IBattleEntity attacker) { LastDamage = amount; CurrentHp -= amount; }
            public void ApplyHeal(int amount, IBattleEntity healer) { LastHeal = amount; CurrentHp += amount; }
        }

        // ===== Damage Calculator =====

        [Test]
        public void DefaultDamageCalculator_ComputesByFormula()
        {
            var atk = new TestEntity { Attack = 100 };
            var def = new TestEntity { Defense = 100 };
            var skill = new SkillDef { BaseDamage = 50, DamageScaling = 0.5f };
            // raw = 50 + 100 * 0.5 = 100. reduction = 100/200 = 0.5. dmg = 100 * 0.5 = 50
            var calc = new DefaultDamageCalculator();
            Assert.AreEqual(50, calc.Calculate(atk, def, skill));
        }

        [Test]
        public void DefaultDamageCalculator_MinimumDamageIsOne()
        {
            var atk = new TestEntity { Attack = 1 };
            var def = new TestEntity { Defense = 10000 };
            var skill = new SkillDef { BaseDamage = 1, DamageScaling = 0.1f };
            Assert.GreaterOrEqual(new DefaultDamageCalculator().Calculate(atk, def, skill), 1);
        }

        // ===== SkillManager =====

        [Test]
        public void SkillManager_RegisterAndCast_DealsDamage()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 30, DamageScaling = 0f, CooldownSeconds = 0 });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };
            Assert.IsTrue(sm.Cast(atk, 1, def));
            Assert.GreaterOrEqual(def.LastDamage, 1);
            Assert.Less(def.CurrentHp, 100);
        }

        [Test]
        public void SkillManager_CooldownPreventsImmediateRecast()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 5, CooldownSeconds = 5f });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };
            Assert.IsTrue(sm.Cast(atk, 1, def));
            Assert.IsFalse(sm.CanCast(atk, 1));
            Assert.IsFalse(sm.Cast(atk, 1, def));   // cooldown 内不能再放
        }

        [Test]
        public void SkillManager_CooldownExpiresAfterUpdate()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 5, CooldownSeconds = 2f });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };
            sm.Cast(atk, 1, def);

            sm.Update(1f, 1f);
            Assert.IsFalse(sm.CanCast(atk, 1));   // 还差 1s
            sm.Update(1.1f, 1.1f);
            Assert.IsTrue(sm.CanCast(atk, 1));
        }

        [Test]
        public void SkillManager_DeadCaster_CannotCast()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1 });
            var atk = new TestEntity { EntityId = 1, CurrentHp = 0 };
            Assert.IsFalse(sm.CanCast(atk, 1));
        }

        [Test]
        public void SkillManager_FiresSkillCastEvent()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 10 });
            int casts = 0;
            sm.SkillCast += (c, def, t) => casts++;
            sm.Cast(new TestEntity { EntityId = 1 }, 1, new TestEntity { EntityId = 2 });
            Assert.AreEqual(1, casts);
        }

        [Test]
        public void SkillManager_RegisterSkill_RejectsUnsupportedManaCost()
        {
            var sm = new SkillManager();
            FrameworkException ex = Assert.Throws<FrameworkException>(() =>
                sm.RegisterSkill(new SkillDef { SkillId = 1, ManaCost = 10 }));
            StringAssert.Contains("ManaCost", ex.Message);
        }

        [Test]
        public void SkillManager_RegisterSkill_RejectsUnsupportedCastTime()
        {
            var sm = new SkillManager();
            FrameworkException ex = Assert.Throws<FrameworkException>(() =>
                sm.RegisterSkill(new SkillDef { SkillId = 1, CastTimeSeconds = 0.5f }));
            StringAssert.Contains("CastTimeSeconds", ex.Message);
        }

        [Test]
        public void SkillManager_RegisterSkill_RejectsUnsupportedAreaOfEffect()
        {
            var sm = new SkillManager();
            FrameworkException ex = Assert.Throws<FrameworkException>(() =>
                sm.RegisterSkill(new SkillDef { SkillId = 1, Targeting = TargetingMode.AreaOfEffect }));
            StringAssert.Contains("AreaOfEffect", ex.Message);
        }

        [Test]
        public void SkillManager_ExpiredCooldown_ReleasesCasterEntry()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 1, CooldownSeconds = 1f });
            var caster = new TestEntity { EntityId = 42 };

            Assert.IsTrue(sm.Cast(caster, 1, new TestEntity { EntityId = 2 }));
            Assert.AreEqual(1, GetPrivateDictionaryCount(sm, "m_LastCastTime"));

            sm.Update(1.1f, 1.1f);

            Assert.AreEqual(0, GetPrivateDictionaryCount(sm, "m_LastCastTime"));
        }

        // ===== BuffManager =====

        [Test]
        public void BuffManager_ApplyBuff_AppearsInGetBuffs()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 5f });
            var t = new TestEntity { EntityId = 1 };
            bm.ApplyBuff(t, 100, null);
            Assert.AreEqual(1, bm.GetBuffs(t).Count);
            Assert.IsTrue(bm.HasBuff(t, 100));
        }

        [Test]
        public void BuffManager_DOT_DealsDamageEachTick()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef
            {
                BuffId = 200,
                Kind = BuffKind.DOT,
                DurationSeconds = 10f,
                TickIntervalSeconds = 1f,
                TickAmount = 5,
            });
            var t = new TestEntity { EntityId = 1, CurrentHp = 100 };
            bm.ApplyBuff(t, 200, null);

            // Tick 一秒 → 应触发 1 次伤害
            bm.Update(1.0f, 1.0f);
            Assert.AreEqual(95, t.CurrentHp);
            bm.Update(1.0f, 1.0f);
            Assert.AreEqual(90, t.CurrentHp);
        }

        [Test]
        public void BuffManager_StackRule_RefreshExtendsDuration()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 5f, Stacking = StackRule.Refresh });
            var t = new TestEntity { EntityId = 1 };
            bm.ApplyBuff(t, 100, null);
            bm.Update(3f, 3f);
            bm.ApplyBuff(t, 100, null);   // 刷新 → 重置到 5s
            bm.Update(3f, 3f);
            Assert.IsTrue(bm.HasBuff(t, 100), "刷新后再过 3s 仍未到期");
        }

        [Test]
        public void BuffManager_StackRule_StackIncrementsLayers()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 10f, Stacking = StackRule.Stack, MaxStacks = 3 });
            var t = new TestEntity { EntityId = 1 };
            bm.ApplyBuff(t, 100, null);
            bm.ApplyBuff(t, 100, null);
            bm.ApplyBuff(t, 100, null);
            bm.ApplyBuff(t, 100, null);   // 超过 MaxStacks
            Assert.AreEqual(3, bm.GetBuffs(t)[0].Stacks);
        }

        [Test]
        public void BuffManager_ExpiresAfterDuration_FiresRemovedEvent()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 2f });
            int removed = 0;
            bm.BuffRemoved += (target, inst) => removed++;
            var t = new TestEntity { EntityId = 1 };
            bm.ApplyBuff(t, 100, null);
            bm.Update(3f, 3f);
            Assert.AreEqual(1, removed);
            Assert.IsFalse(bm.HasBuff(t, 100));
        }

        [Test]
        public void BuffManager_RemoveLastBuff_ReleasesTargetBucket()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 5f });
            var target = new TestEntity { EntityId = 7 };
            bm.ApplyBuff(target, 100, null);

            bm.RemoveBuff(target, 100);

            Assert.AreEqual(0, GetPrivateDictionaryCount(bm, "m_ByTarget"));
        }

        [Test]
        public void BuffManager_RemoveAllBuffs_ReleasesTargetBucket()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 100, DurationSeconds = 5f });
            var target = new TestEntity { EntityId = 8 };
            bm.ApplyBuff(target, 100, null);

            bm.RemoveAllBuffs(target);

            Assert.AreEqual(0, GetPrivateDictionaryCount(bm, "m_ByTarget"));
        }

        private static int GetPrivateDictionaryCount(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing field: " + fieldName);
            var dictionary = field.GetValue(instance) as IDictionary;
            Assert.IsNotNull(dictionary, "Field is not IDictionary: " + fieldName);
            return dictionary.Count;
        }
    }
}
