//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Atmosphere
{
    /// <summary>
    /// 一天中的时段分期。由 <see cref="TimeOfDayCycle"/> 根据当天归一化进度推导。
    /// 归一化区间（默认映射）：
    /// 夜晚 [0,0.21)、黎明 [0.21,0.29)、白昼 [0.29,0.71)、黄昏 [0.71,0.79)、夜晚 [0.79,1)。
    /// </summary>
    public enum DayPhase
    {
        /// <summary>夜晚。</summary>
        Night,

        /// <summary>黎明（日出过渡）。</summary>
        Dawn,

        /// <summary>白昼。</summary>
        Day,

        /// <summary>黄昏（日落过渡）。</summary>
        Dusk,
    }
}
