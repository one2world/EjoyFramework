//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Guide
{
    /// <summary>
    /// 引导流程的状态。
    /// </summary>
    public enum GuideFlowStatus
    {
        /// <summary>
        /// 尚未开始（已注册但未运行，且未被标记为已完成）。
        /// </summary>
        NotStarted,

        /// <summary>
        /// 正在进行。同一时刻至多一个流程处于此状态。
        /// </summary>
        Running,

        /// <summary>
        /// 已完成（含正常走完或被跳过）。
        /// </summary>
        Completed,
    }
}
