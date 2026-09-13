//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units.Movement
{
    /// <summary>
    /// 2D 移动平面上的一个路径点。值类型，便于在路径数组中紧凑存储且零额外分配。
    /// </summary>
    /// <remarks>
    /// 坐标使用引擎无关的 2D 平面 (X, Y)。Unity 层负责把世界水平面 (XZ) 映射到 (X, Y)：
    /// 即 <c>X = worldPos.x</c>、<c>Y = worldPos.z</c>。
    /// </remarks>
    public readonly struct Waypoint
    {
        private readonly float m_X;
        private readonly float m_Y;

        /// <summary>
        /// 构造一个路径点。
        /// </summary>
        /// <param name="x">2D 平面上的 X 坐标。</param>
        /// <param name="y">2D 平面上的 Y 坐标。</param>
        public Waypoint(float x, float y)
        {
            m_X = x;
            m_Y = y;
        }

        /// <summary>
        /// 2D 平面上的 X 坐标。
        /// </summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>
        /// 2D 平面上的 Y 坐标。
        /// </summary>
        public float Y
        {
            get { return m_Y; }
        }
    }
}
