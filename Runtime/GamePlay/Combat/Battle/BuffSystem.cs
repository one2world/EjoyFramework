//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Battle
{
    [Serializable]
    public sealed class BuffDef
    {
        public int BuffId;
        public string Name;
        public BuffKind Kind;
        public float DurationSeconds;       // <=0 表示永久（业务侧 Dispel）
        public float TickIntervalSeconds;   // DOT/HOT 频率
        public int TickAmount;              // 每次 tick 的伤害 / 治疗（DOT > 0 = 减 HP，HOT > 0 = 加 HP）
        public StackRule Stacking;
        public int MaxStacks;
    }

    public enum BuffKind
    {
        Buff,        // 增益（如攻击力 +10%）
        Debuff,      // 减益
        DOT,         // damage over time
        HOT,         // heal over time
        Stun,        // 控制（业务读 IsStunned 决定行为）
    }

    public enum StackRule
    {
        Refresh,        // 重复施加刷新持续时间
        Stack,          // 叠加层数
        Independent,    // 每次独立实例
    }

    /// <summary>实体身上的 buff 实例。</summary>
    public sealed class BuffInstance
    {
        public BuffDef Def;
        public IBattleEntity Source;
        public IBattleEntity Target;
        public float RemainingSeconds;
        public float SinceLastTick;
        public int Stacks;
    }

    public interface IBuffManager
    {
        void RegisterBuff(BuffDef def);
        void ApplyBuff(IBattleEntity target, int buffId, IBattleEntity source);
        void RemoveBuff(IBattleEntity target, int buffId);
        void RemoveAllBuffs(IBattleEntity target);
        IReadOnlyList<BuffInstance> GetBuffs(IBattleEntity target);
        bool HasBuff(IBattleEntity target, int buffId);

        event Action<IBattleEntity, BuffInstance> BuffApplied;
        event Action<IBattleEntity, BuffInstance> BuffRemoved;
    }

    public sealed class BuffManager : FrameworkModule, IBuffManager
    {
        private readonly Dictionary<int, BuffDef> m_Defs = new Dictionary<int, BuffDef>();
        private readonly Dictionary<int, List<BuffInstance>> m_ByTarget = new Dictionary<int, List<BuffInstance>>();

        // Update 期间复用的快照缓冲：DOT/HOT 回调可能重入 ApplyBuff/RemoveBuff（详见 Update 注释），
        // 故先快照、后结算。复用字段避免每帧分配；Update 由框架单线程驱动，不会自身重入。
        private readonly List<KeyValuePair<int, List<BuffInstance>>> m_UpdateTargets = new List<KeyValuePair<int, List<BuffInstance>>>();
        private readonly List<BuffInstance> m_UpdateInstances = new List<BuffInstance>();

        public event Action<IBattleEntity, BuffInstance> BuffApplied;
        public event Action<IBattleEntity, BuffInstance> BuffRemoved;

        public override int Priority { get { return 0; } }

        /// <summary>
        /// 每帧推进所有 buff：递减剩余时间、按间隔结算 DOT/HOT tick，并收集到期实例。
        /// DOT/HOT 通过 BuffInstance.Target（ApplyBuff 时缓存）作用于目标。
        /// 到期 buff 在遍历完成后统一移除并触发 BuffRemoved，避免迭代中修改集合。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (m_ByTarget.Count == 0) return;
            List<(int, BuffInstance)> expired = null;

            // 快照「目标 -> buff 列表」再结算：DOT/HOT 会回调用户的 ApplyDamage/ApplyHeal，业务方可能在其中
            // 重入 ApplyBuff（向 m_ByTarget 增删键，如「受击触发 debuff」）或 RemoveBuff。直接遍历活动集合会抛
            // InvalidOperationException 或错位。BuffInstance 为引用类型，快照内对其字段的累减仍写回真实实例。
            m_UpdateTargets.Clear();
            foreach (var kv in m_ByTarget) m_UpdateTargets.Add(kv);

            for (int t = 0; t < m_UpdateTargets.Count; t++)
            {
                int targetId = m_UpdateTargets[t].Key;
                var list = m_UpdateTargets[t].Value;

                // 同一 target 的 buff 也快照，避免重入 RemoveBuff 改动正在迭代的 list。
                m_UpdateInstances.Clear();
                for (int i = 0; i < list.Count; i++) m_UpdateInstances.Add(list[i]);

                for (int i = 0; i < m_UpdateInstances.Count; i++)
                {
                    var inst = m_UpdateInstances[i];
                    if (inst.Def.DurationSeconds > 0)
                    {
                        inst.RemainingSeconds -= elapseSeconds;
                        if (inst.RemainingSeconds <= 0f)
                        {
                            (expired ??= new List<(int, BuffInstance)>()).Add((targetId, inst));
                            continue;
                        }
                    }
                    if (inst.Def.TickIntervalSeconds > 0)
                    {
                        inst.SinceLastTick += elapseSeconds;
                        while (inst.SinceLastTick >= inst.Def.TickIntervalSeconds)
                        {
                            inst.SinceLastTick -= inst.Def.TickIntervalSeconds;
                            var target = inst.Target;
                            if (target == null || !target.IsAlive) continue;
                            int amount = inst.Def.TickAmount * inst.Stacks;
                            if (inst.Def.Kind == BuffKind.DOT) target.ApplyDamage(amount, inst.Source);
                            else if (inst.Def.Kind == BuffKind.HOT) target.ApplyHeal(amount, inst.Source);
                        }
                    }
                }
            }
            m_UpdateInstances.Clear();
            m_UpdateTargets.Clear();

            if (expired != null)
            {
                foreach (var (targetId, inst) in expired)
                {
                    if (m_ByTarget.TryGetValue(targetId, out var list))
                    {
                        if (list.Remove(inst))
                        {
                            RemoveTargetIfEmpty(targetId, list);
                            try { BuffRemoved?.Invoke(inst.Target, inst); }
                            catch (Exception ex) { FrameworkLog.Error("BuffRemoved threw: {0}", ex); }
                        }
                    }
                }
            }
        }

        public override void Shutdown() { m_Defs.Clear(); m_ByTarget.Clear(); }

        public void RegisterBuff(BuffDef def)
        {
            if (def == null) throw new FrameworkException("def is null.");
            m_Defs[def.BuffId] = def;
        }

        public void ApplyBuff(IBattleEntity target, int buffId, IBattleEntity source)
        {
            Framework.EnsureMainThread(nameof(ApplyBuff));
            if (target == null || !target.IsAlive) return;
            if (!m_Defs.TryGetValue(buffId, out var def)) return;

            if (!m_ByTarget.TryGetValue(target.EntityId, out var list))
            {
                list = new List<BuffInstance>();
                m_ByTarget[target.EntityId] = list;
            }

            BuffInstance existing = null;
            if (def.Stacking != StackRule.Independent)
            {
                foreach (var b in list) if (b.Def.BuffId == buffId) { existing = b; break; }
            }

            if (existing != null)
            {
                if (def.Stacking == StackRule.Refresh)
                {
                    existing.RemainingSeconds = def.DurationSeconds;
                }
                else if (def.Stacking == StackRule.Stack)
                {
                    existing.Stacks = System.Math.Min(existing.Stacks + 1, def.MaxStacks > 0 ? def.MaxStacks : int.MaxValue);
                    existing.RemainingSeconds = def.DurationSeconds;
                }
            }
            else
            {
                var inst = new BuffInstance
                {
                    Def = def,
                    Source = source,
                    Target = target,
                    RemainingSeconds = def.DurationSeconds,
                    Stacks = 1,
                };
                list.Add(inst);
                try { BuffApplied?.Invoke(target, inst); }
                catch (Exception ex) { FrameworkLog.Error("BuffApplied threw: {0}", ex); }
            }
        }

        public void RemoveBuff(IBattleEntity target, int buffId)
        {
            if (target == null) return;
            if (!m_ByTarget.TryGetValue(target.EntityId, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Def.BuffId == buffId)
                {
                    var inst = list[i];
                    list.RemoveAt(i);
                    try { BuffRemoved?.Invoke(target, inst); }
                    catch (Exception ex) { FrameworkLog.Error("BuffRemoved threw: {0}", ex); }
                }
            }
            RemoveTargetIfEmpty(target.EntityId, list);
        }

        public void RemoveAllBuffs(IBattleEntity target)
        {
            if (target == null) return;
            if (!m_ByTarget.TryGetValue(target.EntityId, out var list)) return;
            // 先快照再清空，最后回调：handler 若重入 ApplyBuff/RemoveBuff 同一 target，
            // 不会在遍历中修改正在迭代的集合（否则抛 InvalidOperationException）。
            var snapshot = list.ToArray();
            list.Clear();
            RemoveTargetIfEmpty(target.EntityId, list);
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { BuffRemoved?.Invoke(target, snapshot[i]); }
                catch (Exception ex) { FrameworkLog.Error("BuffRemoved threw: {0}", ex); }
            }
        }

        public IReadOnlyList<BuffInstance> GetBuffs(IBattleEntity target)
        {
            if (target == null) return System.Array.Empty<BuffInstance>();
            return m_ByTarget.TryGetValue(target.EntityId, out var list)
                ? (IReadOnlyList<BuffInstance>)list
                : System.Array.Empty<BuffInstance>();
        }

        public bool HasBuff(IBattleEntity target, int buffId)
        {
            if (target == null) return false;
            if (!m_ByTarget.TryGetValue(target.EntityId, out var list)) return false;
            foreach (var b in list) if (b.Def.BuffId == buffId) return true;
            return false;
        }

        private void RemoveTargetIfEmpty(int targetId, List<BuffInstance> list)
        {
            if (list.Count == 0
                && m_ByTarget.TryGetValue(targetId, out var current)
                && ReferenceEquals(current, list))
            {
                m_ByTarget.Remove(targetId);
            }
        }
    }
}
