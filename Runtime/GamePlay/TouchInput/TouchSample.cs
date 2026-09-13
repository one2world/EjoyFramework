//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 抽象触摸阶段。独立于 UnityEngine.TouchPhase，使核心保持引擎无关。
    /// 由 Unity 层在采样时映射进来。
    /// </summary>
    public enum TouchPhaseKind
    {
        /// <summary>手指刚按下（一次触摸的开始）。</summary>
        Began,

        /// <summary>手指按下且本帧发生了移动。</summary>
        Moved,

        /// <summary>手指按下但本帧位置未变化。</summary>
        Stationary,

        /// <summary>手指正常抬起（一次触摸的结束）。</summary>
        Ended,

        /// <summary>触摸被系统取消（如来电、超出触摸区域等）。</summary>
        Canceled
    }

    /// <summary>
    /// 一个抽象触摸采样。坐标单位为像素，<see cref="Time"/> 单位为秒。
    /// 不可变值类型；由 Unity 层从真实触摸/鼠标输入填充并喂入识别器。
    /// </summary>
    public readonly struct TouchSample
    {
        private readonly int m_FingerId;
        private readonly float m_X;
        private readonly float m_Y;
        private readonly TouchPhaseKind m_Phase;
        private readonly float m_Time;

        /// <summary>
        /// 构造一个触摸采样。
        /// </summary>
        /// <param name="fingerId">手指 / 指针标识（同一次触摸序列内保持稳定）。</param>
        /// <param name="x">横坐标（像素）。</param>
        /// <param name="y">纵坐标（像素）。</param>
        /// <param name="phase">触摸阶段。</param>
        /// <param name="time">采样时间（秒）。</param>
        public TouchSample(int fingerId, float x, float y, TouchPhaseKind phase, float time)
        {
            m_FingerId = fingerId;
            m_X = x;
            m_Y = y;
            m_Phase = phase;
            m_Time = time;
        }

        /// <summary>手指 / 指针标识。</summary>
        public int FingerId
        {
            get { return m_FingerId; }
        }

        /// <summary>横坐标（像素）。</summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>纵坐标（像素）。</summary>
        public float Y
        {
            get { return m_Y; }
        }

        /// <summary>触摸阶段。</summary>
        public TouchPhaseKind Phase
        {
            get { return m_Phase; }
        }

        /// <summary>采样时间（秒）。</summary>
        public float Time
        {
            get { return m_Time; }
        }
    }
}
