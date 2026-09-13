//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 手势类型。
    /// </summary>
    public enum GestureKind
    {
        /// <summary>单击（短按快抬，几乎不移动）。</summary>
        Tap,

        /// <summary>双击（短时间内两次相近位置的单击）。</summary>
        DoubleTap,

        /// <summary>长按（按住超过阈值时长且不移动）。</summary>
        LongPress,

        /// <summary>快速滑动（位移超过阈值且抬手迅速）。</summary>
        Swipe,

        /// <summary>拖拽（单指按住移动过程中的持续事件）。</summary>
        Drag,

        /// <summary>捏合 / 张开（双指距离变化）。</summary>
        Pinch
    }

    /// <summary>
    /// 一次手势事件。不可变值类型。
    /// </summary>
    public readonly struct GestureEvent
    {
        private readonly GestureKind m_Kind;
        private readonly float m_X;
        private readonly float m_Y;
        private readonly float m_DeltaX;
        private readonly float m_DeltaY;
        private readonly float m_Magnitude;

        /// <summary>
        /// 构造手势事件。
        /// </summary>
        /// <param name="kind">手势类型。</param>
        /// <param name="x">焦点横坐标（像素）。</param>
        /// <param name="y">焦点纵坐标（像素）。</param>
        /// <param name="deltaX">横向位移（用于 Swipe / Drag）。</param>
        /// <param name="deltaY">纵向位移（用于 Swipe / Drag）。</param>
        /// <param name="magnitude">幅度：Swipe 为滑动距离；Pinch 为缩放比例（当前/上一帧距离）；Drag 为本次位移长度。</param>
        public GestureEvent(GestureKind kind, float x, float y, float deltaX, float deltaY, float magnitude)
        {
            m_Kind = kind;
            m_X = x;
            m_Y = y;
            m_DeltaX = deltaX;
            m_DeltaY = deltaY;
            m_Magnitude = magnitude;
        }

        /// <summary>手势类型。</summary>
        public GestureKind Kind
        {
            get { return m_Kind; }
        }

        /// <summary>焦点横坐标（像素）。双指手势为两指中点。</summary>
        public float X
        {
            get { return m_X; }
        }

        /// <summary>焦点纵坐标（像素）。双指手势为两指中点。</summary>
        public float Y
        {
            get { return m_Y; }
        }

        /// <summary>横向位移（Swipe / Drag）。</summary>
        public float DeltaX
        {
            get { return m_DeltaX; }
        }

        /// <summary>纵向位移（Swipe / Drag）。</summary>
        public float DeltaY
        {
            get { return m_DeltaY; }
        }

        /// <summary>
        /// 幅度。Swipe 为滑动距离（像素）；Pinch 为缩放比例
        /// （currentDistance / previousDistance，&gt;1 表示张开，&lt;1 表示捏合）；
        /// Drag 为本次移动的位移长度（像素）。
        /// </summary>
        public float Magnitude
        {
            get { return m_Magnitude; }
        }
    }
}
// 触发 Unity 重新导入此脚本（修复 MCP 断连期间 GestureEvent 的 MonoScript 注册竞态）。
