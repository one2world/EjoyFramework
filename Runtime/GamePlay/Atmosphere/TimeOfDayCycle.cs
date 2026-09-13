//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 引擎无关的昼夜循环模拟核心。
    /// 仅维护「一天中的归一化进度」与派生量（小时、时段、太阳角度、天数），
    /// 由外部把输出（太阳角度、时段、天数）接到灯光 / 天空盒 / 事件上。
    /// </summary>
    /// <remarks>
    /// 时段映射（归一化区间，左闭右开）：
    /// <list type="bullet">
    /// <item>夜晚 Night：[0, 0.21)</item>
    /// <item>黎明 Dawn：[0.21, 0.29)</item>
    /// <item>白昼 Day：[0.29, 0.71)</item>
    /// <item>黄昏 Dusk：[0.71, 0.79)</item>
    /// <item>夜晚 Night：[0.79, 1)</item>
    /// </list>
    /// 太阳角度映射：<c>SunAngleDegrees = (Normalized * 360 + 270) mod 360</c>。
    /// 即午夜（0）太阳在 270°（地平线以下最低点），正午（0.5）在 90°（天顶正上方），
    /// 06:00（0.25）在 0°（东方地平线），18:00（0.75）在 180°（西方地平线）。
    /// </remarks>
    public sealed class TimeOfDayCycle : FrameworkModule, ITimeOfDayCycle
    {
        // 时段边界（与上面 remarks 保持一致）。
        private const float DawnStart = 0.21f;
        private const float DayStart = 0.29f;
        private const float DuskStart = 0.71f;
        private const float NightStart = 0.79f;

        // 无参构造（Activator.CreateInstance）默认的昼夜时长与起始时间，业务可用 SetDayLength / SetNormalized 覆盖。
        private const float DefaultDayLengthSeconds = 600f;
        private const float DefaultStartNormalized = 0.25f;

        private float m_DayLengthSeconds;
        private float m_Normalized;
        private int m_DayCount;
        private DayPhase m_Phase;

        /// <summary>
        /// 无参构造，供 <c>Framework.GetModule&lt;ITimeOfDayCycle&gt;()</c> 通过 <see cref="System.Activator"/> 创建。
        /// 默认昼夜时长 <see cref="DefaultDayLengthSeconds"/> 秒、起始 06:00，可用 <see cref="SetDayLength"/> /
        /// <see cref="SetNormalized"/> 调整。
        /// </summary>
        public TimeOfDayCycle()
            : this(DefaultDayLengthSeconds, DefaultStartNormalized)
        {
        }

        /// <summary>
        /// 以指定参数构造昼夜循环。保留作为内部便捷构造（测试与样例直接 new），
        /// 框架解析模块走无参构造。
        /// </summary>
        /// <param name="dayLengthSeconds">一个游戏内昼夜对应的真实秒数，必须为正。</param>
        /// <param name="startNormalized">
        /// 起始归一化时间，落在 [0,1)：0=午夜，0.25=06:00，0.5=正午，0.75=18:00。
        /// 超出范围会被 wrap 回 [0,1)。
        /// </param>
        internal TimeOfDayCycle(float dayLengthSeconds, float startNormalized = 0.25f)
        {
            if (dayLengthSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(dayLengthSeconds), "昼夜时长必须为正数。");
            }

            m_DayLengthSeconds = dayLengthSeconds;
            m_Normalized = Wrap01(startNormalized);
            m_DayCount = 0;
            m_Phase = ComputePhase(m_Normalized);
        }

        /// <summary>
        /// 当天的归一化进度，落在 [0,1)。0=午夜，0.5=正午。
        /// </summary>
        public float Normalized
        {
            get { return m_Normalized; }
        }

        /// <summary>
        /// 当前一天中的小时数，落在 [0,24)。
        /// </summary>
        public float HourOfDay
        {
            get { return m_Normalized * 24f; }
        }

        /// <summary>
        /// 当前时段，由 <see cref="Normalized"/> 推导，在 Tick / SetNormalized 时刷新。
        /// </summary>
        public DayPhase Phase
        {
            get { return m_Phase; }
        }

        /// <summary>
        /// 太阳角度（度），范围 [0,360)。映射见类型 remarks：午夜 270°、06:00 为 0°、正午 90°、18:00 为 180°。
        /// </summary>
        public float SunAngleDegrees
        {
            get
            {
                float angle = m_Normalized * 360f + 270f;
                // 归一化到 [0,360)。
                angle %= 360f;
                if (angle < 0f)
                {
                    angle += 360f;
                }

                return angle;
            }
        }

        /// <summary>
        /// 累计经过的午夜次数（天数）。每跨过一次午夜递增。
        /// </summary>
        public int DayCount
        {
            get { return m_DayCount; }
        }

        /// <summary>
        /// 一个游戏内昼夜对应的真实秒数。赋值必须为正，否则抛异常。
        /// </summary>
        public float DayLengthSeconds
        {
            get { return m_DayLengthSeconds; }
            set
            {
                if (value <= 0f)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "昼夜时长必须为正数。");
                }

                m_DayLengthSeconds = value;
            }
        }

        /// <summary>
        /// 配置昼夜时长（秒）的便捷方法，等价于设置 <see cref="DayLengthSeconds"/>。
        /// 沿用核心模块 SetXxx 注入风格，便于 <c>GetModule</c> 拿到实例后做配置。
        /// </summary>
        /// <param name="dayLengthSeconds">一个游戏内昼夜对应的真实秒数，必须为正。</param>
        public void SetDayLength(float dayLengthSeconds)
        {
            DayLengthSeconds = dayLengthSeconds;
        }

        /// <summary>
        /// 当时段真正发生变化时触发，参数为本循环实例与新时段。
        /// </summary>
        public event Action<TimeOfDayCycle, DayPhase> OnPhaseChanged;

        /// <summary>
        /// 当跨过午夜、天数递增时触发，参数为本循环实例与新的 <see cref="DayCount"/>。
        /// 单次 Tick 跨多天时，每跨一天触发一次（不合并）。
        /// </summary>
        public event Action<TimeOfDayCycle, int> OnNewDay;

        /// <summary>
        /// 框架优先级。无特殊先后要求，取默认 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架每帧轮询。把 <paramref name="elapseSeconds"/> 作为推进步长驱动昼夜前进。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒），昼夜推进不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Tick(elapseSeconds);
        }

        /// <summary>
        /// 关闭模块：清空事件订阅并把运行态复位到起始默认。
        /// </summary>
        public override void Shutdown()
        {
            OnPhaseChanged = null;
            OnNewDay = null;
            m_DayLengthSeconds = DefaultDayLengthSeconds;
            m_Normalized = Wrap01(DefaultStartNormalized);
            m_DayCount = 0;
            m_Phase = ComputePhase(m_Normalized);
        }

        /// <summary>
        /// 推进时间。归一化进度按 <c>deltaSeconds / DayLengthSeconds</c> 前进；
        /// 每跨过 1.0 都会减 1、天数 +1 并触发一次 <see cref="OnNewDay"/>（支持单帧跨多天）。
        /// 推进后若时段变化则触发 <see cref="OnPhaseChanged"/>。
        /// 非正的 deltaSeconds 直接忽略（不回退时间）。
        /// </summary>
        /// <param name="deltaSeconds">本次推进的真实秒数。</param>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }

            DayPhase oldPhase = m_Phase;

            m_Normalized += deltaSeconds / m_DayLengthSeconds;

            // 单帧可能跨过多天，逐天结算以保证每天都触发一次 OnNewDay。
            while (m_Normalized >= 1f)
            {
                m_Normalized -= 1f;
                m_DayCount++;
                if (OnNewDay != null)
                {
                    OnNewDay(this, m_DayCount);
                }
            }

            m_Phase = ComputePhase(m_Normalized);
            if (m_Phase != oldPhase && OnPhaseChanged != null)
            {
                OnPhaseChanged(this, m_Phase);
            }
        }

        /// <summary>
        /// 直接跳转到指定归一化时间（落在 [0,1)，越界自动 wrap）。
        /// 刷新时段并在变化时触发 <see cref="OnPhaseChanged"/>，但<b>不</b>改变 <see cref="DayCount"/>，
        /// 也不会触发 <see cref="OnNewDay"/>。
        /// </summary>
        /// <param name="t">目标归一化时间。</param>
        public void SetNormalized(float t)
        {
            DayPhase oldPhase = m_Phase;
            m_Normalized = Wrap01(t);
            m_Phase = ComputePhase(m_Normalized);
            if (m_Phase != oldPhase && OnPhaseChanged != null)
            {
                OnPhaseChanged(this, m_Phase);
            }
        }

        /// <summary>
        /// 根据归一化时间计算时段。区间见类型 remarks。
        /// </summary>
        private static DayPhase ComputePhase(float normalized)
        {
            if (normalized < DawnStart)
            {
                return DayPhase.Night;
            }

            if (normalized < DayStart)
            {
                return DayPhase.Dawn;
            }

            if (normalized < DuskStart)
            {
                return DayPhase.Day;
            }

            if (normalized < NightStart)
            {
                return DayPhase.Dusk;
            }

            return DayPhase.Night;
        }

        /// <summary>
        /// 把任意值规整到 [0,1)。对 NaN/无穷返回 0。
        /// </summary>
        private static float Wrap01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            value %= 1f;
            if (value < 0f)
            {
                value += 1f;
            }

            // 浮点取模后可能因精度恰好等于 1f，强制压回 [0,1)。
            if (value >= 1f)
            {
                value = 0f;
            }

            return value;
        }
    }
}
