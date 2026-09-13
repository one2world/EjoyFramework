//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 引擎无关的天气状态机。负责：
    /// <list type="bullet">
    /// <item>维护当前天气 <see cref="Current"/> 与目标强度，并在过渡期间对 <see cref="Intensity"/> 做插值。</item>
    /// <item>维护一张「从某天气出发」的加权转移表，<see cref="RollNext"/> 按权重随机挑选下一种天气。</item>
    /// </list>
    /// 切换语义：调用 <see cref="SetWeather"/> 时 <see cref="Current"/> 立即变为新类型并触发一次
    /// <see cref="OnWeatherChanged"/>（即「过渡开始」即视为类型已切换）；强度则在 <c>transitionSeconds</c>
    /// 内从当前值线性插值到目标值。<c>transitionSeconds&lt;=0</c> 表示瞬间切换。
    /// </summary>
    public sealed class WeatherSystem : FrameworkModule, IWeatherSystem
    {
        // 单条加权转移：目标天气 + 权重。
        private struct Transition
        {
            public WeatherType To;
            public float Weight;
        }

        private Func<double> m_RandomProvider;
        private readonly Dictionary<WeatherType, List<Transition>> m_Transitions;

        private WeatherType m_Current;
        private WeatherType m_Target;

        private float m_Intensity;        // 当前实际强度。
        private float m_StartIntensity;   // 本次过渡起始强度。
        private float m_TargetIntensity;  // 本次过渡目标强度。

        private float m_TransitionDuration; // 本次过渡总时长（秒）。
        private float m_TransitionElapsed;  // 本次过渡已用时长（秒）。
        private bool m_IsTransitioning;

        /// <summary>
        /// 无参构造，供 <c>Framework.GetModule&lt;IWeatherSystem&gt;()</c> 通过 <see cref="System.Activator"/> 创建。
        /// 默认初始天气 <see cref="WeatherType.Clear"/>、强度 1，随机源默认使用内部 <see cref="System.Random"/>，
        /// 可用 <see cref="SetRandomProvider"/> 注入确定性随机源。
        /// </summary>
        public WeatherSystem()
            : this(WeatherType.Clear, null)
        {
        }

        /// <summary>
        /// 以指定参数构造天气系统。保留作为内部便捷构造（测试与样例直接 new），
        /// 框架解析模块走无参构造。
        /// </summary>
        /// <param name="initial">初始天气，强度默认 1。</param>
        /// <param name="randomProvider">
        /// 返回 [0,1) 的随机源，供 <see cref="RollNext"/> 做加权抽样；传 null 则使用内部 <see cref="System.Random"/>。
        /// </param>
        internal WeatherSystem(WeatherType initial = WeatherType.Clear, Func<double> randomProvider = null)
        {
            if (randomProvider != null)
            {
                m_RandomProvider = randomProvider;
            }
            else
            {
                var rng = new Random();
                m_RandomProvider = rng.NextDouble;
            }

            m_Transitions = new Dictionary<WeatherType, List<Transition>>();

            m_Current = initial;
            m_Target = initial;
            m_Intensity = 1f;
            m_StartIntensity = 1f;
            m_TargetIntensity = 1f;
            m_TransitionDuration = 0f;
            m_TransitionElapsed = 0f;
            m_IsTransitioning = false;
        }

        /// <summary>
        /// 注入加权抽样使用的随机源（返回 [0,1)）。传 null 时回退到内部 <see cref="System.Random"/>。
        /// 沿用核心模块 SetXxx 注入风格，便于 <c>GetModule</c> 拿到实例后做配置或在测试中确定性化。
        /// </summary>
        /// <param name="randomProvider">返回 [0,1) 的随机源；为 null 时使用内部随机。</param>
        public void SetRandomProvider(Func<double> randomProvider)
        {
            if (randomProvider != null)
            {
                m_RandomProvider = randomProvider;
            }
            else
            {
                var rng = new Random();
                m_RandomProvider = rng.NextDouble;
            }
        }

        /// <summary>
        /// 设置初始天气并立即生效（强度 1、瞬间切换、无过渡）。
        /// 沿用核心模块 SetXxx 注入风格，便于 <c>GetModule</c> 拿到实例后初始化基线天气。
        /// </summary>
        /// <param name="initial">初始天气。</param>
        public void SetInitialWeather(WeatherType initial)
        {
            SetWeather(initial, 1f, 0f);
        }

        /// <summary>
        /// 当前天气。过渡开始时即切换为新类型。
        /// </summary>
        public WeatherType Current
        {
            get { return m_Current; }
        }

        /// <summary>
        /// 过渡目标天气；未过渡时与 <see cref="Current"/> 相同。
        /// 由于本实现「过渡开始即切换类型」，Target 始终与 Current 保持一致，仅供语义对齐与未来扩展。
        /// </summary>
        public WeatherType Target
        {
            get { return m_Target; }
        }

        /// <summary>
        /// 当前强度，落在 [0,1]。过渡期间从起始强度线性插值到目标强度。
        /// </summary>
        public float Intensity
        {
            get { return m_Intensity; }
        }

        /// <summary>
        /// 是否正处于强度过渡中。
        /// </summary>
        public bool IsTransitioning
        {
            get { return m_IsTransitioning; }
        }

        /// <summary>
        /// 当 <see cref="Current"/> 真正发生变化时触发（即过渡开始那一刻），参数为本系统与新天气类型。
        /// 切到与当前相同的类型不会触发。
        /// </summary>
        public event Action<WeatherSystem, WeatherType> OnWeatherChanged;

        /// <summary>
        /// 切换天气。
        /// <para><c>transitionSeconds&lt;=0</c>：瞬间生效，强度立即设为 <paramref name="intensity"/>。</para>
        /// <para><c>transitionSeconds&gt;0</c>：类型立即切换，强度在该时长内从当前值线性插值到 <paramref name="intensity"/>。</para>
        /// 类型实际改变时触发一次 <see cref="OnWeatherChanged"/>。
        /// </summary>
        /// <param name="type">目标天气。</param>
        /// <param name="intensity">目标强度，自动夹取到 [0,1]。</param>
        /// <param name="transitionSeconds">过渡时长（秒），<=0 表示瞬间。</param>
        public void SetWeather(WeatherType type, float intensity = 1f, float transitionSeconds = 0f)
        {
            // NaN 强度视为 0（GameMath.Clamp01 不特殊处理 NaN，这里在边界显式兜底）。
            float clamped = float.IsNaN(intensity) ? 0f : GameMath.Clamp01(intensity);
            bool typeChanged = type != m_Current;

            m_Current = type;
            m_Target = type;

            if (transitionSeconds <= 0f)
            {
                // 瞬间切换。
                m_Intensity = clamped;
                m_StartIntensity = clamped;
                m_TargetIntensity = clamped;
                m_TransitionDuration = 0f;
                m_TransitionElapsed = 0f;
                m_IsTransitioning = false;
            }
            else
            {
                // 强度从当前值插值到目标值。
                m_StartIntensity = m_Intensity;
                m_TargetIntensity = clamped;
                m_TransitionDuration = transitionSeconds;
                m_TransitionElapsed = 0f;
                // 若起止强度一致则没有可插值的量，视为非过渡。
                m_IsTransitioning = !FloatEquals(m_StartIntensity, m_TargetIntensity);
                if (!m_IsTransitioning)
                {
                    m_Intensity = clamped;
                }
            }

            if (typeChanged && OnWeatherChanged != null)
            {
                OnWeatherChanged(this, m_Current);
            }
        }

        /// <summary>
        /// 为「从 <paramref name="from"/> 出发」的转移表登记一条加权目标。
        /// 多次为同一 (from,to) 调用会追加多条（权重叠加由抽样自然处理）。
        /// 非正权重会被忽略。
        /// </summary>
        /// <param name="from">起始天气。</param>
        /// <param name="to">可能转移到的天气。</param>
        /// <param name="weight">权重，必须为正才生效。</param>
        public void DefineTransition(WeatherType from, WeatherType to, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            List<Transition> list;
            if (!m_Transitions.TryGetValue(from, out list))
            {
                list = new List<Transition>();
                m_Transitions[from] = list;
            }

            list.Add(new Transition { To = to, Weight = weight });
        }

        /// <summary>
        /// 依据 <see cref="Current"/> 的加权转移表，用随机源挑选下一种天气并以
        /// <paramref name="transitionSeconds"/> 过渡到强度 1。
        /// 若当前天气没有任何已登记的转移，则为安全的空操作（不改变状态、不触发事件）。
        /// </summary>
        /// <param name="transitionSeconds">过渡到新天气的时长（秒）。</param>
        public void RollNext(float transitionSeconds)
        {
            List<Transition> list;
            if (!m_Transitions.TryGetValue(m_Current, out list) || list.Count == 0)
            {
                // 当前天气未定义任何转移：安全空操作。
                return;
            }

            float total = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                total += list[i].Weight;
            }

            if (total <= 0f)
            {
                return;
            }

            // 随机源返回 [0,1)，映射到 [0,total) 后做加权区间命中。
            double roll = m_RandomProvider();
            if (roll < 0d)
            {
                roll = 0d;
            }
            else if (roll >= 1d)
            {
                // 容忍越界随机源，钳到接近 1 以仍落在最后一个区间内。
                roll = 0.9999999d;
            }

            float target = (float)(roll * total);
            float cursor = 0f;
            WeatherType picked = list[list.Count - 1].To; // 浮点兜底：默认最后一项。
            for (int i = 0; i < list.Count; i++)
            {
                cursor += list[i].Weight;
                if (target < cursor)
                {
                    picked = list[i].To;
                    break;
                }
            }

            SetWeather(picked, 1f, transitionSeconds);
        }

        /// <summary>
        /// 框架优先级。无特殊先后要求，取默认 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架每帧轮询。把 <paramref name="elapseSeconds"/> 作为推进步长驱动强度过渡。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒），天气过渡不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Tick(elapseSeconds);
        }

        /// <summary>
        /// 关闭模块：清空事件订阅与转移表，并把天气复位为晴、满强度、非过渡。
        /// </summary>
        public override void Shutdown()
        {
            OnWeatherChanged = null;
            m_Transitions.Clear();

            m_Current = WeatherType.Clear;
            m_Target = WeatherType.Clear;
            m_Intensity = 1f;
            m_StartIntensity = 1f;
            m_TargetIntensity = 1f;
            m_TransitionDuration = 0f;
            m_TransitionElapsed = 0f;
            m_IsTransitioning = false;
        }

        /// <summary>
        /// 推进过渡插值。仅在过渡中生效；完成后把强度对齐到目标并结束过渡。
        /// 非正 deltaSeconds 直接忽略。
        /// </summary>
        /// <param name="deltaSeconds">本次推进的真实秒数。</param>
        public void Tick(float deltaSeconds)
        {
            if (!m_IsTransitioning || deltaSeconds <= 0f)
            {
                return;
            }

            m_TransitionElapsed += deltaSeconds;

            if (m_TransitionElapsed >= m_TransitionDuration)
            {
                // 过渡完成：对齐目标强度。
                m_Intensity = m_TargetIntensity;
                m_IsTransitioning = false;
                m_TransitionElapsed = m_TransitionDuration;
                return;
            }

            float t = m_TransitionElapsed / m_TransitionDuration;
            m_Intensity = m_StartIntensity + (m_TargetIntensity - m_StartIntensity) * t;
        }

        private static bool FloatEquals(float a, float b)
        {
            float diff = a - b;
            if (diff < 0f)
            {
                diff = -diff;
            }

            return diff <= 1e-6f;
        }
    }
}
