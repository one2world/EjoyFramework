//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 缓动函数求值。所有函数输入归一化时间 t ∈ [0,1]，输出缓动后的进度。
    /// 纯 C# 实现，不依赖 UnityEngine；常量参考 Robert Penner / DOTween 的通用实现。
    /// </summary>
    public static class Easing
    {
        // OutBack 回弹超调系数（经典值 1.70158）。
        private const float BackC1 = 1.70158f;
        private const float BackC3 = BackC1 + 1f;

        // OutElastic 振荡周期系数。
        private const float ElasticC4 = (2f * (float)Math.PI) / 3f;

        // OutBounce 分段参数。
        private const float BounceN1 = 7.5625f;
        private const float BounceD1 = 2.75f;

        /// <summary>
        /// 按指定缓动类型对归一化时间求值。
        /// </summary>
        /// <param name="ease">缓动类型。</param>
        /// <param name="t">归一化时间，调用方应保证落在 [0,1]；越界时由各分支自然延拓。</param>
        /// <returns>缓动后的进度（OutBack/OutElastic 过程中可能越过 [0,1]）。</returns>
        public static float Evaluate(Ease ease, float t)
        {
            switch (ease)
            {
                case Ease.Linear:
                    return t;

                case Ease.InQuad:
                    return t * t;
                case Ease.OutQuad:
                    return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad:
                    return t < 0.5f
                        ? 2f * t * t
                        : 1f - Pow2(-2f * t + 2f) / 2f;

                case Ease.InCubic:
                    return t * t * t;
                case Ease.OutCubic:
                    return 1f - Pow3(1f - t);
                case Ease.InOutCubic:
                    return t < 0.5f
                        ? 4f * t * t * t
                        : 1f - Pow3(-2f * t + 2f) / 2f;

                case Ease.InSine:
                    return 1f - (float)Math.Cos((t * Math.PI) / 2.0);
                case Ease.OutSine:
                    return (float)Math.Sin((t * Math.PI) / 2.0);
                case Ease.InOutSine:
                    return -(float)(Math.Cos(Math.PI * t) - 1.0) / 2f;

                case Ease.InExpo:
                    return t <= 0f ? 0f : (float)Math.Pow(2.0, 10.0 * t - 10.0);
                case Ease.OutExpo:
                    return t >= 1f ? 1f : 1f - (float)Math.Pow(2.0, -10.0 * t);
                case Ease.InOutExpo:
                    if (t <= 0f)
                    {
                        return 0f;
                    }
                    if (t >= 1f)
                    {
                        return 1f;
                    }
                    return t < 0.5f
                        ? (float)Math.Pow(2.0, 20.0 * t - 10.0) / 2f
                        : (2f - (float)Math.Pow(2.0, -20.0 * t + 10.0)) / 2f;

                case Ease.OutBack:
                {
                    float p = t - 1f;
                    return 1f + BackC3 * Pow3(p) + BackC1 * Pow2(p);
                }

                case Ease.OutElastic:
                    if (t <= 0f)
                    {
                        return 0f;
                    }
                    if (t >= 1f)
                    {
                        return 1f;
                    }
                    return (float)(Math.Pow(2.0, -10.0 * t) * Math.Sin((t * 10.0 - 0.75) * ElasticC4)) + 1f;

                case Ease.OutBounce:
                    return OutBounce(t);

                default:
                    return t;
            }
        }

        private static float OutBounce(float t)
        {
            if (t < 1f / BounceD1)
            {
                return BounceN1 * t * t;
            }
            if (t < 2f / BounceD1)
            {
                t -= 1.5f / BounceD1;
                return BounceN1 * t * t + 0.75f;
            }
            if (t < 2.5f / BounceD1)
            {
                t -= 2.25f / BounceD1;
                return BounceN1 * t * t + 0.9375f;
            }

            t -= 2.625f / BounceD1;
            return BounceN1 * t * t + 0.984375f;
        }

        private static float Pow2(float v)
        {
            return v * v;
        }

        private static float Pow3(float v)
        {
            return v * v * v;
        }
    }
}
