//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Units.Movement
{
    /// <summary>
    /// 一次 <see cref="IMover.Step(float, float, float)"/> 推进的结果快照。
    /// 值类型，便于在移动热路径上零分配返回。
    /// </summary>
    public readonly struct MoveStep
    {
        private readonly float m_X;
        private readonly float m_Y;
        private readonly bool m_Arrived;
        private readonly float m_DistanceMoved;

        /// <summary>
        /// 构造一次移动结果。
        /// </summary>
        /// <param name="x">推进后的新 X 坐标。</param>
        /// <param name="y">推进后的新 Y 坐标。</param>
        /// <param name="arrived">本步是否抵达最终目的地。</param>
        /// <param name="distanceMoved">本步实际移动的直线距离。</param>
        public MoveStep(float x, float y, bool arrived, float distanceMoved)
        {
            m_X = x;
            m_Y = y;
            m_Arrived = arrived;
            m_DistanceMoved = distanceMoved;
        }

        /// <summary>
        /// 推进后的新 X 坐标。
        /// </summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>
        /// 推进后的新 Y 坐标。
        /// </summary>
        public float Y
        {
            get { return m_Y; }
        }

        /// <summary>
        /// 本步是否抵达最终目的地（直线移动的终点，或路径移动的最后一个路径点）。
        /// </summary>
        public bool Arrived
        {
            get { return m_Arrived; }
        }

        /// <summary>
        /// 本步实际移动的直线距离（已抵达或无目的地时可能为 0）。
        /// </summary>
        public float DistanceMoved
        {
            get { return m_DistanceMoved; }
        }
    }
}
