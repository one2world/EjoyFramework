//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Factions;
using EjoyFramework.GamePlay.Targeting;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 目标采集适配器：把单位（<see cref="ITargetableUnit"/>）桥接到既有的目标选择系统
    /// （<see cref="TargetInfo"/> → <see cref="TargetSelector"/> → 选中单位）。具备阵营感知能力，并复用内部缓冲以降低 GC。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 工作流：阵营/存活/可瞄准过滤（<see cref="Passes"/>）→ 构建 <see cref="TargetInfo"/> 快照 →
    /// 交由 <see cref="TargetSelector.TrySelect"/> 应用射程与策略 → 通过 Id 回映到选中的单位。
    /// </para>
    /// <para>
    /// <b>线程假设：</b>本类型复用了内部的 <see cref="List{T}"/> 与 <see cref="Dictionary{TKey,TValue}"/> 缓冲，
    /// 因此<b>仅可用于单线程</b>。每次 <see cref="TryAcquire"/> 调用会在开始时清空这些缓冲，从而保证同一线程内的串行复用是安全的；
    /// 不要在多个线程间共享同一个 <see cref="TargetingQuery"/> 实例。
    /// </para>
    /// </remarks>
    public sealed class TargetingQuery
    {
        private readonly FactionRelations m_Relations;

        // 复用的内部缓冲（仅单线程）：每次 TryAcquire 开始时清空。
        private readonly List<TargetInfo> m_InfoBuffer;
        private readonly Dictionary<int, ITargetableUnit> m_UnitById;

        /// <summary>
        /// 构造目标采集适配器。
        /// </summary>
        /// <param name="relations">阵营关系注册表，用于判定敌/友/中立。</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="relations"/> 为 <c>null</c> 时抛出。</exception>
        public TargetingQuery(FactionRelations relations)
        {
            if (relations == null)
            {
                throw new System.ArgumentNullException(nameof(relations));
            }

            m_Relations = relations;
            m_InfoBuffer = new List<TargetInfo>();
            m_UnitById = new Dictionary<int, ITargetableUnit>();
        }

        /// <summary>
        /// 本适配器使用的阵营关系注册表。
        /// </summary>
        public FactionRelations Relations
        {
            get { return m_Relations; }
        }

        /// <summary>
        /// 判断某个候选单位是否通过阵营 / 存活 / 可瞄准过滤（<b>不</b>检查射程，射程在选择阶段应用）。
        /// </summary>
        /// <param name="candidate">候选单位。</param>
        /// <param name="sourceFactionId">来源单位的阵营 Id。</param>
        /// <param name="filter">过滤条件。</param>
        /// <returns>满足亲和性且满足存活/可瞄准约束时返回 <c>true</c>。</returns>
        public bool Passes(ITargetableUnit candidate, int sourceFactionId, in TargetFilter filter)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!MatchesAffinity(sourceFactionId, candidate.FactionId, filter.Affinity))
            {
                return false;
            }

            if (filter.AliveOnly && !candidate.IsAlive)
            {
                return false;
            }

            if (filter.TargetableOnly && !candidate.IsTargetable)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 将所有通过过滤（<see cref="Passes"/>）且 Id 不等于 <paramref name="excludeId"/> 的候选单位，
        /// 转换为 <see cref="TargetInfo"/> 写入 <paramref name="into"/>（写入前先清空）。不做射程过滤（射程在选择阶段应用）。
        /// </summary>
        /// <param name="candidates">候选单位列表（只读）。</param>
        /// <param name="sourceFactionId">来源单位的阵营 Id。</param>
        /// <param name="filter">过滤条件。</param>
        /// <param name="excludeId">需要排除的单位 Id（通常是来源自身）；传 <c>-1</c> 表示不排除。</param>
        /// <param name="into">输出目标快照列表。会被先清空再填充，不可为 <c>null</c>。</param>
        public void BuildTargetInfos(
            IReadOnlyList<ITargetableUnit> candidates,
            int sourceFactionId, in TargetFilter filter, int excludeId,
            List<TargetInfo> into)
        {
            if (into == null)
            {
                throw new System.ArgumentNullException(nameof(into));
            }

            into.Clear();
            if (candidates == null)
            {
                return;
            }

            // 索引循环，避免迭代器分配与 LINQ（热路径）。
            for (int i = 0; i < candidates.Count; i++)
            {
                ITargetableUnit candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.Id == excludeId)
                {
                    continue;
                }

                if (!Passes(candidate, sourceFactionId, filter))
                {
                    continue;
                }

                into.Add(new TargetInfo(
                    candidate.Id,
                    candidate.PositionX,
                    candidate.PositionY,
                    candidate.Health,
                    candidate.MaxHealth,
                    candidate.Threat,
                    candidate.Priority,
                    candidate.Progress));
            }
        }

        /// <summary>
        /// 为位于 <paramref name="sourceX"/>、<paramref name="sourceY"/>、隶属 <paramref name="sourceFactionId"/> 的来源，
        /// 依据过滤条件与选择策略采集“最佳”目标单位。
        /// </summary>
        /// <remarks>
        /// 阵营/存活/可瞄准通过 <see cref="Passes"/> 过滤；射程（<see cref="TargetFilter.MaxRange"/>）与策略通过
        /// <see cref="TargetSelector.TrySelect"/> 应用。该方法复用内部缓冲，<b>仅可单线程使用</b>，每次调用开头会清空缓冲。
        /// </remarks>
        /// <param name="candidates">候选单位列表（只读）。</param>
        /// <param name="sourceFactionId">来源单位的阵营 Id。</param>
        /// <param name="sourceX">来源点 X 坐标。</param>
        /// <param name="sourceY">来源点 Y 坐标。</param>
        /// <param name="filter">过滤条件。</param>
        /// <param name="strategy">选择策略。</param>
        /// <param name="excludeId">需要排除的单位 Id（通常是来源自身）；传 <c>-1</c> 表示不排除。</param>
        /// <param name="best">输出：选中的单位；返回 <c>false</c> 时为 <c>null</c>。</param>
        /// <returns>存在合格目标时返回 <c>true</c>；否则 <paramref name="best"/> 为 <c>null</c> 并返回 <c>false</c>。</returns>
        public bool TryAcquire(
            IReadOnlyList<ITargetableUnit> candidates,
            int sourceFactionId, float sourceX, float sourceY,
            in TargetFilter filter, TargetingStrategy strategy, int excludeId,
            out ITargetableUnit best)
        {
            best = null;

            // 复用缓冲：每次调用开头清空，保证单线程内串行复用安全。
            m_InfoBuffer.Clear();
            m_UnitById.Clear();

            if (candidates == null)
            {
                return false;
            }

            // 构建快照并同步建立 id → 单位 的回映表（索引循环，避免 LINQ/迭代器分配）。
            for (int i = 0; i < candidates.Count; i++)
            {
                ITargetableUnit candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.Id == excludeId)
                {
                    continue;
                }

                if (!Passes(candidate, sourceFactionId, filter))
                {
                    continue;
                }

                m_InfoBuffer.Add(new TargetInfo(
                    candidate.Id,
                    candidate.PositionX,
                    candidate.PositionY,
                    candidate.Health,
                    candidate.MaxHealth,
                    candidate.Threat,
                    candidate.Priority,
                    candidate.Progress));

                // 后写覆盖前写：若候选 Id 重复，回映到最后一个通过过滤的同 Id 单位。
                m_UnitById[candidate.Id] = candidate;
            }

            if (!TargetSelector.TrySelect(m_InfoBuffer, sourceX, sourceY, strategy, filter.MaxRange, out TargetInfo info))
            {
                return false;
            }

            if (m_UnitById.TryGetValue(info.Id, out ITargetableUnit unit))
            {
                best = unit;
                return true;
            }

            // 理论上不会发生：选中的快照必来自上面填充的缓冲。防御性返回 false。
            return false;
        }

        /// <summary>
        /// 依据亲和性判断来源阵营与候选阵营是否匹配。
        /// </summary>
        private bool MatchesAffinity(int sourceFactionId, int candidateFactionId, TargetAffinity affinity)
        {
            switch (affinity)
            {
                case TargetAffinity.Enemies:
                    return m_Relations.AreEnemies(sourceFactionId, candidateFactionId);
                case TargetAffinity.Allies:
                    return m_Relations.AreAllies(sourceFactionId, candidateFactionId);
                case TargetAffinity.Neutrals:
                    return m_Relations.AreNeutral(sourceFactionId, candidateFactionId);
                case TargetAffinity.Any:
                    return true;
                default:
                    return false;
            }
        }
    }
}
