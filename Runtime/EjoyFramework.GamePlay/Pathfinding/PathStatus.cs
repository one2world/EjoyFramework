//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Pathfinding
{
    /// <summary>
    /// 寻路结果状态，用于区分"找到路径""无路可达""端点非法"三种情形。
    /// </summary>
    public enum PathStatus
    {
        /// <summary>
        /// 找到从起点到终点的可行路径，结果列表包含 start..goal 的完整格子序列（含两端）。
        /// </summary>
        Found = 0,

        /// <summary>
        /// 起点与终点均合法，但网格上不存在连通它们的可行路径（被墙完全隔断）。
        /// </summary>
        NoPath = 1,

        /// <summary>
        /// 端点非法：起点或终点越界，或落在不可行走的格子上。
        /// </summary>
        InvalidEndpoint = 2,
    }
}
