//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 昼夜循环模块的访问接口。供 <c>Framework.GetModule&lt;ITimeOfDayCycle&gt;()</c> 解析，
    /// 暴露昼夜进度的读取、时间跳转、昼夜时长配置与时段/天数事件。
    /// </summary>
    /// <remarks>
    /// 每帧推进由框架的 Update 驱动（按 <c>elapseSeconds / DayLengthSeconds</c> 前进）。
    /// 时段映射与太阳角度映射见实现类 <see cref="TimeOfDayCycle"/> 的 remarks。
    /// </remarks>
    public interface ITimeOfDayCycle
    {
        /// <summary>
        /// 当天的归一化进度，落在 [0,1)。0=午夜，0.5=正午。
        /// </summary>
        float Normalized { get; }

        /// <summary>
        /// 当前一天中的小时数，落在 [0,24)。
        /// </summary>
        float HourOfDay { get; }

        /// <summary>
        /// 当前时段，由 <see cref="Normalized"/> 推导，在推进 / 跳转时刷新。
        /// </summary>
        DayPhase Phase { get; }

        /// <summary>
        /// 太阳角度（度），范围 [0,360)。午夜 270°、06:00 为 0°、正午 90°、18:00 为 180°。
        /// </summary>
        float SunAngleDegrees { get; }

        /// <summary>
        /// 累计经过的午夜次数（天数）。每跨过一次午夜递增。
        /// </summary>
        int DayCount { get; }

        /// <summary>
        /// 一个游戏内昼夜对应的真实秒数。赋值必须为正，否则抛异常。
        /// </summary>
        float DayLengthSeconds { get; set; }

        /// <summary>
        /// 当时段真正发生变化时触发，参数为本循环实例与新时段。
        /// </summary>
        event Action<TimeOfDayCycle, DayPhase> OnPhaseChanged;

        /// <summary>
        /// 当跨过午夜、天数递增时触发，参数为本循环实例与新的 <see cref="DayCount"/>。
        /// 单次推进跨多天时，每跨一天触发一次（不合并）。
        /// </summary>
        event Action<TimeOfDayCycle, int> OnNewDay;

        /// <summary>
        /// 直接跳转到指定归一化时间（落在 [0,1)，越界自动 wrap）。
        /// 刷新时段并在变化时触发 <see cref="OnPhaseChanged"/>，但<b>不</b>改变 <see cref="DayCount"/>，
        /// 也不会触发 <see cref="OnNewDay"/>。
        /// </summary>
        /// <param name="t">目标归一化时间。</param>
        void SetNormalized(float t);
    }
}
