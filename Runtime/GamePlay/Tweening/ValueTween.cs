//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 泛型数值缓动。插值函数 <paramref name="lerp"/> 由外部注入，从而让核心保持引擎无关
    /// （Unity 层注入 Vector3.Lerp / Color.Lerp / Mathf.Lerp 等）。
    /// 起点 from 在构造时固定；每帧将插值结果通过 setter 写回目标。
    /// </summary>
    /// <typeparam name="T">被插值的数值类型。</typeparam>
    public sealed class ValueTween<T> : Tween
    {
        private readonly T m_From;
        private readonly T m_To;
        private readonly Func<T, T, float, T> m_Lerp;
        private readonly Action<T> m_Setter;
        private T m_CurrentValue;

        /// <summary>
        /// 构造数值缓动。
        /// </summary>
        /// <param name="from">起点值。</param>
        /// <param name="to">终点值。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <param name="lerp">插值函数 (from, to, t) =&gt; value，不可为空。</param>
        /// <param name="setter">将插值结果写回目标的回调，不可为空。</param>
        /// <exception cref="ArgumentNullException">当 lerp 或 setter 为空时抛出。</exception>
        public ValueTween(T from, T to, float duration, Func<T, T, float, T> lerp, Action<T> setter)
        {
            if (lerp == null)
            {
                throw new ArgumentNullException(nameof(lerp));
            }
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }

            m_From = from;
            m_To = to;
            m_Lerp = lerp;
            m_Setter = setter;
            m_CurrentValue = from;
            Initialize(duration);
        }

        /// <summary>
        /// 最近一次应用的插值结果。
        /// </summary>
        public T CurrentValue
        {
            get { return m_CurrentValue; }
        }

        /// <summary>
        /// 应用缓动进度：Yoyo 反向阶段使用 (1 - progress) 实现 to→from。
        /// </summary>
        protected override void ApplyProgress(float easedProgress)
        {
            float p = Reversed ? 1f - easedProgress : easedProgress;
            m_CurrentValue = m_Lerp(m_From, m_To, p);
            m_Setter(m_CurrentValue);
        }
    }
}
