//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Battle;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Battle
{
    /// <summary>
    /// 针对 <see cref="BattleEngine"/> 伤害/治疗原语、事件产出与触发器效果链的单元测试。
    /// </summary>
    [TestFixture]
    public class BattleEngineTests
    {
        private static BattleUnit MakeUnit(string id, string team, int maxHp, int atk, int def, int spd)
        {
            return new BattleUnit(id, team, new BattleStats(maxHp, atk, def, spd));
        }

        private static BattleEngine MakeEngine(out BattleState state)
        {
            state = new BattleState();
            return new BattleEngine(state);
        }

        // ---- 伤害减免与生命值钳制 ----

        [Test]
        public void DealDamage_AppliesMitigation_MaxOneFloor()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit attacker = MakeUnit("atk", "A", 100, 20, 0, 10);
            BattleUnit target = MakeUnit("tgt", "B", 100, 0, 8, 5); // 防御 8
            state.AddUnit(attacker);
            state.AddUnit(target);

            // 原始 20 - 防御 8 = 12。
            engine.DealDamage("atk", "tgt", 20);
            Assert.AreEqual(88, target.Stats.Hp);

            // 原始 3 - 防御 8 = -5 → 钳制到下限 1。
            engine.DealDamage("atk", "tgt", 3);
            Assert.AreEqual(87, target.Stats.Hp);
        }

        [Test]
        public void DealDamage_LethalClampsHpAtZero_EmitsUnitDied()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("atk", "A", 100, 0, 0, 10));
            BattleUnit target = MakeUnit("tgt", "B", 10, 0, 0, 5);
            state.AddUnit(target);

            List<BattleEvent> events = new List<BattleEvent>();
            engine.OnEvent += (e, evt) => events.Add(evt);

            engine.DealDamage("atk", "tgt", 999);

            Assert.AreEqual(0, target.Stats.Hp);
            Assert.IsFalse(target.Stats.IsAlive);

            // 顺序：DamageDealt, DamageTaken, UnitDied。
            Assert.AreEqual(3, events.Count);
            Assert.AreEqual(BattleEventType.DamageDealt, events[0].Type);
            Assert.AreEqual(BattleEventType.DamageTaken, events[1].Type);
            Assert.AreEqual(BattleEventType.UnitDied, events[2].Type);
            Assert.AreEqual("tgt", events[2].TargetId);
        }

        [Test]
        public void DealDamage_OnDeadTarget_IsNoOp()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit target = MakeUnit("tgt", "B", 10, 0, 0, 5);
            state.AddUnit(target);
            target.Stats.Hp = 0; // 预先阵亡

            int count = 0;
            engine.OnEvent += (e, evt) => count++;

            engine.DealDamage(null, "tgt", 50);

            Assert.AreEqual(0, count, "对已阵亡目标造成伤害应为无操作。");
            Assert.AreEqual(0, target.Stats.Hp);
        }

        [Test]
        public void DealDamage_NonPositiveRaw_IsNoOp()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit target = MakeUnit("tgt", "B", 10, 0, 0, 5);
            state.AddUnit(target);

            engine.DealDamage(null, "tgt", 0);
            engine.DealDamage(null, "tgt", -5);

            Assert.AreEqual(10, target.Stats.Hp);
        }

        // ---- 治疗 ----

        [Test]
        public void Heal_ClampsAtMaxHp_EmitsActualAmount()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit target = MakeUnit("tgt", "B", 100, 0, 0, 5);
            state.AddUnit(target);
            target.Stats.Hp = 90;

            List<BattleEvent> events = new List<BattleEvent>();
            engine.OnEvent += (e, evt) => events.Add(evt);

            // 治疗 50，但只缺 10 → 实际恢复 10。
            engine.Heal("healer", "tgt", 50);

            Assert.AreEqual(100, target.Stats.Hp);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(BattleEventType.Healed, events[0].Type);
            Assert.AreEqual(10, events[0].Amount);
        }

        [Test]
        public void Heal_FullHp_NoEvent()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit target = MakeUnit("tgt", "B", 100, 0, 0, 5);
            state.AddUnit(target);

            int count = 0;
            engine.OnEvent += (e, evt) => count++;

            engine.Heal("healer", "tgt", 30);

            Assert.AreEqual(100, target.Stats.Hp);
            Assert.AreEqual(0, count, "满血治疗无实际恢复，不应产出事件。");
        }

        [Test]
        public void Heal_DeadTarget_DoesNotResurrect()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit target = MakeUnit("tgt", "B", 100, 0, 0, 5);
            state.AddUnit(target);
            target.Stats.Hp = 0;

            engine.Heal("healer", "tgt", 50);

            Assert.AreEqual(0, target.Stats.Hp);
            Assert.IsFalse(target.Stats.IsAlive);
        }

        // ---- PerformAttack ----

        [Test]
        public void PerformAttack_UsesAttack_ReturnsEvents()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("atk", "A", 100, 25, 0, 10));
            BattleUnit target = MakeUnit("tgt", "B", 100, 0, 5, 5);
            state.AddUnit(target);

            IReadOnlyList<BattleEvent> events = engine.PerformAttack("atk", "tgt");

            // 25 - 5 = 20。
            Assert.AreEqual(80, target.Stats.Hp);
            Assert.AreEqual(2, events.Count); // DamageDealt + DamageTaken（未致死）
            Assert.AreEqual(BattleEventType.DamageDealt, events[0].Type);
            Assert.AreEqual(20, events[0].Amount);
            Assert.AreEqual(BattleEventType.DamageTaken, events[1].Type);
            Assert.AreEqual("atk", events[0].SourceId);
            Assert.AreEqual("tgt", events[0].TargetId);
        }

        [Test]
        public void PerformAttack_MissingSource_ReturnsEmpty()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("tgt", "B", 100, 0, 0, 5));

            IReadOnlyList<BattleEvent> events = engine.PerformAttack("ghost", "tgt");
            Assert.AreEqual(0, events.Count);
        }

        [Test]
        public void PerformAttack_ReentrantFromTrigger_TopLevelFullChain_InnerReturnsEmpty()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit hero = MakeUnit("hero", "A", 100, 20, 0, 10);
            BattleUnit boss = MakeUnit("boss", "B", 100, 30, 0, 8);
            state.AddUnit(hero);
            state.AddUnit(boss);

            IReadOnlyList<BattleEvent> innerResult = null;
            bool countered = false;
            // boss 受击时反击 hero：触发器在效果链处理中嵌套调用 PerformAttack。
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageTaken, "boss", (e, evt) =>
                {
                    if (countered) return;
                    countered = true;
                    innerResult = e.PerformAttack("boss", "hero");
                }));

            IReadOnlyList<BattleEvent> top = engine.PerformAttack("hero", "boss");

            // 处理正确性：反击链已并入顶层结果，数值与事件数都正确。
            Assert.AreEqual(80, boss.Stats.Hp);  // 100 - 20
            Assert.AreEqual(70, hero.Stats.Hp);  // 100 - 30（反击）
            Assert.AreEqual(4, top.Count);       // hero->boss(DD+DT) + boss->hero(DD+DT)

            // 契约修复：链内嵌套 PerformAttack 返回空表，而非顶层调用此刻的半截快照。
            Assert.IsNotNull(innerResult);
            Assert.AreEqual(0, innerResult.Count);
        }

        // ---- 触发器效果链 ----

        [Test]
        public void Trigger_OnUnitDied_RevengeDamage_Chains()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            BattleUnit hero = MakeUnit("hero", "A", 100, 50, 0, 10);
            BattleUnit victim = MakeUnit("victim", "B", 10, 0, 0, 5);
            BattleUnit avenger = MakeUnit("avenger", "A", 100, 0, 0, 8);
            state.AddUnit(hero);
            state.AddUnit(victim);
            state.AddUnit(avenger);

            // 复仇：当 victim 阵亡时，对 hero 造成 30 伤害。归属为 victim（UnitDied 按 TargetId）。
            BattleTrigger revenge = new BattleTrigger(
                BattleEventType.UnitDied,
                "victim",
                (e, evt) => e.DealDamage("victim", "hero", 30));
            engine.RegisterTrigger(revenge);

            List<BattleEvent> events = new List<BattleEvent>();
            engine.OnEvent += (e, evt) => events.Add(evt);

            // hero 攻击 victim 致死（50 伤害 > 10 HP）。
            engine.PerformAttack("hero", "victim");

            // 复仇伤害已对 hero 生效：30 - 0 = 30。
            Assert.AreEqual(70, hero.Stats.Hp);

            // 事件按 FIFO 顺序：先 victim 的三连，再链上 hero 的两连。
            Assert.AreEqual(BattleEventType.DamageDealt, events[0].Type); // hero -> victim
            Assert.AreEqual(BattleEventType.DamageTaken, events[1].Type);
            Assert.AreEqual(BattleEventType.UnitDied, events[2].Type);    // victim died
            Assert.AreEqual("victim", events[2].TargetId);
            Assert.AreEqual(BattleEventType.DamageDealt, events[3].Type); // victim -> hero (revenge)
            Assert.AreEqual("hero", events[3].TargetId);
            Assert.AreEqual(BattleEventType.DamageTaken, events[4].Type);
            Assert.AreEqual(5, events.Count);
        }

        [Test]
        public void Trigger_GlobalOwnerNull_FiresForAnyUnit()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("atk", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("x", "B", 100, 0, 0, 5));
            state.AddUnit(MakeUnit("y", "B", 100, 0, 0, 5));

            int fired = 0;
            // 全局：任意单位受伤都计数。
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageTaken, null, (e, evt) => fired++));

            engine.DealDamage("atk", "x", 10);
            engine.DealDamage("atk", "y", 10);

            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Trigger_OwnerScoped_OnlyFiresForOwner()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("atk", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("x", "B", 100, 0, 0, 5));
            state.AddUnit(MakeUnit("y", "B", 100, 0, 0, 5));

            int firedForX = 0;
            // 仅当 x 受伤时触发（DamageTaken 按 TargetId 归属）。
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageTaken, "x", (e, evt) => firedForX++));

            engine.DealDamage("atk", "y", 10); // 不应触发
            Assert.AreEqual(0, firedForX);

            engine.DealDamage("atk", "x", 10); // 应触发
            Assert.AreEqual(1, firedForX);
        }

        [Test]
        public void Trigger_DamageDealt_ScopedBySourceId()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("a", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("b", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("t", "B", 100, 0, 0, 5));

            int firedForA = 0;
            // DamageDealt 按攻击方（SourceId）归属：仅 a 造成伤害时触发。
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageDealt, "a", (e, evt) => firedForA++));

            engine.DealDamage("b", "t", 10); // 攻击方 b，不触发
            Assert.AreEqual(0, firedForA);

            engine.DealDamage("a", "t", 10); // 攻击方 a，触发
            Assert.AreEqual(1, firedForA);
        }

        [Test]
        public void RemoveTrigger_StopsFiring()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("atk", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("x", "B", 100, 0, 0, 5));

            int fired = 0;
            BattleTrigger trigger = new BattleTrigger(
                BattleEventType.DamageTaken, null, (e, evt) => fired++);
            engine.RegisterTrigger(trigger);

            engine.DealDamage("atk", "x", 10);
            Assert.AreEqual(1, fired);

            Assert.IsTrue(engine.RemoveTrigger(trigger));
            engine.DealDamage("atk", "x", 10);
            Assert.AreEqual(1, fired, "移除后不应再触发。");

            Assert.IsFalse(engine.RemoveTrigger(trigger), "重复移除返回 false。");
        }

        [Test]
        public void DepthCap_TwoMutualTriggers_TerminateSafely()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            // 两个高血量、零防御单位，互相在对方受伤时反打对方，形成潜在无限链。
            BattleUnit p = MakeUnit("p", "A", 1000000, 0, 0, 10);
            BattleUnit q = MakeUnit("q", "B", 1000000, 0, 0, 5);
            state.AddUnit(p);
            state.AddUnit(q);

            // p 受伤 → 打 q 1 点；q 受伤 → 打 p 1 点。互相激发。
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageTaken, "p", (e, evt) => e.DealDamage("p", "q", 1)));
            engine.RegisterTrigger(new BattleTrigger(
                BattleEventType.DamageTaken, "q", (e, evt) => e.DealDamage("q", "p", 1)));

            int processed = 0;
            engine.OnEvent += (e, evt) => processed++;

            // 顶层触发一次：不会无限循环，应在深度上限处安全终止。
            Assert.DoesNotThrow(() => engine.DealDamage(null, "p", 1));

            // 处理的事件数不超过上限。
            Assert.LessOrEqual(processed, BattleEngine.MaxEventsPerCall);
            Assert.Greater(processed, 0);

            // 两个单位都还活着（血量远大于上限内能造成的累计伤害）。
            Assert.IsTrue(p.Stats.IsAlive);
            Assert.IsTrue(q.Stats.IsAlive);

            // 上限计数为“每次顶层调用”：再次顶层调用应可继续正常处理而不抛异常。
            Assert.DoesNotThrow(() => engine.Heal(null, "p", 1));
        }

        [Test]
        public void RunTurn_EmitsTurnStartedAndEnded_AroundAction()
        {
            BattleState state;
            BattleEngine engine = MakeEngine(out state);
            state.AddUnit(MakeUnit("hero", "A", 100, 10, 0, 10));
            state.AddUnit(MakeUnit("foe", "B", 100, 0, 0, 5));

            List<BattleEventType> order = new List<BattleEventType>();
            engine.OnEvent += (e, evt) => order.Add(evt.Type);

            engine.RunTurn("hero", e => e.PerformAttack("hero", "foe"));

            // 期望：TurnStarted, (攻击链), TurnEnded。
            Assert.AreEqual(BattleEventType.TurnStarted, order[0]);
            Assert.AreEqual(BattleEventType.TurnEnded, order[order.Count - 1]);
            Assert.Contains(BattleEventType.DamageDealt, order);
            Assert.Contains(BattleEventType.DamageTaken, order);
        }
    }
}
