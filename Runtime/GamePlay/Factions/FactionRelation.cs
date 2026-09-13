//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Factions
{
    /// <summary>
    /// 两个阵营（队伍）之间的关系。用于目标筛选时判定敌/友/中立。
    /// </summary>
    public enum FactionRelation
    {
        /// <summary>
        /// 友方：彼此不应被作为攻击目标，可互相增益。
        /// </summary>
        Ally,

        /// <summary>
        /// 中立：互不攻击，但也不结盟（例如野怪、第三方 NPC）。
        /// </summary>
        Neutral,

        /// <summary>
        /// 敌对：互为攻击目标。
        /// </summary>
        Enemy
    }
}
