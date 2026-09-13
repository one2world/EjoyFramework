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
    /// 针对效果应用语义的单元测试：瞬时、持续、周期与授予标签。
    /// </summary>
    [TestFixture]
    public class GameplayEffectTests
    {
        private const float Delta = 1e-4f;

        private static AbilitySystem MakeSystem()
        {
            AttributeSet attributes = new AttributeSet();
            attributes.Add("health", 100f);
            attributes.Add("mana", 50f);
            AbilitySystem system = new AbilitySystem();
            system.SetAttributes(attributes);
            return system;
        }

        [Test]
        public void InstantEffect_ModifiesBaseValuePermanently()
        {
            AbilitySystem system = MakeSystem();
            GameplayEffectDefinition heal = GameplayEffectDefinition.Create("heal")
                .Instant()
                .AddModifier("health", ModifierOp.Flat, 25f)
                .Build();

            ActiveGameplayEffect active = system.ApplyEffect(heal);

            Assert.IsNull(active); // 瞬时效果不返回运行时实例。
            Assert.AreEqual(125f, system.Attributes.GetValue("health"), Delta);

            // 永久性：Tick 之后依旧保持。
            system.Tick(10f);
            Assert.AreEqual(125f, system.Attributes.GetValue("health"), Delta);
        }

        [Test]
        public void DurationEffect_AddsModifier_ThenRemovedOnExpiry()
        {
            AbilitySystem system = MakeSystem();
            GameplayEffectDefinition shield = GameplayEffectDefinition.Create("armor_buff")
                .ForDuration(3f)
                .AddModifier("health", ModifierOp.Flat, 50f)
                .Build();

            int appliedCount = 0;
            int expiredCount = 0;
            system.OnEffectApplied += (s, e) => appliedCount++;
            system.OnEffectExpired += (s, e) => expiredCount++;

            ActiveGameplayEffect active = system.ApplyEffect(shield, "caster");
            Assert.IsNotNull(active);
            Assert.AreEqual(1, appliedCount);
            Assert.AreEqual(150f, system.Attributes.GetValue("health"), Delta);
            Assert.AreEqual("caster", active.Source);

            // 未到期：修饰器仍在。
            system.Tick(1f);
            Assert.AreEqual(150f, system.Attributes.GetValue("health"), Delta);
            Assert.AreEqual(0, expiredCount);

            // 到期：修饰器移除，CurrentValue 还原，到期事件触发。
            system.Tick(2f);
            Assert.AreEqual(100f, system.Attributes.GetValue("health"), Delta);
            Assert.AreEqual(1, expiredCount);
        }

        [Test]
        public void PeriodicEffect_AppliesEachPeriod()
        {
            AbilitySystem system = MakeSystem();
            // DOT：每 1 秒 -5 health，持续 3 秒 -> 共 -15。
            GameplayEffectDefinition poison = GameplayEffectDefinition.Create("poison")
                .ForDuration(3f)
                .WithPeriod(1f)
                .AddModifier("health", ModifierOp.Flat, -5f)
                .Build();

            system.ApplyEffect(poison);
            Assert.AreEqual(100f, system.Attributes.GetValue("health"), Delta);

            system.Tick(1f);
            Assert.AreEqual(95f, system.Attributes.GetValue("health"), Delta);

            system.Tick(1f);
            Assert.AreEqual(90f, system.Attributes.GetValue("health"), Delta);

            system.Tick(1f);
            Assert.AreEqual(85f, system.Attributes.GetValue("health"), Delta);
        }

        [Test]
        public void PeriodicEffect_NoExtraTickAfterMidFrameExpiry()
        {
            AbilitySystem system = MakeSystem();
            // DOT：每 1 秒 -5，持续 1.5 秒。t=1.0 结算一次；第二帧只存活 0.5s（< 周期 1s）后到期，
            // 不应再多结算一次到期之后的周期。共 -5。
            GameplayEffectDefinition poison = GameplayEffectDefinition.Create("poison_short")
                .ForDuration(1.5f)
                .WithPeriod(1f)
                .AddModifier("health", ModifierOp.Flat, -5f)
                .Build();

            system.ApplyEffect(poison);

            system.Tick(1f);
            Assert.AreEqual(95f, system.Attributes.GetValue("health"), Delta);

            system.Tick(1f); // 本帧 0.5s 后到期，不足一个周期 -> 不再结算
            Assert.AreEqual(95f, system.Attributes.GetValue("health"), Delta);
        }

        [Test]
        public void GrantedTags_PresentWhileActive_GoneAfterExpiry()
        {
            AbilitySystem system = MakeSystem();
            GameplayEffectDefinition stun = GameplayEffectDefinition.Create("stun")
                .ForDuration(2f)
                .GrantTag("state.stunned")
                .Build();

            Assert.IsFalse(system.Tags.HasTag("state.stunned"));

            system.ApplyEffect(stun);
            Assert.IsTrue(system.Tags.HasTag("state.stunned"));
            Assert.IsTrue(system.Tags.HasTag("state")); // 父前缀命中。

            system.Tick(2f);
            Assert.IsFalse(system.Tags.HasTag("state.stunned"));
            Assert.IsFalse(system.Tags.HasTag("state"));
        }

        [Test]
        public void SharedGrantedTag_RefCounted_RemovedOnlyWhenLastEffectEnds()
        {
            AbilitySystem system = MakeSystem();

            // 两个效果授予同一标签：短持续 + 无限。
            GameplayEffectDefinition shortStun = GameplayEffectDefinition.Create("short_stun")
                .ForDuration(2f)
                .GrantTag("state.stunned")
                .Build();
            GameplayEffectDefinition lockdown = GameplayEffectDefinition.Create("lockdown")
                .Infinite()
                .GrantTag("state.stunned")
                .Build();

            system.ApplyEffect(shortStun);
            ActiveGameplayEffect infinite = system.ApplyEffect(lockdown);
            Assert.IsTrue(system.Tags.HasTag("state.stunned"));

            // 短持续效果到期：标签仍被无限效果授予，必须保留。
            system.Tick(2f);
            Assert.IsTrue(system.Tags.HasTag("state.stunned"),
                "共享标签不应在仍有其它效果授予时被移除。");
            Assert.IsTrue(system.Tags.HasTag("state")); // 父前缀仍命中。

            // 最后一个授予者移除后，标签才真正消失。
            Assert.IsTrue(system.RemoveEffect(infinite));
            Assert.IsFalse(system.Tags.HasTag("state.stunned"));
            Assert.IsFalse(system.Tags.HasTag("state"));
        }

        [Test]
        public void RemoveEffect_ManuallyRemovesModifiersAndTags()
        {
            AbilitySystem system = MakeSystem();
            GameplayEffectDefinition aura = GameplayEffectDefinition.Create("aura")
                .Infinite()
                .AddModifier("mana", ModifierOp.Flat, 20f)
                .GrantTag("buff.aura")
                .Build();

            ActiveGameplayEffect active = system.ApplyEffect(aura);
            Assert.AreEqual(70f, system.Attributes.GetValue("mana"), Delta);
            Assert.IsTrue(system.Tags.HasTag("buff.aura"));

            Assert.IsTrue(system.RemoveEffect(active));
            Assert.AreEqual(50f, system.Attributes.GetValue("mana"), Delta);
            Assert.IsFalse(system.Tags.HasTag("buff.aura"));

            // 二次移除返回 false。
            Assert.IsFalse(system.RemoveEffect(active));
        }

        [Test]
        public void InfiniteEffect_DoesNotExpireOnTick()
        {
            AbilitySystem system = MakeSystem();
            GameplayEffectDefinition aura = GameplayEffectDefinition.Create("aura")
                .Infinite()
                .AddModifier("health", ModifierOp.Flat, 10f)
                .Build();

            system.ApplyEffect(aura);
            system.Tick(100f);

            Assert.AreEqual(110f, system.Attributes.GetValue("health"), Delta);
        }
    }
}
