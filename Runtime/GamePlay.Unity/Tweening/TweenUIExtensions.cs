//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// UI 相关缓动扩展（CanvasGroup 透明度、Graphic 颜色 / 透明度）。
    /// 与 <see cref="TweenExtensions"/> 同样在创建时读取起点、注册到 <see cref="TweenRunner.Active"/>。
    /// </summary>
    public static class TweenUIExtensions
    {
        /// <summary>
        /// 将 CanvasGroup 的透明度缓动到目标值。
        /// </summary>
        /// <param name="target">目标 CanvasGroup，不可为空。</param>
        /// <param name="to">目标 alpha（0~1）。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoFade(this CanvasGroup target, float to, float duration)
        {
            float from = target.alpha;
            var tween = new FloatTween(
                from, to, duration,
                value => { if (target != null) target.alpha = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }

        /// <summary>
        /// 将 Graphic（Image / Text 等）的颜色缓动到目标值。
        /// </summary>
        /// <param name="target">目标 Graphic，不可为空。</param>
        /// <param name="to">目标颜色。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoColor(this Graphic target, Color to, float duration)
        {
            Color from = target.color;
            var tween = new ValueTween<Color>(
                from, to, duration,
                Color.Lerp,
                value => { if (target != null) target.color = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }

        /// <summary>
        /// 将 Graphic 的透明度（仅 alpha 通道）缓动到目标值。
        /// </summary>
        /// <param name="target">目标 Graphic，不可为空。</param>
        /// <param name="alpha">目标 alpha（0~1）。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoFade(this Graphic target, float alpha, float duration)
        {
            float from = target.color.a;
            var tween = new FloatTween(
                from, alpha, duration,
                value =>
                {
                    if (target != null)
                    {
                        Color c = target.color;
                        c.a = value;
                        target.color = c;
                    }
                });
            TweenRunner.Active.Add(tween);
            return tween;
        }
    }
}
