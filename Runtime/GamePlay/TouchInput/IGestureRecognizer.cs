//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 引擎无关的手势识别器模块接口。喂入抽象 <see cref="TouchSample"/>，识别
    /// 单击 / 双击 / 长按 / 滑动 / 拖拽 / 捏合，并通过事件回调输出。
    ///
    /// 通过 <c>Framework.GetModule&lt;IGestureRecognizer&gt;()</c> 获取实例；
    /// 模块每帧由框架驱动，内部以累计时间推进基于时间的判定（长按、双击窗口收尾）。
    /// 也可显式调用 <see cref="Update(float)"/> 以绝对时间手动驱动（如测试 / 离线回放）。
    ///
    /// 不引用任何 UnityEngine 类型；坐标 / 距离单位为像素，时间单位为秒。
    /// </summary>
    public interface IGestureRecognizer
    {
        /// <summary>当前生效的配置（已应用默认值回退）。</summary>
        GestureConfig Config { get; }

        /// <summary>单击事件。</summary>
        event Action<GestureEvent> OnTap;

        /// <summary>双击事件（双击发生时仅触发本事件，不再额外触发第二次 <see cref="OnTap"/>）。</summary>
        event Action<GestureEvent> OnDoubleTap;

        /// <summary>长按事件。按住超过阈值时长时触发一次。</summary>
        event Action<GestureEvent> OnLongPress;

        /// <summary>滑动事件。抬手时若位移超过阈值且耗时较短则触发。</summary>
        event Action<GestureEvent> OnSwipe;

        /// <summary>拖拽事件。单指按住移动且超过小阈值时持续触发，Magnitude 为本次位移长度。</summary>
        event Action<GestureEvent> OnDrag;

        /// <summary>捏合事件。双指移动时触发，Magnitude 为缩放比例（当前距离 / 上一帧距离）。</summary>
        event Action<GestureEvent> OnPinch;

        /// <summary>
        /// 喂入一个触摸采样。应在每次触摸状态变化时调用。
        /// </summary>
        /// <param name="sample">触摸采样。</param>
        void Feed(TouchSample sample);

        /// <summary>
        /// 以绝对时间手动驱动基于时间的判定（长按触发、双击窗口过期清理）。
        /// 框架每帧轮询时会以累计时间自动调用同样的逻辑；此重载用于测试 / 离线回放等显式驱动场景。
        /// </summary>
        /// <param name="now">当前时间（秒）。</param>
        void Update(float now);

        /// <summary>
        /// 重置全部内部状态，丢弃所有进行中的触摸与双击候选。
        /// </summary>
        void Reset();
    }
}
