//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Pathfinding
{
    /// <summary>
    /// 可寻路网格抽象：A* 寻路器（<see cref="AStarPathfinder"/>）通过该接口查询网格的尺寸、
    /// 可行走性与进入代价，而无需关心底层数据如何存储（密集数组、分块、瓦片地图等）。
    ///
    /// 纯逻辑、引擎无关：不依赖 UnityEngine，仅依赖 System.*。
    ///
    /// 实现约定：
    /// - <see cref="IsWalkable"/> 必须对越界坐标返回 false（寻路器据此判定边界，无需自行裁剪）。
    /// - <see cref="GetCost"/> 返回"进入"该格子的移动代价，应 &gt;= 1（用于保证 A* 启发可采纳/路径非负）。
    /// </summary>
    public interface IPathGrid
    {
        /// <summary>
        /// 网格宽度（X 方向格子数）。
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 网格高度（Y 方向格子数）。
        /// </summary>
        int Height { get; }

        /// <summary>
        /// 判断格子 (x, y) 是否可行走。越界坐标必须返回 false（视为阻挡）。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <returns>可行走返回 true；阻挡（墙）或越界返回 false。</returns>
        bool IsWalkable(int x, int y);

        /// <summary>
        /// 获取"进入"格子 (x, y) 的移动代价（应 &gt;= 1）。
        /// 直行进入的 g 代价为该值；对角进入的 g 代价约为 1.414 × 该值（见 <see cref="AStarPathfinder"/>）。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <returns>进入该格子的移动代价。</returns>
        float GetCost(int x, int y);
    }
}
