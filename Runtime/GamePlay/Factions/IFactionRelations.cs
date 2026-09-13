//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Factions
{
    /// <summary>
    /// 基于整型阵营 Id 的成对（pairwise）队伍关系注册表抽象，关系是对称的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 关系按 (a, b) 这一对来存储，并归一化为 (min, max)，因此 (a, b) 与 (b, a) 共享同一条记录，
    /// 保证 <see cref="SetRelation"/> 与 <see cref="GetRelation"/> 天然对称。
    /// </para>
    /// <para>
    /// 关系解析顺序（<see cref="GetRelation"/>）：先看显式覆盖，其次同 Id 默认友方，
    /// 最后落到 <see cref="DefaultBetweenDifferent"/>（不同阵营之间的默认关系）。
    /// </para>
    /// </remarks>
    public interface IFactionRelations
    {
        /// <summary>
        /// 不同阵营之间在未显式设置时的默认关系。修改该值不会影响已显式设置的对。
        /// </summary>
        FactionRelation DefaultBetweenDifferent { get; set; }

        /// <summary>
        /// 显式设置一对阵营之间的关系（对称：<c>(a, b)</c> 与 <c>(b, a)</c> 等价）。
        /// </summary>
        /// <param name="factionA">阵营 A 的 Id。</param>
        /// <param name="factionB">阵营 B 的 Id。</param>
        /// <param name="relation">要设置的关系。</param>
        void SetRelation(int factionA, int factionB, FactionRelation relation);

        /// <summary>
        /// 查询一对阵营之间的关系，按“显式覆盖 → 同 Id 默认友方 → 不同 Id 默认值”的顺序解析。
        /// </summary>
        /// <param name="factionA">阵营 A 的 Id。</param>
        /// <param name="factionB">阵营 B 的 Id。</param>
        /// <returns>解析得到的关系。</returns>
        FactionRelation GetRelation(int factionA, int factionB);

        /// <summary>
        /// 判断两个阵营是否敌对。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Enemy"/> 时返回 <c>true</c>。</returns>
        bool AreEnemies(int a, int b);

        /// <summary>
        /// 判断两个阵营是否友方。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Ally"/> 时返回 <c>true</c>。</returns>
        bool AreAllies(int a, int b);

        /// <summary>
        /// 判断两个阵营是否中立。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>关系为 <see cref="FactionRelation.Neutral"/> 时返回 <c>true</c>。</returns>
        bool AreNeutral(int a, int b);

        /// <summary>
        /// 移除一对阵营的显式覆盖，使其回落到默认解析规则。
        /// </summary>
        /// <param name="a">阵营 A 的 Id。</param>
        /// <param name="b">阵营 B 的 Id。</param>
        /// <returns>存在并成功移除一条显式覆盖时返回 <c>true</c>；本就没有覆盖则返回 <c>false</c>。</returns>
        bool ClearRelation(int a, int b);

        /// <summary>
        /// 清空所有显式覆盖（不影响 <see cref="DefaultBetweenDifferent"/> 与同 Id 默认友方规则）。
        /// </summary>
        void Clear();
    }
}
