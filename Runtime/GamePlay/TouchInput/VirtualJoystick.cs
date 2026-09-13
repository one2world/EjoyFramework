//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 引擎无关的虚拟摇杆框架模块。以像素坐标驱动，输出归一化方向与幅度。
    ///
    /// 通过 <c>Framework.GetModule&lt;IVirtualJoystick&gt;()</c> 获取实例；无需任何外部配置即可工作
    /// （默认半径 100px、动态圆心），可通过 <see cref="SetRadius"/> / <see cref="SetDynamicCenter"/> 覆盖。
    /// 动态圆心模式（dynamicCenter = true）：每次 <see cref="Press"/> 将圆心设在按下点；
    /// 固定圆心模式（false）：同样以首次 <see cref="Press"/> 点为圆心（保持最简实现）。
    /// 两种模式下 <see cref="Drag"/> 都以圆心到当前点的向量、按 <see cref="Radius"/> 钳制后归一化。
    /// </summary>
    public sealed class VirtualJoystick : FrameworkModule, IVirtualJoystick
    {
        /// <summary>无参构造时使用的默认半径（像素）。</summary>
        private const float DefaultRadius = 100f;

        private float m_Radius;
        private bool m_DynamicCenter;

        private bool m_IsActive;
        private float m_CenterX;
        private float m_CenterY;
        private float m_DirectionX;
        private float m_DirectionY;
        private float m_Magnitude;
        private bool m_HasFixedCenter;

        /// <summary>
        /// 构造虚拟摇杆（框架解析所需的无参构造）。默认半径 <see cref="DefaultRadius"/>、动态圆心，
        /// 可后续通过 <see cref="SetRadius"/> / <see cref="SetDynamicCenter"/> 覆盖。
        /// </summary>
        public VirtualJoystick()
        {
            m_Radius = DefaultRadius;
            m_DynamicCenter = true;
        }

        /// <summary>
        /// 以指定参数构造虚拟摇杆（测试 / 直接 new 的便捷构造）。
        /// </summary>
        /// <param name="radius">摇杆半径（像素），须为正值。</param>
        /// <param name="dynamicCenter">是否在每次按下时重设圆心，默认 true。</param>
        internal VirtualJoystick(float radius, bool dynamicCenter = true)
        {
            m_Radius = radius > 0f ? radius : 1f;
            m_DynamicCenter = dynamicCenter;
        }

        /// <summary>
        /// 注入摇杆半径（setter 注入）。非正值回退为 1f。
        /// </summary>
        /// <param name="radius">摇杆半径（像素）。</param>
        public void SetRadius(float radius)
        {
            m_Radius = radius > 0f ? radius : 1f;
        }

        /// <summary>
        /// 注入圆心模式（setter 注入）。
        /// </summary>
        /// <param name="dynamicCenter">是否在每次按下时重设圆心。</param>
        public void SetDynamicCenter(bool dynamicCenter)
        {
            m_DynamicCenter = dynamicCenter;
        }

        /// <summary>摇杆半径（像素）。</summary>
        public float Radius
        {
            get { return m_Radius; }
        }

        /// <summary>当前是否处于激活态（已按下未释放）。</summary>
        public bool IsActive
        {
            get { return m_IsActive; }
        }

        /// <summary>归一化横向方向，范围 [-1, 1]。</summary>
        public float DirectionX
        {
            get { return m_DirectionX; }
        }

        /// <summary>归一化纵向方向，范围 [-1, 1]。</summary>
        public float DirectionY
        {
            get { return m_DirectionY; }
        }

        /// <summary>归一化幅度，范围 [0, 1]（钳制后的距离 / 半径）。</summary>
        public float Magnitude
        {
            get { return m_Magnitude; }
        }

        /// <summary>当前圆心横坐标（像素）。</summary>
        public float CenterX
        {
            get { return m_CenterX; }
        }

        /// <summary>当前圆心纵坐标（像素）。</summary>
        public float CenterY
        {
            get { return m_CenterY; }
        }

        /// <summary>
        /// 显式设置固定圆心。用于固定圆心模式下预先指定摇杆中心；
        /// 设置后即便 dynamicCenter 为 false，按下也将围绕此圆心计算。
        /// </summary>
        /// <param name="x">圆心横坐标（像素）。</param>
        /// <param name="y">圆心纵坐标（像素）。</param>
        public void SetFixedCenter(float x, float y)
        {
            m_CenterX = x;
            m_CenterY = y;
            m_HasFixedCenter = true;
        }

        /// <summary>
        /// 开始操作。动态模式下将圆心设在按下点；固定模式下若已通过
        /// <see cref="SetFixedCenter"/> 指定圆心则沿用，否则以首次按下点为圆心。
        /// </summary>
        /// <param name="x">按下点横坐标（像素）。</param>
        /// <param name="y">按下点纵坐标（像素）。</param>
        public void Press(float x, float y)
        {
            if (m_DynamicCenter || !m_HasFixedCenter)
            {
                m_CenterX = x;
                m_CenterY = y;
                m_HasFixedCenter = true;
            }

            m_IsActive = true;
            // 按下即按当前点更新一次方向（动态模式下方向为零，固定模式下可能已偏移）。
            UpdateDirection(x, y);
        }

        /// <summary>
        /// 拖动更新。以圆心到 (x, y) 的向量、按半径钳制后计算归一化方向与幅度。
        /// 未激活时忽略。
        /// </summary>
        /// <param name="x">当前点横坐标（像素）。</param>
        /// <param name="y">当前点纵坐标（像素）。</param>
        public void Drag(float x, float y)
        {
            if (!m_IsActive)
            {
                return;
            }
            UpdateDirection(x, y);
        }

        /// <summary>
        /// 释放摇杆：归零方向与幅度，并转为非激活态。
        /// </summary>
        public void Release()
        {
            m_IsActive = false;
            m_DirectionX = 0f;
            m_DirectionY = 0f;
            m_Magnitude = 0f;
        }

        /// <summary>
        /// 框架模块优先级。虚拟摇杆为纯逻辑模块，使用默认优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架每帧轮询。虚拟摇杆完全由 <see cref="Press"/> / <see cref="Drag"/> / <see cref="Release"/>
        /// 事件驱动，无每帧工作，故为空实现。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒），本模块不使用。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒），本模块不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭模块：归零方向 / 幅度 / 圆心并转为非激活态，清除已设固定圆心标记。
        /// </summary>
        public override void Shutdown()
        {
            m_IsActive = false;
            m_DirectionX = 0f;
            m_DirectionY = 0f;
            m_Magnitude = 0f;
            m_CenterX = 0f;
            m_CenterY = 0f;
            m_HasFixedCenter = false;
        }

        private void UpdateDirection(float x, float y)
        {
            float dx = x - m_CenterX;
            float dy = y - m_CenterY;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

            if (dist <= 1e-6f)
            {
                m_DirectionX = 0f;
                m_DirectionY = 0f;
                m_Magnitude = 0f;
                return;
            }

            float clamped = dist > m_Radius ? m_Radius : dist;
            m_Magnitude = clamped / m_Radius;

            // 方向始终为单位向量；幅度单独表达推进量。
            m_DirectionX = dx / dist;
            m_DirectionY = dy / dist;
        }
    }
}
