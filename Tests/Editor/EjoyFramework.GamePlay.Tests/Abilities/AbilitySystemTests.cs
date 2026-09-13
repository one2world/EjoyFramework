//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Abilities;
using EjoyFramework.GamePlay.Attributes;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Abilities
{
    /// <summary>
    /// 针对 <see cref="AbilitySystem"/> 技能授予、激活、冷却、消耗与标签门槛的单元测试。
    /// </summary>
    [TestFixture]
    public class AbilitySystemTests
    {
        private const float Delta = 1e-4f;

        private static AttributeSet MakeAttributes()
        {
            AttributeSet attributes = new AttributeSet();
            attributes.Add("health", 100f);
            attributes.Add("mana", 50f);
            return attributes;
        }

        private static AbilitySystem MakeSystem(AttributeSet attributes)
        {
            AbilitySystem system = new AbilitySystem();
            system.SetAttributes(attributes);
            return system;
        }

        [Test]
        public void Grant_Has_Remove_Ability()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            AbilityDefinition fireball = AbilityDefinition.Create("fireball").Build();

            Assert.IsFalse(system.HasAbility("fireball"));

            system.GrantAbility(fireball);
            Assert.IsTrue(system.HasAbility("fireball"));

            Assert.IsTrue(system.RemoveAbility("fireball"));
            Assert.IsFalse(system.HasAbility("fireball"));
            Assert.IsFalse(system.RemoveAbility("fireball"));
        }

        [Test]
        public void TryActivate_Success_PaysCost_StartsCooldown()
        {
            AttributeSet attributes = MakeAttributes();
            AbilitySystem system = MakeSystem(attributes);

            AbilityDefinition fireball = AbilityDefinition.Create("fireball")
                .WithCooldown(5f)
                .AddCost("mana", 20f)
                .Build();
            system.GrantAbility(fireball);

            string activatedId = null;
            system.OnAbilityActivated += (s, id) => activatedId = id;

            AbilityActivationResult result = system.TryActivate("fireball");

            Assert.AreEqual(AbilityActivationResult.Success, result);
            Assert.AreEqual("fireball", activatedId);
            // 法力扣除（永久 BaseValue 下降）。
            Assert.AreEqual(30f, attributes.GetValue("mana"), Delta);
            // 进入冷却。
            Assert.IsTrue(system.IsOnCooldown("fireball"));
            Assert.AreEqual(5f, system.GetCooldownRemaining("fireball"), Delta);
        }

        [Test]
        public void TryActivate_OnCooldownBeforeElapsed_SuccessAfterTick()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            AbilityDefinition dash = AbilityDefinition.Create("dash")
                .WithCooldown(3f)
                .Build();
            system.GrantAbility(dash);

            Assert.AreEqual(AbilityActivationResult.Success, system.TryActivate("dash"));
            Assert.AreEqual(AbilityActivationResult.OnCooldown, system.TryActivate("dash"));

            // 部分冷却仍未就绪。
            system.Tick(1f);
            Assert.AreEqual(AbilityActivationResult.OnCooldown, system.TryActivate("dash"));
            Assert.AreEqual(2f, system.GetCooldownRemaining("dash"), Delta);

            // 冷却结束后可再次激活。
            system.Tick(2f);
            Assert.IsFalse(system.IsOnCooldown("dash"));
            Assert.AreEqual(0f, system.GetCooldownRemaining("dash"), Delta);
            Assert.AreEqual(AbilityActivationResult.Success, system.TryActivate("dash"));
        }

        [Test]
        public void TryActivate_InsufficientResource()
        {
            AttributeSet attributes = MakeAttributes();
            AbilitySystem system = MakeSystem(attributes);

            AbilityDefinition ultimate = AbilityDefinition.Create("ultimate")
                .AddCost("mana", 200f) // 超过当前 50。
                .Build();
            system.GrantAbility(ultimate);

            Assert.AreEqual(AbilityActivationResult.InsufficientResource, system.TryActivate("ultimate"));
            // 资源未被扣除。
            Assert.AreEqual(50f, attributes.GetValue("mana"), Delta);
            Assert.IsFalse(system.IsOnCooldown("ultimate"));
        }

        [Test]
        public void TryActivate_NotGranted_ForUnknownAbility()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            Assert.AreEqual(AbilityActivationResult.NotGranted, system.TryActivate("ghost"));
        }

        [Test]
        public void TryActivate_Blocked_WhenOwnerHasBlockedTag()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            AbilityDefinition cast = AbilityDefinition.Create("cast")
                .BlockedBy("state.silenced")
                .Build();
            system.GrantAbility(cast);

            system.Tags.Add("state.silenced");
            Assert.AreEqual(AbilityActivationResult.Blocked, system.TryActivate("cast"));

            system.Tags.Remove("state.silenced");
            Assert.AreEqual(AbilityActivationResult.Success, system.TryActivate("cast"));
        }

        [Test]
        public void TryActivate_Blocked_WhenMissingRequiredTag()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            AbilityDefinition stance = AbilityDefinition.Create("power_strike")
                .RequireTag("stance.combat")
                .Build();
            system.GrantAbility(stance);

            // 缺少必需标签 -> Blocked。
            Assert.AreEqual(AbilityActivationResult.Blocked, system.TryActivate("power_strike"));

            system.Tags.Add("stance.combat");
            Assert.AreEqual(AbilityActivationResult.Success, system.TryActivate("power_strike"));
        }

        [Test]
        public void TryActivate_AppliesEffectsOnActivate()
        {
            AttributeSet attributes = MakeAttributes();
            AbilitySystem system = MakeSystem(attributes);

            GameplayEffectDefinition damageSelf = GameplayEffectDefinition.Create("recoil")
                .Instant()
                .AddModifier("health", ModifierOp.Flat, -10f)
                .Build();
            GameplayEffectDefinition buff = GameplayEffectDefinition.Create("rage")
                .ForDuration(2f)
                .AddModifier("mana", ModifierOp.Flat, 15f)
                .GrantTag("buff.rage")
                .Build();

            AbilityDefinition berserk = AbilityDefinition.Create("berserk")
                .AddEffect(damageSelf)
                .AddEffect(buff)
                .Build();
            system.GrantAbility(berserk);

            Assert.AreEqual(AbilityActivationResult.Success, system.TryActivate("berserk"));

            // 瞬时自伤 + 持续法力增益同时生效。
            Assert.AreEqual(90f, attributes.GetValue("health"), Delta);
            Assert.AreEqual(65f, attributes.GetValue("mana"), Delta);
            Assert.IsTrue(system.Tags.HasTag("buff.rage"));

            // 持续效果到期后法力增益移除，自伤永久保留。
            system.Tick(2f);
            Assert.AreEqual(90f, attributes.GetValue("health"), Delta);
            Assert.AreEqual(50f, attributes.GetValue("mana"), Delta);
            Assert.IsFalse(system.Tags.HasTag("buff.rage"));
        }

        [Test]
        public void ApplyEffect_NullArgument_Throws()
        {
            AbilitySystem system = MakeSystem(MakeAttributes());
            Assert.Throws<System.ArgumentNullException>(() => system.ApplyEffect(null));
        }
    }
}
