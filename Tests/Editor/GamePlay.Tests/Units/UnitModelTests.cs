//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Abilities;
using EjoyFramework.GamePlay.Attributes;
using EjoyFramework.GamePlay.Units;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Units
{
    /// <summary>
    /// 针对 <see cref="UnitModel"/> 的单元测试：覆盖可选生命语义、伤害/治疗/击杀、
    /// 事件触发次数、最大生命缩放、属性/技能组合，以及对象池重置。
    /// </summary>
    [TestFixture]
    public class UnitModelTests
    {
        private const float Delta = 1e-4f;

        // ---------------------------------------------------------------
        // 可选生命：未配置生命即不可摧毁
        // ---------------------------------------------------------------

        [Test]
        public void Fresh_Unit_IsIndestructibleWithZeroCombatState()
        {
            UnitModel unit = new UnitModel(1, 7);

            Assert.IsFalse(unit.IsDamageable);
            Assert.IsTrue(unit.IsAlive);
            Assert.IsTrue(unit.IsTargetable);
            Assert.AreEqual(7, unit.FactionId);
            Assert.AreEqual(UnitModel.IndestructibleHealthSentinel, unit.Health, Delta);
            Assert.AreEqual(0f, unit.MaxHealth, Delta);
            Assert.IsNull(unit.Attributes);
            Assert.IsNull(unit.Behavior);
        }

        [Test]
        public void Fresh_Unit_TakeDamage_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            int damagedCount = 0;
            int diedCount = 0;
            unit.OnDamaged += (u, amount, src) => damagedCount++;
            unit.OnDied += (u, src) => diedCount++;

            DamageResult result = unit.TakeDamage(50f);

            Assert.AreEqual(0f, result.Applied, Delta);
            Assert.IsFalse(result.WasLethal);
            Assert.IsTrue(unit.IsAlive);
            Assert.AreEqual(0, damagedCount);
            Assert.AreEqual(0, diedCount);
        }

        [Test]
        public void Fresh_Unit_Kill_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            int diedCount = 0;
            unit.OnDied += (u, src) => diedCount++;

            unit.Kill();

            Assert.IsTrue(unit.IsAlive);
            Assert.AreEqual(0, diedCount);
        }

        // ---------------------------------------------------------------
        // ConfigureHealth
        // ---------------------------------------------------------------

        [Test]
        public void ConfigureHealth_FullByDefault()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);

            Assert.IsTrue(unit.IsDamageable);
            Assert.IsTrue(unit.IsAlive);
            Assert.AreEqual(100f, unit.Health, Delta);
            Assert.AreEqual(100f, unit.MaxHealth, Delta);
        }

        [Test]
        public void ConfigureHealth_WithCurrent_StartsAtThatValue()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 30f);

            Assert.AreEqual(30f, unit.Health, Delta);
            Assert.AreEqual(100f, unit.MaxHealth, Delta);
            Assert.IsTrue(unit.IsAlive);
        }

        [Test]
        public void ConfigureHealth_CurrentClampedToMax()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 250f);

            Assert.AreEqual(100f, unit.Health, Delta);
        }

        // ---------------------------------------------------------------
        // TakeDamage
        // ---------------------------------------------------------------

        [Test]
        public void TakeDamage_ReducesHealthAndFiresOnDamaged()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);

            float reportedApplied = -1f;
            int reportedSource = 0;
            unit.OnDamaged += (u, amount, src) =>
            {
                reportedApplied = amount;
                reportedSource = src;
            };

            DamageResult result = unit.TakeDamage(40f, 99);

            Assert.AreEqual(40f, result.Applied, Delta);
            Assert.IsFalse(result.WasLethal);
            Assert.AreEqual(60f, unit.Health, Delta);
            Assert.AreEqual(40f, reportedApplied, Delta);
            Assert.AreEqual(99, reportedSource);
        }

        [Test]
        public void TakeDamage_Overkill_ClampsAppliedAndMarksLethal()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 30f);

            int diedCount = 0;
            unit.OnDied += (u, src) => diedCount++;

            DamageResult result = unit.TakeDamage(1000f);

            Assert.AreEqual(30f, result.Applied, Delta);
            Assert.IsTrue(result.WasLethal);
            Assert.AreEqual(0f, unit.Health, Delta);
            Assert.IsFalse(unit.IsAlive);
            Assert.AreEqual(1, diedCount);
        }

        [Test]
        public void TakeDamage_OnDied_FiresExactlyOnce()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(50f);

            int diedCount = 0;
            unit.OnDied += (u, src) => diedCount++;

            unit.TakeDamage(50f);
            Assert.AreEqual(1, diedCount);

            // 对已死亡单位继续施加伤害：返回 0，不再触发死亡。
            DamageResult again = unit.TakeDamage(10f);
            Assert.AreEqual(0f, again.Applied, Delta);
            Assert.IsFalse(again.WasLethal);
            Assert.AreEqual(1, diedCount);
        }

        [Test]
        public void TakeDamage_NonPositive_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);
            int damagedCount = 0;
            unit.OnDamaged += (u, amount, src) => damagedCount++;

            Assert.AreEqual(0f, unit.TakeDamage(0f).Applied, Delta);
            Assert.AreEqual(0f, unit.TakeDamage(-5f).Applied, Delta);
            Assert.AreEqual(100f, unit.Health, Delta);
            Assert.AreEqual(0, damagedCount);
        }

        // ---------------------------------------------------------------
        // Heal
        // ---------------------------------------------------------------

        [Test]
        public void Heal_ClampsToMaxAndFiresOnHealed()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 40f);

            float reportedHealed = -1f;
            unit.OnHealed += (u, amount) => reportedHealed = amount;

            unit.Heal(1000f);

            Assert.AreEqual(100f, unit.Health, Delta);
            // 实际回复量应是 100 - 40 = 60，而非传入的 1000。
            Assert.AreEqual(60f, reportedHealed, Delta);
        }

        [Test]
        public void Heal_OnDead_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);
            unit.Kill();

            int healedCount = 0;
            unit.OnHealed += (u, amount) => healedCount++;

            unit.Heal(50f);

            Assert.AreEqual(0f, unit.Health, Delta);
            Assert.IsFalse(unit.IsAlive);
            Assert.AreEqual(0, healedCount);
        }

        // ---------------------------------------------------------------
        // Kill
        // ---------------------------------------------------------------

        [Test]
        public void Kill_FiresOnDiedOnce_ThenNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);

            int diedCount = 0;
            int lastSource = 0;
            unit.OnDied += (u, src) =>
            {
                diedCount++;
                lastSource = src;
            };

            unit.Kill(42);
            Assert.AreEqual(1, diedCount);
            Assert.AreEqual(42, lastSource);
            Assert.AreEqual(0f, unit.Health, Delta);
            Assert.IsFalse(unit.IsAlive);

            unit.Kill();
            Assert.AreEqual(1, diedCount);
        }

        [Test]
        public void Kill_AfterLethalDamage_DoesNotRefireOnDied()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(20f);

            int diedCount = 0;
            unit.OnDied += (u, src) => diedCount++;

            unit.TakeDamage(20f);
            unit.Kill();

            Assert.AreEqual(1, diedCount);
        }

        // ---------------------------------------------------------------
        // SetMaxHealth
        // ---------------------------------------------------------------

        [Test]
        public void SetMaxHealth_KeepRatioTrue_PreservesFraction()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 50f); // 50% 生命

            unit.SetMaxHealth(200f, keepRatio: true);

            Assert.AreEqual(200f, unit.MaxHealth, Delta);
            Assert.AreEqual(100f, unit.Health, Delta); // 仍为 50%
        }

        [Test]
        public void SetMaxHealth_KeepRatioFalse_KeepsCurrentClamped()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 80f);

            // 提高上限：当前生命数值保持不变。
            unit.SetMaxHealth(200f, keepRatio: false);
            Assert.AreEqual(200f, unit.MaxHealth, Delta);
            Assert.AreEqual(80f, unit.Health, Delta);

            // 降低上限到低于当前生命：钳制到新上限。
            unit.SetMaxHealth(50f, keepRatio: false);
            Assert.AreEqual(50f, unit.MaxHealth, Delta);
            Assert.AreEqual(50f, unit.Health, Delta);
        }

        [Test]
        public void SetMaxHealth_WhenNotDamageable_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.SetMaxHealth(500f);

            Assert.IsFalse(unit.IsDamageable);
            Assert.AreEqual(0f, unit.MaxHealth, Delta);
        }

        // ---------------------------------------------------------------
        // 属性组合 + SyncMaxHealthFromAttribute
        // ---------------------------------------------------------------

        [Test]
        public void SyncMaxHealthFromAttribute_DrivesMaxHealth()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f, 50f); // 50%

            AttributeSet attributes = new AttributeSet();
            attributes.Add("maxHealth", 300f);
            unit.AttachAttributes(attributes);

            Assert.AreSame(attributes, unit.Attributes);

            unit.SyncMaxHealthFromAttribute(); // 默认 "maxHealth"，保持占比

            Assert.AreEqual(300f, unit.MaxHealth, Delta);
            Assert.AreEqual(150f, unit.Health, Delta); // 仍为 50%
        }

        [Test]
        public void SyncMaxHealthFromAttribute_MissingAttribute_IsNoOp()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(100f);
            unit.AttachAttributes(new AttributeSet());

            unit.SyncMaxHealthFromAttribute(); // 集合中没有 "maxHealth"

            Assert.AreEqual(100f, unit.MaxHealth, Delta);
        }

        // ---------------------------------------------------------------
        // 技能组合 + Tick
        // ---------------------------------------------------------------

        [Test]
        public void Tick_WithoutAbilities_DoesNotThrow()
        {
            UnitModel unit = new UnitModel(1, 0);
            Assert.DoesNotThrow(() => unit.Tick(0.016f));
        }

        [Test]
        public void Tick_WithAbilities_AdvancesCooldown()
        {
            UnitModel unit = new UnitModel(1, 0);

            AttributeSet attributes = new AttributeSet();
            attributes.Add("mana", 100f);
            unit.AttachAttributes(attributes);

            AbilitySystem abilities = new AbilitySystem(unit.Attributes);
            unit.AttachBehavior(abilities);
            Assert.AreSame(abilities, unit.Behavior);

            // 授予一个带 2 秒冷却的技能并激活，使其进入冷却。
            AbilityDefinition ability = new AbilityDefinition("dash", cooldown: 2f);
            abilities.GrantAbility(ability);
            AbilityActivationResult activation = abilities.TryActivate("dash");
            Assert.AreEqual(AbilityActivationResult.Success, activation);
            Assert.AreEqual(2f, abilities.GetCooldownRemaining("dash"), Delta);

            // 通过单位 Tick 推进时间，冷却应递减。
            unit.Tick(0.5f);
            Assert.AreEqual(1.5f, abilities.GetCooldownRemaining("dash"), Delta);

            unit.Tick(1.5f);
            Assert.AreEqual(0f, abilities.GetCooldownRemaining("dash"), Delta);
            Assert.IsFalse(abilities.IsOnCooldown("dash"));
        }

        // ---------------------------------------------------------------
        // ITargetableUnit 契约
        // ---------------------------------------------------------------

        [Test]
        public void ImplementsITargetableUnit_ReflectsState()
        {
            UnitModel unit = new UnitModel(123, 5);
            unit.ConfigureHealth(80f, 60f);
            unit.SetPosition(3.5f, -2.25f);
            unit.IsTargetable = false;

            ITargetableUnit target = unit;

            Assert.AreEqual(123, target.Id);
            Assert.AreEqual(5, target.FactionId);
            Assert.AreEqual(3.5f, target.PositionX, Delta);
            Assert.AreEqual(-2.25f, target.PositionY, Delta);
            Assert.AreEqual(60f, target.Health, Delta);
            Assert.AreEqual(80f, target.MaxHealth, Delta);
            Assert.IsTrue(target.IsAlive);
            Assert.IsFalse(target.IsTargetable);
        }

        [Test]
        public void ITargetableUnit_IndestructibleUnit_IsAlwaysAlive()
        {
            ITargetableUnit target = new UnitModel(1, 0);

            Assert.IsTrue(target.IsAlive);
            Assert.AreEqual(UnitModel.IndestructibleHealthSentinel, target.Health, Delta);
            Assert.AreEqual(0f, target.MaxHealth, Delta);
        }

        // ---------------------------------------------------------------
        // Reset（对象池复用）
        // ---------------------------------------------------------------

        [Test]
        public void Reset_ReturnsToFreshIndestructibleState_KeepsId()
        {
            UnitModel unit = new UnitModel(77, 1);
            unit.ConfigureHealth(100f, 40f);
            unit.SetPosition(10f, 20f);
            unit.Progress = 5f;
            unit.Threat = 3f;
            unit.Priority = 9;
            unit.IsTargetable = false;
            unit.AttachAttributes(new AttributeSet());
            unit.AttachBehavior(new AbilitySystem(unit.Attributes));

            unit.Reset(2);

            Assert.AreEqual(77, unit.Id);          // Id 不变
            Assert.AreEqual(2, unit.FactionId);    // 应用新阵营
            Assert.IsFalse(unit.IsDamageable);
            Assert.IsTrue(unit.IsAlive);
            Assert.AreEqual(UnitModel.IndestructibleHealthSentinel, unit.Health, Delta);
            Assert.AreEqual(0f, unit.MaxHealth, Delta);
            Assert.AreEqual(0f, unit.PositionX, Delta);
            Assert.AreEqual(0f, unit.PositionY, Delta);
            Assert.AreEqual(0f, unit.Progress, Delta);
            Assert.AreEqual(0f, unit.Threat, Delta);
            Assert.AreEqual(0, unit.Priority);
            Assert.IsTrue(unit.IsTargetable);
            Assert.IsNull(unit.Attributes);
            Assert.IsNull(unit.Behavior);
        }

        [Test]
        public void Reset_AllowsReconfigureAndRedeath()
        {
            UnitModel unit = new UnitModel(1, 0);
            unit.ConfigureHealth(10f);
            unit.Kill();
            Assert.IsFalse(unit.IsAlive);

            unit.Reset(0);
            unit.ConfigureHealth(10f);

            int diedCount = 0;
            unit.OnDied += (u, src) => diedCount++;
            unit.Kill();

            // 重置后是“新的一条命”，死亡事件应能再次触发。
            Assert.AreEqual(1, diedCount);
            Assert.IsFalse(unit.IsAlive);
        }
    }
}
