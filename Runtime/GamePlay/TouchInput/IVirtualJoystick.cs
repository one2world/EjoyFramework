//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 引擎无关的虚拟摇杆模块接口。以像素坐标驱动，输出归一化方向与幅度。
    ///
    /// 通过 <c>Framework.GetModule&lt;IVirtualJoystick&gt;()</c> 获取实例。
    /// 动态圆心模式（dynamicCenter = true）：每次 <see cref="Press"/> 将圆心设在按下点；
    /// 固定圆心模式（false）：以 <see cref="SetFixedCenter"/> 指定的圆心或首次 <see cref="Press"/> 点为圆心。
    /// 两种模式下 <see cref="Drag"/> 都以圆心到当前点的向量、按 <see cref="Radius"/> 钳制后归一化。
    /// </summary>
    public interface IVirtualJoystick
    {
        /// <summary>摇杆半径（像素）。</summary>
        float Radius { get; }

        /// <summary>当前是否处于激活态（已按下未释放）。</summary>
        bool IsActive { get; }

        /// <summary>归一化横向方向，范围 [-1, 1]。</summary>
        float DirectionX { get; }

        /// <summary>归一化纵向方向，范围 [-1, 1]。</summary>
        float DirectionY { get; }

        /// <summary>归一化幅度，范围 [0, 1]（钳制后的距离 / 半径）。</summary>
        float Magnitude { get; }

        /// <summary>当前圆心横坐标（像素）。</summary>
        float CenterX { get; }

        /// <summary>当前圆心纵坐标（像素）。</summary>
        float CenterY { get; }

        /// <summary>
        /// 显式设置固定圆心。用于固定圆心模式下预先指定摇杆中心；
        /// 设置后即便 dynamicCenter 为 false，按下也将围绕此圆心计算。
        /// </summary>
        /// <param name="x">圆心横坐标（像素）。</param>
        /// <param name="y">圆心纵坐标（像素）。</param>
        void SetFixedCenter(float x, float y);

        /// <summary>
        /// 开始操作。动态模式下将圆心设在按下点；固定模式下若已通过
        /// <see cref="SetFixedCenter"/> 指定圆心则沿用，否则以首次按下点为圆心。
        /// </summary>
        /// <param name="x">按下点横坐标（像素）。</param>
        /// <param name="y">按下点纵坐标（像素）。</param>
        void Press(float x, float y);

        /// <summary>
        /// 拖动更新。以圆心到 (x, y) 的向量、按半径钳制后计算归一化方向与幅度。
        /// 未激活时忽略。
        /// </summary>
        /// <param name="x">当前点横坐标（像素）。</param>
        /// <param name="y">当前点纵坐标（像素）。</param>
        void Drag(float x, float y);

        /// <summary>
        /// 释放摇杆：归零方向与幅度，并转为非激活态。
        /// </summary>
        void Release();
    }
}
