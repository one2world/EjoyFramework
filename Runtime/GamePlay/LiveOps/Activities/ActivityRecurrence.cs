//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Activities
{
    /// <summary>
    /// 限时活动的重复（周期）类型。
    /// </summary>
    public enum ActivityRecurrence
    {
        /// <summary>
        /// 一次性活动：由绝对的开始/结束 UTC 时间戳界定，结束后永久 Ended。
        /// </summary>
        Once,

        /// <summary>
        /// 每日活动：在每天（UTC）的某个时间窗内开放，永不进入 Ended（窗外为 NotStarted）。
        /// </summary>
        Daily,

        /// <summary>
        /// 每周活动：仅在掩码命中的星期几（UTC）的每日时间窗内开放，永不进入 Ended。
        /// </summary>
        Weekly,
    }
}
