//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 战斗局面：持有全部参战单位，提供按队伍/存活筛选与回合行动顺序计算。
    /// 仅维护单位集合，不负责伤害/治疗等逻辑（由 <see cref="BattleEngine"/> 驱动）。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class BattleState
    {
        // 保持插入顺序，便于行动顺序在速度相同时退化为稳定的 Id 排序；同时用字典做 O(1) 查找。
        private readonly List<BattleUnit> m_Units = new List<BattleUnit>();
        private readonly Dictionary<string, BattleUnit> m_UnitsById =
            new Dictionary<string, BattleUnit>(StringComparer.Ordinal);

        /// <summary>
        /// 全部单位（含已阵亡），按加入顺序枚举。
        /// </summary>
        public IEnumerable<BattleUnit> Units
        {
            get { return m_Units; }
        }

        /// <summary>
        /// 全部存活单位（<see cref="BattleStats.IsAlive"/> 为 true），按加入顺序枚举。
        /// </summary>
        public IEnumerable<BattleUnit> AliveUnits
        {
            get
            {
                // 返回新列表快照，避免调用方在枚举期间因引擎改动血量而受影响。
                List<BattleUnit> alive = new List<BattleUnit>();
                for (int i = 0; i < m_Units.Count; i++)
                {
                    if (m_Units[i].Stats.IsAlive)
                    {
                        alive.Add(m_Units[i]);
                    }
                }

                return alive;
            }
        }

        /// <summary>
        /// 加入一个单位。
        /// </summary>
        /// <param name="unit">待加入单位，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="unit"/> 为空时抛出。</exception>
        /// <exception cref="ArgumentException">当存在相同 <see cref="BattleUnit.Id"/> 的单位时抛出。</exception>
        public void AddUnit(BattleUnit unit)
        {
            if (unit == null)
            {
                throw new ArgumentNullException(nameof(unit));
            }

            if (m_UnitsById.ContainsKey(unit.Id))
            {
                throw new ArgumentException("已存在相同标识的单位：" + unit.Id, nameof(unit));
            }

            m_UnitsById.Add(unit.Id, unit);
            m_Units.Add(unit);
        }

        /// <summary>
        /// 按标识查找单位。
        /// </summary>
        /// <param name="id">单位标识。</param>
        /// <returns>找到的单位；不存在（含 id 为空）时返回 null。</returns>
        public BattleUnit GetUnit(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            BattleUnit unit;
            return m_UnitsById.TryGetValue(id, out unit) ? unit : null;
        }

        /// <summary>
        /// 枚举指定队伍的全部单位（含已阵亡）。
        /// </summary>
        /// <param name="teamId">队伍标识。</param>
        /// <returns>该队伍单位的快照；teamId 为空时为空集合。</returns>
        public IEnumerable<BattleUnit> UnitsOfTeam(string teamId)
        {
            List<BattleUnit> result = new List<BattleUnit>();
            if (string.IsNullOrEmpty(teamId))
            {
                return result;
            }

            for (int i = 0; i < m_Units.Count; i++)
            {
                if (string.Equals(m_Units[i].TeamId, teamId, StringComparison.Ordinal))
                {
                    result.Add(m_Units[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 判断某队伍是否已被击败：该队伍至少有一个单位，且其全部单位均已阵亡。
        /// 不存在任何单位的队伍视为未被击败（false）。
        /// </summary>
        /// <param name="teamId">队伍标识。</param>
        /// <returns>全员阵亡返回 true。</returns>
        public bool IsTeamDefeated(string teamId)
        {
            if (string.IsNullOrEmpty(teamId))
            {
                return false;
            }

            bool hasAny = false;
            for (int i = 0; i < m_Units.Count; i++)
            {
                if (!string.Equals(m_Units[i].TeamId, teamId, StringComparison.Ordinal))
                {
                    continue;
                }

                hasAny = true;
                if (m_Units[i].Stats.IsAlive)
                {
                    return false;
                }
            }

            return hasAny;
        }

        /// <summary>
        /// 计算回合行动顺序：仅含存活单位，按速度降序排列，速度相同按 <see cref="BattleUnit.Id"/> 的序号（Ordinal）升序作为稳定的次序保证。
        /// </summary>
        /// <returns>只读的行动顺序列表（每次调用返回新列表）。</returns>
        public IReadOnlyList<BattleUnit> GetTurnOrder()
        {
            List<BattleUnit> order = new List<BattleUnit>();
            for (int i = 0; i < m_Units.Count; i++)
            {
                if (m_Units[i].Stats.IsAlive)
                {
                    order.Add(m_Units[i]);
                }
            }

            order.Sort(CompareTurnOrder);
            return order;
        }

        private static int CompareTurnOrder(BattleUnit left, BattleUnit right)
        {
            // 速度降序。
            int bySpeed = right.Stats.Speed.CompareTo(left.Stats.Speed);
            if (bySpeed != 0)
            {
                return bySpeed;
            }

            // 速度相同：按 Id 序号升序，保证确定性。
            return string.CompareOrdinal(left.Id, right.Id);
        }
    }
}
