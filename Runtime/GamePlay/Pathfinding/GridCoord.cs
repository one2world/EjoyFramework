//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Pathfinding
{
    /// <summary>
    /// 网格坐标：二维整数格子坐标 (X, Y)，用于 A* 网格寻路。
    /// 不可变值类型（readonly struct），用作字典/集合键时具有稳定的相等语义与哈希。
    ///
    /// 纯逻辑、引擎无关：不依赖 UnityEngine，仅依赖 System.*。
    /// 坐标系约定：X 向右、Y 向上（或向下，由调用方自行解释），本类型只关心整数索引，不关心朝向。
    /// </summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        private readonly int m_X;
        private readonly int m_Y;

        /// <summary>
        /// 构造一个网格坐标。
        /// </summary>
        /// <param name="x">X 坐标（列索引）。</param>
        /// <param name="y">Y 坐标（行索引）。</param>
        public GridCoord(int x, int y)
        {
            m_X = x;
            m_Y = y;
        }

        /// <summary>
        /// X 坐标（列索引）。
        /// </summary>
        public int X
        {
            get { return m_X; }
        }

        /// <summary>
        /// Y 坐标（行索引）。
        /// </summary>
        public int Y
        {
            get { return m_Y; }
        }

        /// <summary>
        /// 判断与另一坐标是否相等（X 与 Y 同时相等）。
        /// </summary>
        public bool Equals(GridCoord other)
        {
            return m_X == other.m_X && m_Y == other.m_Y;
        }

        /// <summary>
        /// 判断与任意对象是否相等。
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        /// <summary>
        /// 计算哈希值。采用经典的整数对混合（确定性、分布良好），便于作为字典/集合键。
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                // 经典 17/31 混合，对 (X, Y) 整数对给出稳定且分布良好的哈希。
                int hash = 17;
                hash = hash * 31 + m_X;
                hash = hash * 31 + m_Y;
                return hash;
            }
        }

        /// <summary>
        /// 返回形如 "(X, Y)" 的可读字符串。
        /// </summary>
        public override string ToString()
        {
            return string.Concat("(", m_X.ToString(), ", ", m_Y.ToString(), ")");
        }

        /// <summary>
        /// 相等运算符。
        /// </summary>
        public static bool operator ==(GridCoord a, GridCoord b)
        {
            return a.Equals(b);
        }

        /// <summary>
        /// 不等运算符。
        /// </summary>
        public static bool operator !=(GridCoord a, GridCoord b)
        {
            return !a.Equals(b);
        }
    }
}
