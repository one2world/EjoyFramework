//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 手势识别阈值配置。坐标 / 距离单位为像素，时间单位为秒。
    /// </summary>
    public struct GestureConfig
    {
        /// <summary>单击最长按住时长（秒）。超过则不再视为 Tap。</summary>
        public float TapMaxDuration;

        /// <summary>单击允许的最大位移（像素）。超过则不再视为 Tap。</summary>
        public float TapMaxMovement;

        /// <summary>双击两次点击之间的最大时间间隔（秒）。</summary>
        public float DoubleTapMaxGap;

        /// <summary>长按触发所需的最短按住时长（秒）。</summary>
        public float LongPressDuration;

        /// <summary>判定为快速滑动所需的最小位移（像素）。</summary>
        public float SwipeMinDistance;

        /// <summary>
        /// 默认配置：Tap 0.3s / 20px，DoubleTap 间隔 0.3s，LongPress 0.6s，Swipe 60px。
        /// </summary>
        public static GestureConfig Default
        {
            get
            {
                return new GestureConfig
                {
                    TapMaxDuration = 0.3f,
                    TapMaxMovement = 20f,
                    DoubleTapMaxGap = 0.3f,
                    LongPressDuration = 0.6f,
                    SwipeMinDistance = 60f
                };
            }
        }

        /// <summary>
        /// 当传入的配置为零值（default）时返回默认配置，否则原样返回。
        /// 以 <see cref="LongPressDuration"/> 是否为 0 作为“未初始化”的判定依据。
        /// </summary>
        internal static GestureConfig OrDefault(GestureConfig config)
        {
            if (config.LongPressDuration <= 0f
                && config.TapMaxDuration <= 0f
                && config.SwipeMinDistance <= 0f)
            {
                return Default;
            }
            return config;
        }
    }
}
