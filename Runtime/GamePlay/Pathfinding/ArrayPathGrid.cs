//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Pathfinding
{
    /// <summary>
    /// 密集数组网格：用一维数组保存每个格子的可行走标记与进入代价，是 <see cref="IPathGrid"/> 的简单内置实现。
    /// 适用于塔防 / 策略 / RPG 等中小规模、可整图驻留内存的网格地图。
    ///
    /// 纯逻辑、引擎无关：不依赖 UnityEngine，仅依赖 System.*。单线程使用，非线程安全。
    ///
    /// 默认状态：所有格子可行走、进入代价为 1。
    /// 越界访问：<see cref="IsWalkable"/> 越界返回 false；<see cref="GetCost"/> 越界返回 1（安全默认值，调用方一般先经可行走判定）。
    /// </summary>
    public sealed class ArrayPathGrid : IPathGrid
    {
        private readonly int m_Width;
        private readonly int m_Height;
        private readonly bool[] m_Walkable;
        private readonly float[] m_Cost;

        /// <summary>
        /// 构造一个 width × height 的网格，所有格子默认可行走、进入代价为 1。
        /// </summary>
        /// <param name="width">网格宽度，必须 &gt;= 1。</param>
        /// <param name="height">网格高度，必须 &gt;= 1。</param>
        /// <exception cref="ArgumentOutOfRangeException">当 width 或 height 小于 1 时抛出。</exception>
        public ArrayPathGrid(int width, int height)
        {
            if (width < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Grid width must be >= 1.");
            }

            if (height < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Grid height must be >= 1.");
            }

            m_Width = width;
            m_Height = height;

            int count = width * height;
            m_Walkable = new bool[count];
            m_Cost = new float[count];
            for (int i = 0; i < count; i++)
            {
                m_Walkable[i] = true;
                m_Cost[i] = 1f;
            }
        }

        /// <summary>
        /// 网格宽度（X 方向格子数）。
        /// </summary>
        public int Width
        {
            get { return m_Width; }
        }

        /// <summary>
        /// 网格高度（Y 方向格子数）。
        /// </summary>
        public int Height
        {
            get { return m_Height; }
        }

        /// <summary>
        /// 设置格子 (x, y) 的可行走性。越界坐标静默忽略。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <param name="walkable">true 可行走，false 阻挡（墙）。</param>
        public void SetWalkable(int x, int y, bool walkable)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            m_Walkable[y * m_Width + x] = walkable;
        }

        /// <summary>
        /// 设置"进入"格子 (x, y) 的移动代价。传入值会被钳制到 &gt;= 1（小于 1 的值置 1），
        /// 以满足 <see cref="IPathGrid.GetCost"/> 的 &gt;= 1 约定，保证 A* 启发可采纳、路径最优。
        /// 越界坐标静默忽略。
        /// </summary>
        /// <param name="x">X 坐标。</param>
        /// <param name="y">Y 坐标。</param>
        /// <param name="cost">进入代价；小于 1 的值钳制为 1。</param>
        public void SetCost(int x, int y, float cost)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            if (cost < 1f)
            {
                cost = 1f;
            }

            m_Cost[y * m_Width + x] = cost;
        }

        /// <summary>
        /// 判断格子 (x, y) 是否可行走；越界返回 false。
        /// </summary>
        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return false;
            }

            return m_Walkable[y * m_Width + x];
        }

        /// <summary>
        /// 获取"进入"格子 (x, y) 的移动代价；越界返回 1（安全默认值）。
        /// </summary>
        public float GetCost(int x, int y)
        {
            if (!InBounds(x, y))
            {
                return 1f;
            }

            return m_Cost[y * m_Width + x];
        }

        private bool InBounds(int x, int y)
        {
            return x >= 0 && x < m_Width && y >= 0 && y < m_Height;
        }
    }
}
