//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 一次伤害结算的结果快照。值类型，便于在热路径上零分配返回。
    /// </summary>
    public readonly struct DamageResult
    {
        private readonly float m_Applied;
        private readonly bool m_WasLethal;

        /// <summary>
        /// 构造一次伤害结果。
        /// </summary>
        /// <param name="applied">实际扣除的生命值（经钳制后的真实数值）。</param>
        /// <param name="wasLethal">本次伤害是否使单位生命值降至 0 及以下（即致命）。</param>
        public DamageResult(float applied, bool wasLethal)
        {
            m_Applied = applied;
            m_WasLethal = wasLethal;
        }

        /// <summary>
        /// 实际扣除的生命值（经钳制后，不超过结算前的剩余生命）。
        /// </summary>
        public float Applied
        {
            get { return m_Applied; }
        }

        /// <summary>
        /// 本次伤害是否致命，即是否令单位生命值降至 0 及以下。
        /// </summary>
        public bool WasLethal
        {
            get { return m_WasLethal; }
        }
    }
}
