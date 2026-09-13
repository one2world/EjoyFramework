//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Units
{
    /// <summary>
    /// 机关 / 陷阱（机关）的触发模式。决定 <see cref="MechanismTrigger"/> 在何种条件下发火。
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>
        /// 邻近触发：当合格目标进入范围且机关已就绪（armed）时发火。
        /// 由调用方在每次 <see cref="MechanismTrigger.Tick(float, bool)"/> 传入“本 tick 是否有目标在范围内”。
        /// </summary>
        Proximity,

        /// <summary>
        /// 定时触发：每经过 <see cref="MechanismTrigger.Interval"/> 秒，若已就绪则发火一次（周期循环）。
        /// </summary>
        Timer,

        /// <summary>
        /// 手动触发：仅当外部显式调用 <see cref="MechanismTrigger.TryManualTrigger"/> 且已就绪时发火。
        /// <see cref="MechanismTrigger.Tick(float, bool)"/> 仅推进冷却，不会自动发火。
        /// </summary>
        Manual
    }

    /// <summary>
    /// 机关 / 陷阱的引擎无关触发内核：封装“触发模式 + 冷却 + 就绪/解除武装（arming）+ 触发计数”逻辑，
    /// 完全可单元测试，不依赖任何引擎类型（仅使用 <c>System.*</c>）。
    /// </summary>
    /// <remarks>
    /// 设计要点：
    /// <list type="bullet">
    /// <item>
    /// <b>就绪（IsArmed）</b>：当 <see cref="CooldownRemaining"/> &lt;= 0
    /// <b>且</b>（<see cref="MaxTriggers"/> &lt;= 0 表示无限，或 <see cref="TriggerCount"/> &lt; <see cref="MaxTriggers"/>）时为 true。
    /// 到达 <see cref="MaxTriggers"/> 上限后将<b>永久</b>解除武装，直到 <see cref="Reset"/>。
    /// </item>
    /// <item>
    /// <b>邻近（<see cref="TriggerMode.Proximity"/>）</b>：在 <see cref="Tick(float, bool)"/> 传入
    /// <c>targetInRange</c>。当 <c>targetInRange &amp;&amp; IsArmed</c> 时发火：计数自增、开始冷却、触发
    /// <see cref="OnFired"/> 并返回 true。是否“在范围内”由调用方判定（本类不做空间查询；<see cref="Radius"/> 仅作信息记录）。
    /// </item>
    /// <item>
    /// <b>定时（<see cref="TriggerMode.Timer"/>）</b>：在 <see cref="Tick(float, bool)"/> 累积时间，每满
    /// <see cref="Interval"/> 秒，若就绪则发火。<b>冷却与周期的关系</b>：Timer 以 <see cref="Interval"/> 作为发火周期；
    /// <see cref="Cooldown"/> 与之独立叠加——发火后既要再过一个 <see cref="Interval"/>，也要等冷却归零才会再次发火。
    /// 若不想引入额外节流，请把 <see cref="Cooldown"/> 设为 0（推荐的简单用法），此时纯由 <see cref="Interval"/> 控制节奏。
    /// </item>
    /// <item>
    /// <b>手动（<see cref="TriggerMode.Manual"/>）</b>：<see cref="TryManualTrigger"/> 在就绪时发火并返回 true；
    /// <see cref="Tick(float, bool)"/> 只推进冷却，从不自动发火。
    /// </item>
    /// <item>
    /// <b>单线程</b>：本类型非线程安全，所有访问应发生在同一逻辑线程。热路径
    /// （<see cref="Tick(float, bool)"/>、<see cref="TryManualTrigger"/>）不产生堆分配。
    /// </item>
    /// </list>
    /// </remarks>
    public sealed class MechanismTrigger
    {
        private readonly TriggerMode m_Mode;

        private float m_Cooldown;
        private float m_Radius;
        private float m_Interval;
        private int m_MaxTriggers;

        private int m_TriggerCount;
        private float m_CooldownRemaining;
        private float m_TimerAccumulator;

        /// <summary>
        /// 构造一个机关触发器。
        /// </summary>
        /// <param name="mode">触发模式（构造后不可变）。</param>
        public MechanismTrigger(TriggerMode mode)
        {
            m_Mode = mode;
            m_Interval = 1f;
        }

        /// <summary>
        /// 触发模式（构造后不可变）。
        /// </summary>
        public TriggerMode Mode
        {
            get { return m_Mode; }
        }

        /// <summary>
        /// 两次发火之间的冷却时长（秒）。负值会被钳制为 0。每次 <see cref="Tick(float, bool)"/> 递减剩余冷却。
        /// </summary>
        public float Cooldown
        {
            get { return m_Cooldown; }
            set { m_Cooldown = value < 0f ? 0f : value; }
        }

        /// <summary>
        /// 邻近触发半径（世界单位）。仅作信息记录——本类不据此做空间查询，
        /// “是否在范围内”由调用方在 <see cref="Tick(float, bool)"/> 传入。负值会被钳制为 0。
        /// </summary>
        public float Radius
        {
            get { return m_Radius; }
            set { m_Radius = value < 0f ? 0f : value; }
        }

        /// <summary>
        /// 定时模式（<see cref="TriggerMode.Timer"/>）的发火周期（秒）。必须为正；
        /// 赋非正值（含 NaN）时回退为 1。
        /// </summary>
        public float Interval
        {
            get { return m_Interval; }
            set { m_Interval = (value > 0f) ? value : 1f; }
        }

        /// <summary>
        /// 最大触发次数。<c>&lt;= 0</c> 表示无限；否则达到该次数后永久解除武装，直到 <see cref="Reset"/>。
        /// </summary>
        public int MaxTriggers
        {
            get { return m_MaxTriggers; }
            set { m_MaxTriggers = value; }
        }

        /// <summary>
        /// 已发火次数（自构造或最近一次 <see cref="Reset"/> 起累计）。
        /// </summary>
        public int TriggerCount
        {
            get { return m_TriggerCount; }
        }

        /// <summary>
        /// 是否已就绪（可发火）：<see cref="CooldownRemaining"/> &lt;= 0
        /// 且（<see cref="MaxTriggers"/> &lt;= 0 或 <see cref="TriggerCount"/> &lt; <see cref="MaxTriggers"/>）。
        /// </summary>
        public bool IsArmed
        {
            get
            {
                bool underLimit = m_MaxTriggers <= 0 || m_TriggerCount < m_MaxTriggers;
                return m_CooldownRemaining <= 0f && underLimit;
            }
        }

        /// <summary>
        /// 距离冷却结束的剩余时长（秒），永不为负。
        /// </summary>
        public float CooldownRemaining
        {
            get { return m_CooldownRemaining; }
        }

        /// <summary>
        /// 推进逻辑时间。语义随 <see cref="Mode"/> 而定：
        /// <list type="bullet">
        /// <item><see cref="TriggerMode.Proximity"/>：当 <paramref name="targetInRange"/> 为 true 且已就绪时发火。</item>
        /// <item><see cref="TriggerMode.Timer"/>：累积时间，每满 <see cref="Interval"/> 秒且就绪时发火（<paramref name="targetInRange"/> 被忽略）。</item>
        /// <item><see cref="TriggerMode.Manual"/>：仅推进冷却，从不自动发火（<paramref name="targetInRange"/> 被忽略）。</item>
        /// </list>
        /// 任意模式下，先递减冷却，再判定发火。发火时：<see cref="TriggerCount"/> 自增、重启冷却、触发 <see cref="OnFired"/>。
        /// </summary>
        /// <param name="deltaTime">本次时间增量（秒）。非正值（含 NaN）按 0 处理，仅做发火判定不推进时间。</param>
        /// <param name="targetInRange">仅 <see cref="TriggerMode.Proximity"/> 使用：本 tick 是否有合格目标在范围内。</param>
        /// <returns>本 tick 是否发火。</returns>
        public bool Tick(float deltaTime, bool targetInRange = false)
        {
            // 防御 NaN / 负增量：不向前推进时间，但仍允许在已就绪时按当前条件发火。
            if (!(deltaTime > 0f))
            {
                deltaTime = 0f;
            }

            // 先递减冷却。
            if (m_CooldownRemaining > 0f)
            {
                m_CooldownRemaining -= deltaTime;
                if (m_CooldownRemaining < 0f)
                {
                    m_CooldownRemaining = 0f;
                }
            }

            switch (m_Mode)
            {
                case TriggerMode.Proximity:
                    if (targetInRange && IsArmed)
                    {
                        Fire();
                        return true;
                    }
                    return false;

                case TriggerMode.Timer:
                    m_TimerAccumulator += deltaTime;
                    // 至多在一个 tick 内消化一个周期，保持单次发火语义（不在一帧内连发）。
                    if (m_TimerAccumulator >= m_Interval)
                    {
                        m_TimerAccumulator -= m_Interval;
                        if (IsArmed)
                        {
                            Fire();
                            return true;
                        }
                    }
                    return false;

                case TriggerMode.Manual:
                default:
                    // 手动模式：Tick 仅推进冷却，不自动发火。
                    return false;
            }
        }

        /// <summary>
        /// 手动触发（<see cref="TriggerMode.Manual"/>）。就绪时发火并返回 true，否则为空操作返回 false。
        /// 出于稳健，本方法在任意模式下都按“就绪即发火”工作；但仅在 Manual 模式下被设计为唯一发火入口。
        /// </summary>
        /// <returns>是否发火。</returns>
        public bool TryManualTrigger()
        {
            if (!IsArmed)
            {
                return false;
            }

            Fire();
            return true;
        }

        /// <summary>
        /// 重置为初始就绪状态：清空 <see cref="TriggerCount"/>、剩余冷却与定时累积，使其重新就绪。
        /// 配置项（<see cref="Cooldown"/>/<see cref="Radius"/>/<see cref="Interval"/>/<see cref="MaxTriggers"/>）保持不变。
        /// </summary>
        public void Reset()
        {
            m_TriggerCount = 0;
            m_CooldownRemaining = 0f;
            m_TimerAccumulator = 0f;
        }

        /// <summary>
        /// 发火时触发：携带本触发器自身（便于订阅方读取 <see cref="TriggerCount"/> 等）。
        /// </summary>
        public event Action<MechanismTrigger> OnFired;

        /// <summary>
        /// 执行一次发火：计数自增、重启冷却、派发 <see cref="OnFired"/>。
        /// </summary>
        private void Fire()
        {
            m_TriggerCount++;
            m_CooldownRemaining = m_Cooldown;

            Action<MechanismTrigger> handler = OnFired;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
