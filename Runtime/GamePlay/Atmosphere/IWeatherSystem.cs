//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 天气系统模块的访问接口。供 <c>Framework.GetModule&lt;IWeatherSystem&gt;()</c> 解析，
    /// 暴露当前天气 / 强度的读取、手动切换、加权转移表登记与随机抽样。
    /// </summary>
    /// <remarks>
    /// 强度过渡每帧由框架的 Update 驱动。切换语义见实现类 <see cref="WeatherSystem"/>：
    /// 过渡开始即视为类型已切换，强度在过渡时长内线性插值。
    /// </remarks>
    public interface IWeatherSystem
    {
        /// <summary>
        /// 当前天气。过渡开始时即切换为新类型。
        /// </summary>
        WeatherType Current { get; }

        /// <summary>
        /// 过渡目标天气；未过渡时与 <see cref="Current"/> 相同。
        /// </summary>
        WeatherType Target { get; }

        /// <summary>
        /// 当前强度，落在 [0,1]。过渡期间从起始强度线性插值到目标强度。
        /// </summary>
        float Intensity { get; }

        /// <summary>
        /// 是否正处于强度过渡中。
        /// </summary>
        bool IsTransitioning { get; }

        /// <summary>
        /// 当 <see cref="Current"/> 真正发生变化时触发（即过渡开始那一刻），参数为本系统与新天气类型。
        /// 切到与当前相同的类型不会触发。
        /// </summary>
        event Action<WeatherSystem, WeatherType> OnWeatherChanged;

        /// <summary>
        /// 切换天气。<c>transitionSeconds&lt;=0</c> 瞬间生效，<c>&gt;0</c> 类型立即切换、强度在该时长内线性插值。
        /// 类型实际改变时触发一次 <see cref="OnWeatherChanged"/>。
        /// </summary>
        /// <param name="type">目标天气。</param>
        /// <param name="intensity">目标强度，自动夹取到 [0,1]。</param>
        /// <param name="transitionSeconds">过渡时长（秒），<=0 表示瞬间。</param>
        void SetWeather(WeatherType type, float intensity = 1f, float transitionSeconds = 0f);

        /// <summary>
        /// 为「从 <paramref name="from"/> 出发」的转移表登记一条加权目标。非正权重会被忽略。
        /// </summary>
        /// <param name="from">起始天气。</param>
        /// <param name="to">可能转移到的天气。</param>
        /// <param name="weight">权重，必须为正才生效。</param>
        void DefineTransition(WeatherType from, WeatherType to, float weight);

        /// <summary>
        /// 依据 <see cref="Current"/> 的加权转移表，用随机源挑选下一种天气并以
        /// <paramref name="transitionSeconds"/> 过渡到强度 1。当前天气无任何转移时为安全空操作。
        /// </summary>
        /// <param name="transitionSeconds">过渡到新天气的时长（秒）。</param>
        void RollNext(float transitionSeconds);
    }
}
