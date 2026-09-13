//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Battle
{
    /// <summary>
    /// 战斗单位的属性模型：自包含的简单数值，不依赖任何其它玩法系统。
    /// 当前血量 <see cref="Hp"/> 由引擎管理，始终被钳制在 [0, <see cref="MaxHp"/>] 区间内。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class BattleStats
    {
        private int m_Hp;

        /// <summary>
        /// 最大生命值，必须为正。
        /// </summary>
        public int MaxHp { get; }

        /// <summary>
        /// 攻击力，必须为非负。
        /// </summary>
        public int Attack { get; }

        /// <summary>
        /// 防御力，必须为非负。用于伤害减免：减免后伤害 = max(1, 原始伤害 - 防御)。
        /// </summary>
        public int Defense { get; }

        /// <summary>
        /// 速度，必须为非负。决定回合行动顺序（降序）。
        /// </summary>
        public int Speed { get; }

        /// <summary>
        /// 当前生命值。读取公开；写入仅限引擎内部（<c>internal</c>），写入时自动钳制到 [0, <see cref="MaxHp"/>]。
        /// </summary>
        public int Hp
        {
            get { return m_Hp; }
            internal set
            {
                if (value < 0)
                {
                    m_Hp = 0;
                }
                else if (value > MaxHp)
                {
                    m_Hp = MaxHp;
                }
                else
                {
                    m_Hp = value;
                }
            }
        }

        /// <summary>
        /// 是否存活：当前生命值大于 0。
        /// </summary>
        public bool IsAlive
        {
            get { return m_Hp > 0; }
        }

        /// <summary>
        /// 构造属性。初始当前生命值等于 <paramref name="maxHp"/>。
        /// </summary>
        /// <param name="maxHp">最大生命值，必须为正。</param>
        /// <param name="attack">攻击力，必须为非负。</param>
        /// <param name="defense">防御力，必须为非负。</param>
        /// <param name="speed">速度，必须为非负。</param>
        /// <exception cref="ArgumentOutOfRangeException">当任一参数超出允许范围时抛出。</exception>
        public BattleStats(int maxHp, int attack, int defense, int speed)
        {
            if (maxHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHp), "最大生命值必须为正。");
            }

            if (attack < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attack), "攻击力不能为负。");
            }

            if (defense < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(defense), "防御力不能为负。");
            }

            if (speed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(speed), "速度不能为负。");
            }

            MaxHp = maxHp;
            Attack = attack;
            Defense = defense;
            Speed = speed;
            m_Hp = maxHp;
        }
    }
}
