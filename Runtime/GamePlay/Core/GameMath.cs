//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay
{
    /// <summary>
    /// GamePlay 层共享的纯标量数学工具（引擎无关，不依赖 UnityEngine.Mathf）。
    /// 收敛各系统中重复的 <see cref="Clamp01"/> / <see cref="Lerp"/> 实现，保证数值行为一致且单一来源。
    /// </summary>
    public static class GameMath
    {
        /// <summary>
        /// 把值夹取到 [0,1]。注意：对 NaN 不做特殊处理（既不小于 0 也不大于 1），返回原值 NaN。
        /// </summary>
        /// <param name="value">待夹取的值。</param>
        /// <returns>落在 [0,1] 内的值。</returns>
        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        /// <summary>
        /// 在 <paramref name="a"/> 与 <paramref name="b"/> 之间按因子 <paramref name="t"/> 线性插值。
        /// 不对 <paramref name="t"/> 做夹取，<paramref name="t"/> 超出 [0,1] 时为外推。
        /// </summary>
        /// <param name="a">起点。</param>
        /// <param name="b">终点。</param>
        /// <param name="t">插值因子。</param>
        /// <returns>插值结果 <c>a + (b - a) * t</c>。</returns>
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
