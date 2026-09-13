//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 战斗单位：以字符串 <see cref="Id"/> 唯一标识，归属于某个队伍 <see cref="TeamId"/>，持有一份 <see cref="BattleStats"/>。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class BattleUnit
    {
        /// <summary>
        /// 单位唯一标识，不可为空。在同一 <see cref="BattleState"/> 内唯一。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 队伍标识，不可为空。同队伍单位视为友方。
        /// </summary>
        public string TeamId { get; }

        /// <summary>
        /// 单位属性。
        /// </summary>
        public BattleStats Stats { get; }

        /// <summary>
        /// 构造战斗单位。
        /// </summary>
        /// <param name="id">单位唯一标识，不可为空。</param>
        /// <param name="teamId">队伍标识，不可为空。</param>
        /// <param name="stats">单位属性，不可为空。</param>
        /// <exception cref="ArgumentException">当 <paramref name="id"/> 或 <paramref name="teamId"/> 为空时抛出。</exception>
        /// <exception cref="ArgumentNullException">当 <paramref name="stats"/> 为空时抛出。</exception>
        public BattleUnit(string id, string teamId, BattleStats stats)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("单位标识不能为空。", nameof(id));
            }

            if (string.IsNullOrEmpty(teamId))
            {
                throw new ArgumentException("队伍标识不能为空。", nameof(teamId));
            }

            if (stats == null)
            {
                throw new ArgumentNullException(nameof(stats));
            }

            Id = id;
            TeamId = teamId;
            Stats = stats;
        }
    }
}
