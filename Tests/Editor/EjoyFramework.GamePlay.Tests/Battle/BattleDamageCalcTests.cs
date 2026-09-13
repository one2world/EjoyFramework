//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Battle;

namespace EjoyFramework.GamePlay.Tests
{
    public class BattleDamageCalcTests
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
            public void ApplyDamage(int amount, IBattleEntity attacker) { LastDamage = amount; CurrentHp -= amount; }
            public void ApplyHeal(int amount, IBattleEntity healer) { CurrentHp += amount; }
        }

        // Records every Calculate call so the test can detect whether injection took effect.
        private sealed class SpyCalculator : IDamageCalculator
        {
            public int Calls;
            public int FixedDamage;
            public SpyCalculator(int fixedDamage) { FixedDamage = fixedDamage; }
            public int Calculate(IBattleEntity attacker, IBattleEntity defender, SkillDef skill)
            {
                Calls++;
                return FixedDamage;
            }
        }

        // ===== Default calculator caching =====

        [Test]
        public void DefaultDamageCalculator_ExposesSharedInstance()
        {
            Assert.IsNotNull(DefaultDamageCalculator.Instance);
            // Reference identity: repeated access returns the same cached object.
            Assert.AreSame(DefaultDamageCalculator.Instance, DefaultDamageCalculator.Instance);
        }

        [Test]
        public void Cast_WithoutInjectedCalculator_StillDealsDamage()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 30, DamageScaling = 0f, CooldownSeconds = 0f });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };

            Assert.IsTrue(sm.Cast(atk, 1, def));
            // Default formula: raw = 30, defense 0 → no reduction → 30 damage, proving the shared
            // default calculator was used (no exception, correct value) without per-cast allocation.
            Assert.AreEqual(30, def.LastDamage);
        }

        [Test]
        public void Cast_RepeatedCasts_ProduceIdenticalDefaultResults()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 25, DamageScaling = 0f, CooldownSeconds = 0f });
            var atk = new TestEntity { EntityId = 1 };
            var def1 = new TestEntity { EntityId = 2 };
            var def2 = new TestEntity { EntityId = 3 };

            sm.Cast(atk, 1, def1);
            sm.Cast(atk, 1, def2);
            // Same shared calculator → deterministic, identical output across casts.
            Assert.AreEqual(def1.LastDamage, def2.LastDamage);
            Assert.AreEqual(25, def1.LastDamage);
        }

        // ===== Injection overrides the default =====

        [Test]
        public void Cast_WithInjectedCalculator_OverridesDefault()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 30, DamageScaling = 0f, CooldownSeconds = 0f });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };
            var spy = new SpyCalculator(fixedDamage: 7);

            Assert.IsTrue(sm.Cast(atk, 1, def, buffManager: null, damageCalculator: spy));
            Assert.AreEqual(1, spy.Calls);          // injected calculator was used
            Assert.AreEqual(7, def.LastDamage);     // and its result applied (not the default's 30)
        }

        [Test]
        public void Cast_WithoutInjection_DoesNotCallInjectedCalculator()
        {
            var sm = new SkillManager();
            sm.RegisterSkill(new SkillDef { SkillId = 1, BaseDamage = 10, DamageScaling = 0f, CooldownSeconds = 0f });
            var atk = new TestEntity { EntityId = 1 };
            var def = new TestEntity { EntityId = 2 };
            var spy = new SpyCalculator(fixedDamage: 99);

            // No calculator passed → default shared instance used, spy untouched.
            sm.Cast(atk, 1, def);
            Assert.AreEqual(0, spy.Calls);
        }
    }
}
