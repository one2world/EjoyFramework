//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 纯 float 缓动便捷类，适用于测试、音量淡入淡出等无需注入 lerp 的场景。
    /// 内部使用线性插值；缓动曲线仍由 <see cref="Tween.SetEase"/> 控制。
    /// </summary>
    public sealed class FloatTween : Tween
    {
        private readonly float m_From;
        private readonly float m_To;
        private readonly Action<float> m_Setter;
        private float m_Value;

        /// <summary>
        /// 构造 float 缓动。
        /// </summary>
        /// <param name="from">起点值。</param>
        /// <param name="to">终点值。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <param name="setter">可选的写回回调，每帧用当前值调用。</param>
        public FloatTween(float from, float to, float duration, Action<float> setter = null)
        {
            m_From = from;
            m_To = to;
            m_Setter = setter;
            m_Value = from;
            Initialize(duration);
        }

        /// <summary>
        /// 最近一次计算得到的当前值。
        /// </summary>
        public float Value
        {
            get { return m_Value; }
        }

        /// <summary>
        /// 应用缓动进度：Yoyo 反向阶段使用 (1 - progress) 实现 to→from。
        /// </summary>
        protected override void ApplyProgress(float easedProgress)
        {
            float p = Reversed ? 1f - easedProgress : easedProgress;
            m_Value = m_From + (m_To - m_From) * p;
            if (m_Setter != null)
            {
                m_Setter(m_Value);
            }
        }
    }
}
