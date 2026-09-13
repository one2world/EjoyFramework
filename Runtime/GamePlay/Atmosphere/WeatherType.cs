//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 天气类型。由 <see cref="WeatherSystem"/> 在类型间做加权随机切换。
    /// 具体的视觉表现（粒子、雾、天空盒）由游戏层根据类型与强度自行接入。
    /// </summary>
    public enum WeatherType
    {
        /// <summary>晴。</summary>
        Clear,

        /// <summary>多云。</summary>
        Cloudy,

        /// <summary>雨。</summary>
        Rain,

        /// <summary>暴风雨。</summary>
        Storm,

        /// <summary>雪。</summary>
        Snow,

        /// <summary>雾。</summary>
        Fog,
    }
}
