//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Worldmap
{
    /// <summary>
    /// 地图坐标数学工具。提供距离计算与世界坐标 ↔ 归一化坐标互转。
    /// 纯 C# 实现，不依赖 UnityEngine。
    /// </summary>
    public static class MapMath
    {
        /// <summary>
        /// 两点欧氏距离。
        /// </summary>
        /// <param name="a">点 A。</param>
        /// <param name="b">点 B。</param>
        /// <returns>欧氏距离。</returns>
        public static float Distance(MapCoord a, MapCoord b)
        {
            return (float)Math.Sqrt(SqrDistance(a, b));
        }

        /// <summary>
        /// 两点距离的平方（避免开方，适合做距离比较）。
        /// </summary>
        /// <param name="a">点 A。</param>
        /// <param name="b">点 B。</param>
        /// <returns>距离的平方。</returns>
        public static float SqrDistance(MapCoord a, MapCoord b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 将世界坐标 (X,Y) 映射到 [0,1]×[0,1] 的归一化坐标。
        /// 不做钳制（结果可能越界），调用方如需限制范围请自行钳制。
        /// 当某一轴上 min==max 时（区间为零，无法归一化），该轴返回 0 以避免除零。
        /// </summary>
        /// <param name="world">待映射的世界坐标。</param>
        /// <param name="worldMin">世界范围最小角。</param>
        /// <param name="worldMax">世界范围最大角。</param>
        /// <returns>归一化坐标。</returns>
        public static MapCoord WorldToNormalized(MapCoord world, MapCoord worldMin, MapCoord worldMax)
        {
            float rangeX = worldMax.X - worldMin.X;
            float rangeY = worldMax.Y - worldMin.Y;
            float nx = rangeX != 0f ? (world.X - worldMin.X) / rangeX : 0f;
            float ny = rangeY != 0f ? (world.Y - worldMin.Y) / rangeY : 0f;
            return new MapCoord(nx, ny);
        }

        /// <summary>
        /// 将 [0,1]×[0,1] 的归一化坐标还原到世界坐标。
        /// 为 <see cref="WorldToNormalized"/> 的逆运算（在 min != max 时严格互逆）。
        /// </summary>
        /// <param name="normalized">归一化坐标。</param>
        /// <param name="worldMin">世界范围最小角。</param>
        /// <param name="worldMax">世界范围最大角。</param>
        /// <returns>世界坐标。</returns>
        public static MapCoord NormalizedToWorld(MapCoord normalized, MapCoord worldMin, MapCoord worldMax)
        {
            float wx = worldMin.X + normalized.X * (worldMax.X - worldMin.X);
            float wy = worldMin.Y + normalized.Y * (worldMax.Y - worldMin.Y);
            return new MapCoord(wx, wy);
        }
    }
}
