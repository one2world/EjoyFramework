//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Music
{
    /// <summary>
    /// 双通道交叉淡入淡出混音器（纯逻辑，引擎无关）。
    /// 维护两个逻辑通道 A、B：其中一个为“前台/活动”通道，另一个为“后台/进入”通道。
    /// <para>
    /// 交叉淡变：活动通道增益从 1 线性降到 0，进入通道增益从 0 线性升到 1，历时 duration 秒；
    /// 完成后交换活动通道并触发 <see cref="OnCrossfadeComplete"/>。
    /// </para>
    /// <para>
    /// <see cref="VolumeA"/> / <see cref="VolumeB"/> 为各通道最终上报增益（已乘以 <see cref="MasterVolume"/> 并钳制到 [0,1]），
    /// 供 Unity 层直接写入 AudioSource.volume。本类不持有任何音频对象。
    /// </para>
    /// 单线程使用，非线程安全。
    /// <para>
    /// 作为框架模块：通过 <c>Framework.GetModule&lt;ICrossfadeMixer&gt;()</c> 获取，需要无参构造函数。
    /// 每帧驱动经由 <see cref="Update"/>（内部转调 <see cref="Tick"/>）。
    /// </para>
    /// </summary>
    public sealed class CrossfadeMixer : FrameworkModule, ICrossfadeMixer
    {
        // 内部各通道的“原始”增益（未乘 Master），范围 [0,1]。
        private float m_RawA = 1f;
        private float m_RawB = 0f;

        // 当前活动通道：0 => A，1 => B。默认 A 为活动通道。
        private int m_ActiveChannel = 0;

        // 淡变进行中标记与计时。
        private bool m_IsFading = false;
        private float m_FadeDuration = 0f;
        private float m_FadeElapsed = 0f;

        // 本次淡变起止时各通道的原始增益（线性插值端点）。
        private float m_StartA, m_StartB;
        private float m_TargetA, m_TargetB;

        // 本次淡变完成后是否需要交换活动通道（仅 StartCrossfade 触发交换；FadeOut/FadeIn 不交换）。
        private bool m_SwapOnComplete = false;

        private float m_MasterVolume = 1f;

        /// <summary>
        /// 主音量，乘到两个通道的最终上报增益上。范围 [0,1]，写入时自动钳制。默认 1。
        /// </summary>
        public float MasterVolume
        {
            get { return m_MasterVolume; }
            set { m_MasterVolume = GameMath.Clamp01(value); }
        }

        /// <summary>
        /// 通道 A 的最终上报增益（已乘 <see cref="MasterVolume"/> 并钳制到 [0,1]）。
        /// </summary>
        public float VolumeA
        {
            get { return GameMath.Clamp01(m_RawA) * m_MasterVolume; }
        }

        /// <summary>
        /// 通道 B 的最终上报增益（已乘 <see cref="MasterVolume"/> 并钳制到 [0,1]）。
        /// </summary>
        public float VolumeB
        {
            get { return GameMath.Clamp01(m_RawB) * m_MasterVolume; }
        }

        /// <summary>
        /// 是否正在进行淡变（交叉淡变 / 淡出 / 淡入）。
        /// </summary>
        public bool IsCrossfading
        {
            get { return m_IsFading; }
        }

        /// <summary>
        /// 当前活动（前台）通道：0 => A，1 => B。
        /// </summary>
        public int ActiveChannel
        {
            get { return m_ActiveChannel; }
        }

        /// <summary>
        /// 交叉淡变完成时触发（仅 <see cref="StartCrossfade"/> 完成时触发，参数为本混音器自身）。
        /// </summary>
        public event Action<CrossfadeMixer> OnCrossfadeComplete;

        /// <summary>
        /// 模块优先级。无特殊顺序需求，返回 0。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架模块每帧轮询：以逻辑时间增量推进淡变。等价于调用 <see cref="Tick"/>(elapseSeconds)。
        /// </summary>
        /// <param name="elapseSeconds">逻辑时间增量（秒）。</param>
        /// <param name="realElapseSeconds">真实时间增量（秒），本模块不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            Tick(elapseSeconds);
        }

        /// <summary>
        /// 关闭并清理运行期状态：复位为初始静止态（A 活动且增益 1，B 为 0），停止淡变并清空完成事件订阅。
        /// </summary>
        public override void Shutdown()
        {
            m_RawA = 1f;
            m_RawB = 0f;
            m_ActiveChannel = 0;

            m_IsFading = false;
            m_FadeDuration = 0f;
            m_FadeElapsed = 0f;
            m_StartA = 0f;
            m_StartB = 0f;
            m_TargetA = 0f;
            m_TargetB = 0f;
            m_SwapOnComplete = false;

            m_MasterVolume = 1f;

            OnCrossfadeComplete = null;
        }

        /// <summary>
        /// 从当前活动通道交叉淡变到另一通道，历时 duration 秒。
        /// <para>活动通道 1→0，进入通道 0→1；完成后两者角色互换（进入通道成为新的活动通道）。</para>
        /// <para>duration &lt;= 0 视为瞬时切换：立即完成交换并触发完成事件。</para>
        /// </summary>
        /// <param name="duration">淡变时长（秒）。</param>
        public void StartCrossfade(float duration)
        {
            // 进入通道在淡变期间从 0 开始升起；活动通道从其当前值降到 0。
            if (m_ActiveChannel == 0)
            {
                BeginFade(duration, m_RawA, 0f, m_RawB, 1f, swapOnComplete: true);
            }
            else
            {
                BeginFade(duration, m_RawA, 1f, m_RawB, 0f, swapOnComplete: true);
            }
        }

        /// <summary>
        /// 将活动通道增益淡出到 0（用于停止音乐），历时 duration 秒。不交换活动通道。
        /// <para>duration &lt;= 0 视为瞬时。</para>
        /// </summary>
        /// <param name="duration">淡出时长（秒）。</param>
        public void FadeOut(float duration)
        {
            if (m_ActiveChannel == 0)
            {
                BeginFade(duration, m_RawA, 0f, m_RawB, m_RawB, swapOnComplete: false);
            }
            else
            {
                BeginFade(duration, m_RawA, m_RawA, m_RawB, 0f, swapOnComplete: false);
            }
        }

        /// <summary>
        /// 将活动通道增益从当前值淡入到 1，历时 duration 秒。不交换活动通道。
        /// <para>duration &lt;= 0 视为瞬时。</para>
        /// </summary>
        /// <param name="duration">淡入时长（秒）。</param>
        public void FadeInActive(float duration)
        {
            if (m_ActiveChannel == 0)
            {
                BeginFade(duration, m_RawA, 1f, m_RawB, m_RawB, swapOnComplete: false);
            }
            else
            {
                BeginFade(duration, m_RawA, m_RawA, m_RawB, 1f, swapOnComplete: false);
            }
        }

        /// <summary>
        /// 推进淡变。deltaTime &lt;= 0 时不做任何处理；未在淡变中时为空操作。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。</param>
        public void Tick(float deltaTime)
        {
            if (!m_IsFading || deltaTime <= 0f)
            {
                return;
            }

            m_FadeElapsed += deltaTime;
            float t = m_FadeDuration <= 0f ? 1f : GameMath.Clamp01(m_FadeElapsed / m_FadeDuration);

            m_RawA = GameMath.Lerp(m_StartA, m_TargetA, t);
            m_RawB = GameMath.Lerp(m_StartB, m_TargetB, t);

            if (t >= 1f)
            {
                CompleteFade();
            }
        }

        // 开始一次淡变：设置端点并立即对齐当前原始增益到起点。
        private void BeginFade(float duration, float startA, float targetA, float startB, float targetB, bool swapOnComplete)
        {
            m_StartA = GameMath.Clamp01(startA);
            m_TargetA = GameMath.Clamp01(targetA);
            m_StartB = GameMath.Clamp01(startB);
            m_TargetB = GameMath.Clamp01(targetB);

            m_RawA = m_StartA;
            m_RawB = m_StartB;

            m_FadeDuration = duration;
            m_FadeElapsed = 0f;
            m_SwapOnComplete = swapOnComplete;
            m_IsFading = true;

            // 瞬时（duration <= 0）：直接落到终点并完成。
            if (duration <= 0f)
            {
                m_RawA = m_TargetA;
                m_RawB = m_TargetB;
                CompleteFade();
            }
        }

        private void CompleteFade()
        {
            m_RawA = m_TargetA;
            m_RawB = m_TargetB;
            m_IsFading = false;

            if (m_SwapOnComplete)
            {
                m_ActiveChannel = m_ActiveChannel == 0 ? 1 : 0;
                RaiseComplete();
            }
        }

        private void RaiseComplete()
        {
            Action<CrossfadeMixer> handler = OnCrossfadeComplete;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
