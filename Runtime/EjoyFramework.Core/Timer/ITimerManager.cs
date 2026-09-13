//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Timer
{
    /// <summary>
    /// 定时器管理器接口。把"延时/重复/帧后回调"集中托管，由框架 Update 统一驱动，
    /// 业务无需各自维护 Coroutine/计时器即可获得可暂停、可取消、可查询的定时调度。
    ///
    /// 使用：
    ///   int id = manager.AddTimer(2f, OnTimeout);                       // 2 秒后触发一次
    ///   int rid = manager.AddRepeatingTimer(0.5f, OnTick, repeatCount: 3); // 每 0.5 秒触发，共 3 次
    ///   int fid = manager.AddFrameTimer(10, OnTenFrames);               // 10 帧后触发一次
    ///   manager.RemoveTimer(id);
    /// </summary>
    public interface ITimerManager
    {
        /// <summary>
        /// 当前活动（未结束、含暂停态）的定时器数量。
        /// </summary>
        int ActiveTimerCount { get; }

        /// <summary>
        /// 增加一个一次性定时器，延时结束后触发一次。
        /// </summary>
        /// <param name="delaySeconds">延时秒数（&lt;=0 表示下一次 Update 即触发）。</param>
        /// <param name="onComplete">到时回调。</param>
        /// <param name="useUnscaledTime">是否使用不受 Time.timeScale 影响的真实时间。</param>
        /// <returns>定时器句柄 id（&gt;0 有效；0 表示参数无效未派发）。</returns>
        int AddTimer(float delaySeconds, Action onComplete, bool useUnscaledTime = false);

        /// <summary>
        /// 增加一个重复定时器，每隔指定间隔触发一次。
        /// </summary>
        /// <param name="intervalSeconds">触发间隔秒数。</param>
        /// <param name="onTick">每次触发回调。</param>
        /// <param name="repeatCount">触发次数（&lt;0 表示无限重复）。</param>
        /// <param name="useUnscaledTime">是否使用不受 Time.timeScale 影响的真实时间。</param>
        /// <returns>定时器句柄 id（&gt;0 有效；0 表示参数无效未派发）。</returns>
        int AddRepeatingTimer(float intervalSeconds, Action onTick, int repeatCount = -1, bool useUnscaledTime = false);

        /// <summary>
        /// 增加一个帧定时器，在经过指定帧数后触发一次（每次 Update 递减 1 帧）。
        /// </summary>
        /// <param name="frames">帧数（&lt;=0 表示下一次 Update 即触发）。</param>
        /// <param name="onComplete">到帧回调。</param>
        /// <returns>定时器句柄 id（&gt;0 有效；0 表示参数无效未派发）。</returns>
        int AddFrameTimer(int frames, Action onComplete);

        /// <summary>
        /// 移除（取消）指定定时器；不存在或已结束返回 false。
        /// </summary>
        bool RemoveTimer(int timerId);

        /// <summary>
        /// 暂停指定定时器（暂停期间不推进、不触发）。
        /// </summary>
        void PauseTimer(int timerId);

        /// <summary>
        /// 恢复指定定时器。
        /// </summary>
        void ResumeTimer(int timerId);

        /// <summary>
        /// 指定定时器是否处于活动状态（存在且未结束；暂停态仍视为活动）。
        /// </summary>
        bool IsTimerActive(int timerId);

        /// <summary>
        /// 移除所有定时器。
        /// </summary>
        void RemoveAllTimers();
    }
}
