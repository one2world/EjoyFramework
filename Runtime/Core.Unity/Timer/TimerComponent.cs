//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.Timer;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 定时器组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/Timer")]
    public sealed class TimerComponent : GameFrameworkComponent
    {
        private ITimerManager m_TimerManager;

        protected override void Awake()
        {
            base.Awake();
            m_TimerManager = Framework.GetModule<ITimerManager>();
            if (m_TimerManager == null)
            {
                Log.Fatal("Timer manager is invalid.");
                return;
            }
        }

        /// <summary>
        /// 当前活动（未结束、含暂停态）的定时器数量。
        /// </summary>
        public int ActiveTimerCount
        {
            get { return m_TimerManager.ActiveTimerCount; }
        }

        /// <summary>
        /// 增加一个一次性定时器，延时结束后触发一次。
        /// </summary>
        public int AddTimer(float delaySeconds, Action onComplete, bool useUnscaledTime = false)
        {
            return m_TimerManager.AddTimer(delaySeconds, onComplete, useUnscaledTime);
        }

        /// <summary>
        /// 增加一个重复定时器，每隔指定间隔触发一次（repeatCount&lt;0 表示无限重复）。
        /// </summary>
        public int AddRepeatingTimer(float intervalSeconds, Action onTick, int repeatCount = -1, bool useUnscaledTime = false)
        {
            return m_TimerManager.AddRepeatingTimer(intervalSeconds, onTick, repeatCount, useUnscaledTime);
        }

        /// <summary>
        /// 增加一个帧定时器，在经过指定帧数后触发一次。
        /// </summary>
        public int AddFrameTimer(int frames, Action onComplete)
        {
            return m_TimerManager.AddFrameTimer(frames, onComplete);
        }

        /// <summary>
        /// 移除（取消）指定定时器；不存在或已结束返回 false。
        /// </summary>
        public bool RemoveTimer(int timerId)
        {
            return m_TimerManager.RemoveTimer(timerId);
        }

        /// <summary>
        /// 暂停指定定时器。
        /// </summary>
        public void PauseTimer(int timerId)
        {
            m_TimerManager.PauseTimer(timerId);
        }

        /// <summary>
        /// 恢复指定定时器。
        /// </summary>
        public void ResumeTimer(int timerId)
        {
            m_TimerManager.ResumeTimer(timerId);
        }

        /// <summary>
        /// 指定定时器是否处于活动状态。
        /// </summary>
        public bool IsTimerActive(int timerId)
        {
            return m_TimerManager.IsTimerActive(timerId);
        }

        /// <summary>
        /// 移除所有定时器。
        /// </summary>
        public void RemoveAllTimers()
        {
            m_TimerManager.RemoveAllTimers();
        }
    }
}
