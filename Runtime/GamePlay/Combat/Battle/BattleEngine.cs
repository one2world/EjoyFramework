//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 回合制战斗引擎：在 <see cref="BattleState"/> 之上提供伤害/治疗原语，
    /// 并以 FIFO 事件队列驱动触发器（<see cref="BattleTrigger"/>）效果链。
    /// </summary>
    /// <remarks>
    /// 事件链语义：
    /// <list type="number">
    /// <item><description>每个原语在执行数值变更后，把产生的事件入队（FIFO）。</description></item>
    /// <item><description>引擎按入队顺序逐个出队处理；每个事件先广播 <see cref="OnEvent"/>，再调用所有匹配的触发器。</description></item>
    /// <item><description>触发器处理函数内部调用 <see cref="DealDamage"/> / <see cref="Heal"/> 会向同一队列追加新事件，从而形成连锁。</description></item>
    /// <item><description>队列处理直至清空，或达到单次顶层调用的事件处理上限 <see cref="MaxEventsPerCall"/>（默认 1000）。
    /// 达到上限即安全终止，防止两个互相伤害对方归属者的触发器造成无限循环。</description></item>
    /// </list>
    /// 触发器快照：处理某一事件时先复制当前触发器列表再遍历，因此处理函数在链中注册/移除触发器不会破坏正在进行的遍历，
    /// 也避免 Mono/IL2CPP 在遍历期间修改集合抛出异常。新注册的触发器对“后续出队的事件”生效。
    /// 单线程使用，非线程安全。
    /// </remarks>
    public sealed class BattleEngine
    {
        /// <summary>
        /// 单次顶层调用允许处理的最大事件数（深度上限），防止触发器无限连锁。
        /// </summary>
        public const int MaxEventsPerCall = 1000;

        private readonly BattleState m_State;
        private readonly List<BattleTrigger> m_Triggers = new List<BattleTrigger>();
        private readonly Queue<BattleEvent> m_Queue = new Queue<BattleEvent>();

        // 复用的触发器遍历快照缓冲，减少每事件分配。
        private readonly List<BattleTrigger> m_TriggerSnapshot = new List<BattleTrigger>();

        // 当前顶层调用收集到的全部事件（按处理顺序），供 PerformAttack 等返回。
        private readonly List<BattleEvent> m_EmittedThisCall = new List<BattleEvent>();

        // 是否正在处理队列：用于区分“顶层原语调用”与“链内嵌套调用”。
        private bool m_Processing;

        /// <summary>
        /// 构造引擎。
        /// </summary>
        /// <param name="state">战斗局面，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="state"/> 为空时抛出。</exception>
        public BattleEngine(BattleState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            m_State = state;
        }

        /// <summary>
        /// 关联的战斗局面。
        /// </summary>
        public BattleState State
        {
            get { return m_State; }
        }

        /// <summary>
        /// 每个事件被处理时广播（先于触发器调用）。可用于日志、表现层驱动等。
        /// </summary>
        public event Action<BattleEngine, BattleEvent> OnEvent;

        /// <summary>
        /// 注册触发器。
        /// </summary>
        /// <param name="trigger">触发器，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="trigger"/> 为空时抛出。</exception>
        public void RegisterTrigger(BattleTrigger trigger)
        {
            if (trigger == null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            m_Triggers.Add(trigger);
        }

        /// <summary>
        /// 移除触发器（按引用相等）。
        /// </summary>
        /// <param name="trigger">触发器。</param>
        /// <returns>移除成功返回 true；不存在返回 false。</returns>
        public bool RemoveTrigger(BattleTrigger trigger)
        {
            if (trigger == null)
            {
                return false;
            }

            return m_Triggers.Remove(trigger);
        }

        /// <summary>
        /// 造成伤害：减免后伤害 = max(1, <paramref name="rawAmount"/> - 目标防御)；从目标当前生命值中扣除。
        /// 产出 <see cref="BattleEventType.DamageDealt"/> 与 <see cref="BattleEventType.DamageTaken"/>；若目标因此阵亡再产出 <see cref="BattleEventType.UnitDied"/>。
        /// 对不存在的来源/目标，或已阵亡（生命值为 0）的目标不做任何处理（无操作，不产生事件）。
        /// </summary>
        /// <param name="sourceId">来源单位标识（可为空，例如环境伤害）。</param>
        /// <param name="targetId">目标单位标识。</param>
        /// <param name="rawAmount">原始伤害（减免前）。非正值不造成伤害（无操作）。</param>
        public void DealDamage(string sourceId, string targetId, int rawAmount)
        {
            BattleUnit target = m_State.GetUnit(targetId);
            if (target == null || !target.Stats.IsAlive)
            {
                // 目标不存在或已阵亡：跳过，避免对死亡单位重复结算。
                return;
            }

            if (rawAmount <= 0)
            {
                return;
            }

            int mitigated = rawAmount - target.Stats.Defense;
            if (mitigated < 1)
            {
                mitigated = 1;
            }

            int hpBefore = target.Stats.Hp;
            target.Stats.Hp = hpBefore - mitigated;
            bool died = target.Stats.Hp == 0 && hpBefore > 0;

            Enqueue(new BattleEvent(BattleEventType.DamageDealt, sourceId, targetId, mitigated));
            Enqueue(new BattleEvent(BattleEventType.DamageTaken, sourceId, targetId, mitigated));
            if (died)
            {
                Enqueue(new BattleEvent(BattleEventType.UnitDied, sourceId, targetId, mitigated));
            }

            DrainIfTopLevel();
        }

        /// <summary>
        /// 治疗：把目标生命值提升至多 <paramref name="amount"/>，并钳制到最大生命值。产出 <see cref="BattleEventType.Healed"/>，
        /// <see cref="BattleEvent.Amount"/> 为实际恢复量。对不存在或已阵亡的目标不做处理（治疗不能复活）。
        /// </summary>
        /// <param name="sourceId">治疗来源标识（可为空）。</param>
        /// <param name="targetId">被治疗单位标识。</param>
        /// <param name="amount">治疗量。非正值无操作。</param>
        public void Heal(string sourceId, string targetId, int amount)
        {
            BattleUnit target = m_State.GetUnit(targetId);
            if (target == null || !target.Stats.IsAlive)
            {
                // 治疗不复活已阵亡单位。
                return;
            }

            if (amount <= 0)
            {
                return;
            }

            int hpBefore = target.Stats.Hp;
            target.Stats.Hp = hpBefore + amount; // setter 自动钳制到 MaxHp
            int healed = target.Stats.Hp - hpBefore;
            if (healed <= 0)
            {
                // 已满血：无实际恢复，不产出事件。
                return;
            }

            Enqueue(new BattleEvent(BattleEventType.Healed, sourceId, targetId, healed));
            DrainIfTopLevel();
        }

        /// <summary>
        /// 便捷接口：以来源单位的 <see cref="BattleStats.Attack"/> 作为原始伤害对目标发起一次普通攻击。
        /// </summary>
        /// <param name="sourceId">攻击方标识。</param>
        /// <param name="targetId">目标标识。</param>
        /// <returns>本次攻击（含其引发的整条效果链）按处理顺序产出的全部事件的只读列表。来源不存在时返回空列表。</returns>
        public IReadOnlyList<BattleEvent> PerformAttack(string sourceId, string targetId)
        {
            BattleUnit source = m_State.GetUnit(sourceId);
            if (source == null)
            {
                return Array.Empty<BattleEvent>();
            }

            // 链内嵌套调用（触发器在效果链处理过程中再次发起攻击）：此时 m_Processing 为真，DealDamage
            // 只入队、由外层顶层调用统一排空，其事件会并入顶层返回结果。此处若返回 m_EmittedThisCall 快照，
            // 实为「顶层调用此刻的半截结果」，对嵌套调用方是误导；故返回空表（嵌套攻击仍正常生效）。
            if (m_Processing)
            {
                DealDamage(sourceId, targetId, source.Stats.Attack);
                return Array.Empty<BattleEvent>();
            }

            // 顶层调用：DealDamage 排空整条效果链；其间收集的事件即本次攻击（含连锁）的完整结果。
            DealDamage(sourceId, targetId, source.Stats.Attack);
            return new List<BattleEvent>(m_EmittedThisCall);
        }

        /// <summary>
        /// 推进一个单位的回合：先产出 <see cref="BattleEventType.TurnStarted"/>，
        /// 执行 <paramref name="action"/>（如有），再产出 <see cref="BattleEventType.TurnEnded"/>。
        /// 这是一个顶层调用，内部产生的所有事件链会在返回前处理完毕。
        /// </summary>
        /// <param name="unitId">行动单位标识。</param>
        /// <param name="action">回合内要执行的动作（可为空），在 TurnStarted 之后、TurnEnded 之前调用。</param>
        public void RunTurn(string unitId, Action<BattleEngine> action)
        {
            EmitTopLevel(new BattleEvent(BattleEventType.TurnStarted, unitId, unitId, 0));
            if (action != null)
            {
                action(this);
            }

            EmitTopLevel(new BattleEvent(BattleEventType.TurnEnded, unitId, unitId, 0));
        }

        /// <summary>
        /// 以顶层调用方式发出单个事件并处理其引发的效果链（供回合等无数值原语使用）。
        /// </summary>
        private void EmitTopLevel(in BattleEvent evt)
        {
            Enqueue(evt);
            DrainIfTopLevel();
        }

        private void Enqueue(in BattleEvent evt)
        {
            m_Queue.Enqueue(evt);
        }

        /// <summary>
        /// 若当前不在处理中（即本次为顶层调用），则开始排空队列；否则仅入队，由外层循环继续处理。
        /// </summary>
        private void DrainIfTopLevel()
        {
            if (m_Processing)
            {
                return;
            }

            m_Processing = true;
            m_EmittedThisCall.Clear();
            try
            {
                int processed = 0;
                while (m_Queue.Count > 0)
                {
                    if (processed >= MaxEventsPerCall)
                    {
                        // 达到深度上限：丢弃剩余队列并安全终止，防止无限连锁。
                        m_Queue.Clear();
                        break;
                    }

                    BattleEvent evt = m_Queue.Dequeue();
                    processed++;
                    m_EmittedThisCall.Add(evt);
                    Dispatch(in evt);
                }
            }
            finally
            {
                m_Processing = false;
                // 防御性清理：极端情况下（如处理函数抛异常）不残留半截队列影响下次调用。
                m_Queue.Clear();
            }
        }

        /// <summary>
        /// 广播事件并调用全部匹配触发器（遍历触发器快照）。
        /// </summary>
        private void Dispatch(in BattleEvent evt)
        {
            Action<BattleEngine, BattleEvent> onEvent = OnEvent;
            if (onEvent != null)
            {
                onEvent(this, evt);
            }

            // 快照：处理函数可能在链中注册/移除触发器，遍历副本以保证安全与确定性。
            m_TriggerSnapshot.Clear();
            m_TriggerSnapshot.AddRange(m_Triggers);

            for (int i = 0; i < m_TriggerSnapshot.Count; i++)
            {
                BattleTrigger trigger = m_TriggerSnapshot[i];
                if (trigger.Matches(in evt))
                {
                    trigger.Handler(this, evt);
                }
            }
        }
    }
}
