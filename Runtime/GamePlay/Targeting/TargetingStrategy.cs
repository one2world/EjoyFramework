//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Targeting
{
    /// <summary>
    /// 目标选择策略。决定 <see cref="TargetSelector.TrySelect"/> 在合格候选中如何挑选“最佳”目标。
    /// </summary>
    public enum TargetingStrategy
    {
        /// <summary>
        /// 距离来源最近（比较与来源点的距离，取最小）。
        /// </summary>
        Nearest,

        /// <summary>
        /// 距离来源最远（比较与来源点的距离，取最大）。
        /// </summary>
        Farthest,

        /// <summary>
        /// 当前血量最低（按 <see cref="TargetInfo.Health"/> 取最小）。
        /// </summary>
        LowestHealth,

        /// <summary>
        /// 当前血量最高（按 <see cref="TargetInfo.Health"/> 取最大）。
        /// </summary>
        HighestHealth,

        /// <summary>
        /// 血量百分比最低（按 <see cref="TargetInfo.Health"/> / <see cref="TargetInfo.MaxHealth"/> 取最小）。
        /// </summary>
        LowestHealthPercent,

        /// <summary>
        /// 仇恨值最高（按 <see cref="TargetInfo.Threat"/> 取最大）。
        /// </summary>
        HighestThreat,

        /// <summary>
        /// 优先级最高（按 <see cref="TargetInfo.Priority"/> 取最大）。
        /// </summary>
        HighestPriority,

        /// <summary>
        /// 路径进度最靠前（按 <see cref="TargetInfo.Progress"/> 取最大）。
        /// 塔防约定：进度越大表示越接近终点，即“队首”目标。
        /// </summary>
        FirstInProgress,

        /// <summary>
        /// 路径进度最靠后（按 <see cref="TargetInfo.Progress"/> 取最小）。
        /// 塔防约定：进度越小表示刚出生不久，即“队尾”目标。
        /// </summary>
        LastInProgress
    }
}
