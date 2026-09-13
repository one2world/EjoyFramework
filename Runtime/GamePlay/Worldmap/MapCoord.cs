//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Worldmap
{
    /// <summary>
    /// 二维地图坐标（不可变值类型）。
    /// 语义上为世界坐标的平面投影：俯视地图中 X 通常对应世界 X，Y 通常对应世界 Z。
    /// 纯 C# 实现，不依赖 UnityEngine。
    /// </summary>
    public readonly struct MapCoord
    {
        private readonly float m_X;
        private readonly float m_Y;

        /// <summary>
        /// 构造地图坐标。
        /// </summary>
        /// <param name="x">X 分量。</param>
        /// <param name="y">Y 分量（俯视地图中通常为世界 Z）。</param>
        public MapCoord(float x, float y)
        {
            m_X = x;
            m_Y = y;
        }

        /// <summary>
        /// X 分量。
        /// </summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>
        /// Y 分量（俯视地图中通常为世界 Z）。
        /// </summary>
        public float Y
        {
            get { return m_Y; }
        }
    }
}
