//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 目标亲和性：决定一次目标采集面向哪一类阵营关系的单位。
    /// </summary>
    public enum TargetAffinity
    {
        /// <summary>
        /// 仅敌对单位。
        /// </summary>
        Enemies,

        /// <summary>
        /// 仅友方单位（用于治疗 / 增益等）。
        /// </summary>
        Allies,

        /// <summary>
        /// 仅中立单位。
        /// </summary>
        Neutrals,

        /// <summary>
        /// 不限阵营关系（任意单位）。
        /// </summary>
        Any
    }

    /// <summary>
    /// 目标采集过滤条件。描述“想找什么样的目标”，与具体选择策略（<see cref="EjoyFramework.GamePlay.Targeting.TargetingStrategy"/>）正交。
    /// </summary>
    /// <remarks>
    /// 这是一个轻量值类型，按值传递、零分配。射程字段在选择阶段由
    /// <see cref="EjoyFramework.GamePlay.Targeting.TargetSelector"/> 应用，阵营/存活/可瞄准条件由
    /// <see cref="TargetingQuery.Passes"/> 应用。
    /// </remarks>
    public struct TargetFilter
    {
        /// <summary>
        /// 目标亲和性（敌 / 友 / 中立 / 任意）。
        /// </summary>
        public TargetAffinity Affinity;

        /// <summary>
        /// 为 <c>true</c> 时仅保留存活单位。
        /// </summary>
        public bool AliveOnly;

        /// <summary>
        /// 为 <c>true</c> 时仅保留可被瞄准的单位（排除潜行 / 无敌 / 相位）。
        /// </summary>
        public bool TargetableOnly;

        /// <summary>
        /// 最大射程；<c>&lt;= 0</c> 表示无限射程。在选择阶段应用。
        /// </summary>
        public float MaxRange;

        /// <summary>
        /// 默认过滤条件：面向敌方、仅存活、仅可瞄准、无限射程。
        /// </summary>
        public static TargetFilter Default
        {
            get
            {
                return new TargetFilter
                {
                    Affinity = TargetAffinity.Enemies,
                    AliveOnly = true,
                    TargetableOnly = true,
                    MaxRange = 0f
                };
            }
        }

        /// <summary>
        /// 在 <see cref="Default"/> 基础上限定射程，得到“射程内的敌方”过滤条件。
        /// </summary>
        /// <param name="range">最大射程；<c>&lt;= 0</c> 表示无限射程。</param>
        /// <returns>配置好的过滤条件。</returns>
        public static TargetFilter EnemiesInRange(float range)
        {
            TargetFilter filter = Default;
            filter.MaxRange = range;
            return filter;
        }
    }
}
