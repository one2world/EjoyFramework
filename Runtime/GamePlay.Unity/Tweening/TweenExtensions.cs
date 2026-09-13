//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// Transform 缓动扩展。每个方法在创建时读取当前值作为起点（from），
    /// 构造 <see cref="ValueTween{T}"/> 并注册到 <see cref="TweenRunner.Active"/>，返回 Tween 以支持链式配置。
    /// 注入 Unity 的 Lerp 函数，使核心保持引擎无关。
    /// </summary>
    public static class TweenExtensions
    {
        /// <summary>
        /// 在世界空间将 Transform 的位置缓动到目标点。
        /// </summary>
        /// <param name="target">目标 Transform，不可为空。</param>
        /// <param name="to">目标世界坐标。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoMove(this Transform target, Vector3 to, float duration)
        {
            Vector3 from = target.position;
            var tween = new ValueTween<Vector3>(
                from, to, duration,
                Vector3.Lerp,
                value => { if (target != null) target.position = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }

        /// <summary>
        /// 在父空间将 Transform 的局部位置缓动到目标点。
        /// </summary>
        /// <param name="target">目标 Transform，不可为空。</param>
        /// <param name="to">目标局部坐标。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoLocalMove(this Transform target, Vector3 to, float duration)
        {
            Vector3 from = target.localPosition;
            var tween = new ValueTween<Vector3>(
                from, to, duration,
                Vector3.Lerp,
                value => { if (target != null) target.localPosition = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }

        /// <summary>
        /// 将 Transform 的局部缩放缓动到目标值。
        /// </summary>
        /// <param name="target">目标 Transform，不可为空。</param>
        /// <param name="to">目标局部缩放。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoScale(this Transform target, Vector3 to, float duration)
        {
            Vector3 from = target.localScale;
            var tween = new ValueTween<Vector3>(
                from, to, duration,
                Vector3.Lerp,
                value => { if (target != null) target.localScale = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }

        /// <summary>
        /// 将 Transform 的局部欧拉角缓动到目标值（按分量线性插值）。
        /// </summary>
        /// <param name="target">目标 Transform，不可为空。</param>
        /// <param name="eulerTo">目标局部欧拉角（度）。</param>
        /// <param name="duration">时长（秒）。</param>
        /// <returns>已注册的 Tween。</returns>
        public static Tween DoLocalRotate(this Transform target, Vector3 eulerTo, float duration)
        {
            Vector3 from = target.localEulerAngles;
            var tween = new ValueTween<Vector3>(
                from, eulerTo, duration,
                Vector3.Lerp,
                value => { if (target != null) target.localEulerAngles = value; });
            TweenRunner.Active.Add(tween);
            return tween;
        }
    }
}
