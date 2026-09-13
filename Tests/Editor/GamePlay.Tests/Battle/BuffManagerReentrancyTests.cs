//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Battle;

namespace EjoyFramework.GamePlay.Tests
{
    /// <summary>
    /// 回归：BuffManager.Update 在 DOT/HOT 回调内被重入 ApplyBuff/RemoveBuff 时不得崩溃。
    /// 历史问题：Update 直接遍历活动 m_ByTarget，回调向其增删键会抛 InvalidOperationException。
    /// </summary>
    public class BuffManagerReentrancyTests
    {
        private sealed class CallbackEntity : IBattleEntity
        {
            public int EntityId { get; set; }
            public bool IsAlive => CurrentHp > 0;
            public int CurrentHp { get; set; } = 100;
            public int MaxHp { get; set; } = 100;
            public int Attack { get; set; }
            public int Defense { get; set; }
            public int Camp { get; set; }
            public Action OnDamaged;
            public void ApplyDamage(int amount, IBattleEntity attacker) { CurrentHp -= amount; OnDamaged?.Invoke(); }
            public void ApplyHeal(int amount, IBattleEntity healer) { CurrentHp += amount; }
        }

        [Test]
        public void Update_DotCallbackAppliesBuffToNewTarget_DoesNotThrow()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 1, Kind = BuffKind.DOT, DurationSeconds = 10f, TickIntervalSeconds = 1f, TickAmount = 5 });
            bm.RegisterBuff(new BuffDef { BuffId = 2, DurationSeconds = 5f });

            var victim = new CallbackEntity { EntityId = 1 };
            var bystander = new CallbackEntity { EntityId = 2 };

            // DOT 结算时，受害者的 ApplyDamage 回调给「新目标」上 buff —— 会向 m_ByTarget 增键。
            // 旧实现在 foreach(m_ByTarget) 中途增键 → InvalidOperationException。
            bool applied = false;
            victim.OnDamaged = () =>
            {
                if (applied) return;
                applied = true;
                bm.ApplyBuff(bystander, 2, null);
            };

            bm.ApplyBuff(victim, 1, null);

            Assert.DoesNotThrow(() => bm.Update(1.0f, 1.0f));
            Assert.IsTrue(bm.HasBuff(bystander, 2), "重入 ApplyBuff 的新目标 buff 应已生效。");
            Assert.AreEqual(95, victim.CurrentHp);
        }

        [Test]
        public void Update_DotCallbackRemovesBuffFromSameTarget_DoesNotThrow()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef { BuffId = 1, Kind = BuffKind.DOT, DurationSeconds = 10f, TickIntervalSeconds = 1f, TickAmount = 5 });
            bm.RegisterBuff(new BuffDef { BuffId = 2, DurationSeconds = 5f });

            var victim = new CallbackEntity { EntityId = 1 };
            bm.ApplyBuff(victim, 2, null); // 普通 buff，将在回调中被移除

            bool removed = false;
            victim.OnDamaged = () =>
            {
                if (removed) return;
                removed = true;
                bm.RemoveBuff(victim, 2); // 重入移除同一 target 的另一 buff
            };

            bm.ApplyBuff(victim, 1, null);

            Assert.DoesNotThrow(() => bm.Update(1.0f, 1.0f));
            Assert.IsFalse(bm.HasBuff(victim, 2));
        }

        [Test]
        public void Update_ReentrantRemovalOfExpiringBuff_FiresRemovedOnce()
        {
            var bm = new BuffManager();
            bm.RegisterBuff(new BuffDef
            {
                BuffId = 1,
                Kind = BuffKind.DOT,
                DurationSeconds = 10f,
                TickIntervalSeconds = 1f,
                TickAmount = 1
            });
            bm.RegisterBuff(new BuffDef { BuffId = 2, DurationSeconds = 0.5f });

            var victim = new CallbackEntity { EntityId = 1 };
            int removedCount = 0;
            bm.BuffRemoved += (_, inst) =>
            {
                if (inst.Def.BuffId == 2) removedCount++;
            };
            victim.OnDamaged = () => bm.RemoveBuff(victim, 2);

            bm.ApplyBuff(victim, 1, null);
            bm.ApplyBuff(victim, 2, null);
            bm.Update(1f, 1f);

            Assert.AreEqual(1, removedCount);
        }
    }
}
