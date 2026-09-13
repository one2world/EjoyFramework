//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Tweening
{
    /// <summary>
    /// 缓动动画的基本单元（非泛型基类，便于 <see cref="TweenManager"/> 与 <see cref="Sequence"/>
    /// 以同一列表持有不同类型的 Tween）。
    ///
    /// 时间模型：
    /// 1. <see cref="SetDelay"/> 设置的延迟先被消耗，期间不推进进度也不触发更新；
    /// 2. 延迟耗尽后，<see cref="Elapsed"/> 随 deltaTime 累加，归一化时间 t = Elapsed/Duration，裁剪到 [0,1]；
    /// 3. eased = <see cref="Easing.Evaluate"/>(Ease, t)，子类据此应用插值并触发 OnUpdate；
    /// 4. 到达 Duration 时按 <see cref="SetLoops"/> 处理循环；循环耗尽则标记完成并触发一次 OnComplete。
    ///
    /// Duration &lt;= 0 视为瞬时：首个有效 Tick 直接落到终点并完成。
    /// 单线程使用，非线程安全。
    /// </summary>
    public abstract class Tween
    {
        private float m_Duration;
        private float m_Elapsed;
        private float m_Delay;
        private float m_DelayRemaining;

        private int m_Loops = 1;        // 总循环次数；<0 表示无限循环。
        private int m_LoopsDone;        // 已完成的循环次数。
        private LoopType m_LoopType = LoopType.Restart;
        private bool m_Reversed;        // Yoyo 当前是否处于反向阶段。

        private Ease m_Ease = Ease.Linear;
        private bool m_IsComplete;
        private bool m_IsKilled;
        private bool m_CompleteFired;   // 防止 OnComplete 被重复触发。

        private Action m_OnComplete;
        private Action m_OnUpdate;

        /// <summary>
        /// 子类提供的动画时长（秒）。基类构造时通过 <see cref="Initialize"/> 写入。
        /// </summary>
        public float Duration
        {
            get { return m_Duration; }
        }

        /// <summary>
        /// 当前已推进的时间（秒），范围 [0, Duration]；循环时在每个循环内回绕。
        /// </summary>
        public float Elapsed
        {
            get { return m_Elapsed; }
        }

        /// <summary>
        /// 是否已正常完成（所有循环跑完）。被 Kill(false) 停止的 Tween 不算完成。
        /// </summary>
        public bool IsComplete
        {
            get { return m_IsComplete; }
        }

        /// <summary>
        /// 是否已被杀死（由 <see cref="Kill"/> 触发）。被杀死的 Tween 会被管理器移除。
        /// </summary>
        public bool IsKilled
        {
            get { return m_IsKilled; }
        }

        /// <summary>
        /// 当前缓动类型。
        /// </summary>
        public Ease Ease
        {
            get { return m_Ease; }
        }

        /// <summary>
        /// 当前 Yoyo 是否处于反向阶段（供子类计算方向）。
        /// </summary>
        protected bool Reversed
        {
            get { return m_Reversed; }
        }

        /// <summary>
        /// 由子类在构造时调用，写入时长。
        /// </summary>
        /// <param name="duration">动画时长（秒）。负值按 0 处理。</param>
        protected void Initialize(float duration)
        {
            m_Duration = duration < 0f ? 0f : duration;
        }

        /// <summary>
        /// 设置缓动类型，返回自身以支持链式调用。
        /// </summary>
        public Tween SetEase(Ease ease)
        {
            m_Ease = ease;
            return this;
        }

        /// <summary>
        /// 设置启动前的延迟（秒），返回自身以支持链式调用。负值按 0 处理。
        /// </summary>
        public Tween SetDelay(float delay)
        {
            m_Delay = delay < 0f ? 0f : delay;
            m_DelayRemaining = m_Delay;
            return this;
        }

        /// <summary>
        /// 设置循环次数与方式，返回自身以支持链式调用。
        /// </summary>
        /// <param name="loops">循环次数；&lt;0 表示无限循环；0 或 1 表示只播放一次。</param>
        /// <param name="loopType">循环方式（Restart / Yoyo）。</param>
        public Tween SetLoops(int loops, LoopType loopType = LoopType.Restart)
        {
            m_Loops = loops == 0 ? 1 : loops;
            m_LoopType = loopType;
            return this;
        }

        /// <summary>
        /// 注册完成回调（正常完成或 Kill(complete:true) 时触发一次）。
        /// </summary>
        public Tween OnComplete(Action callback)
        {
            m_OnComplete = callback;
            return this;
        }

        /// <summary>
        /// 注册每次进度更新后的回调。
        /// </summary>
        public Tween OnUpdate(Action callback)
        {
            m_OnUpdate = callback;
            return this;
        }

        /// <summary>
        /// 杀死该 Tween。
        /// </summary>
        /// <param name="complete">
        /// 为 true 时将进度快照到终点（应用终值）并触发 OnComplete；
        /// 为 false 时仅停止，不应用终值、不触发 OnComplete。
        /// </param>
        public void Kill(bool complete = false)
        {
            if (m_IsKilled)
            {
                return;
            }

            if (complete)
            {
                // 快照到终点：消除延迟，按终态应用一次进度，并标记完成。
                m_DelayRemaining = 0f;
                m_Elapsed = m_Duration;
                ApplySnapToEnd();
                InvokeUpdate();
                MarkComplete();
            }

            m_IsKilled = true;
        }

        /// <summary>
        /// 推进时间一帧。返回 true 表示本帧后该 Tween 已结束（完成或已被杀死）。
        /// </summary>
        /// <param name="deltaTime">本帧时间增量（秒）。负值按 0 处理。</param>
        public bool Tick(float deltaTime)
        {
            if (m_IsKilled || m_IsComplete)
            {
                return true;
            }

            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            // 先消耗延迟。
            if (m_DelayRemaining > 0f)
            {
                m_DelayRemaining -= deltaTime;
                if (m_DelayRemaining > 0f)
                {
                    return false;
                }

                // 延迟在本帧内耗尽，把溢出的时间转入实际推进。
                deltaTime = -m_DelayRemaining;
                m_DelayRemaining = 0f;
            }

            return Advance(deltaTime);
        }

        /// <summary>
        /// 在延迟耗尽后推进实际进度，处理循环与完成。返回 true 表示已结束。
        /// </summary>
        private bool Advance(float deltaTime)
        {
            // 瞬时 Tween：直接落到终点。
            if (m_Duration <= 0f)
            {
                m_Elapsed = 0f;
                ApplyProgressForCurrentLoop(1f);
                InvokeUpdate();
                return HandleLoopBoundary();
            }

            m_Elapsed += deltaTime;

            bool reachedEnd = m_Elapsed >= m_Duration;
            if (reachedEnd)
            {
                m_Elapsed = m_Duration;
            }

            float t = m_Elapsed / m_Duration;
            if (t < 0f)
            {
                t = 0f;
            }
            else if (t > 1f)
            {
                t = 1f;
            }

            ApplyProgressForCurrentLoop(t);
            InvokeUpdate();

            if (reachedEnd)
            {
                return HandleLoopBoundary();
            }

            return false;
        }

        /// <summary>
        /// 到达单次循环终点后的处理：递增循环计数；若仍需循环则重置进度（Yoyo 反向），
        /// 否则标记完成并触发回调。返回 true 表示整体已结束。
        /// </summary>
        private bool HandleLoopBoundary()
        {
            m_LoopsDone++;

            bool infinite = m_Loops < 0;
            bool moreLoops = infinite || m_LoopsDone < m_Loops;
            if (moreLoops)
            {
                m_Elapsed = 0f;
                if (m_LoopType == LoopType.Yoyo)
                {
                    m_Reversed = !m_Reversed;
                }
                OnLoopRestart();
                return false;
            }

            MarkComplete();
            return true;
        }

        /// <summary>
        /// 计算缓动进度并交由子类应用。Yoyo 反向阶段下，方向由 <see cref="Reversed"/> 决定。
        /// </summary>
        private void ApplyProgressForCurrentLoop(float normalizedTime)
        {
            float eased = Easing.Evaluate(m_Ease, normalizedTime);
            ApplyProgress(eased);
        }

        /// <summary>
        /// 标记完成并触发一次 OnComplete（幂等）。
        /// </summary>
        private void MarkComplete()
        {
            m_IsComplete = true;
            if (!m_CompleteFired)
            {
                m_CompleteFired = true;
                Action handler = m_OnComplete;
                if (handler != null)
                {
                    handler();
                }
            }
        }

        private void InvokeUpdate()
        {
            Action handler = m_OnUpdate;
            if (handler != null)
            {
                handler();
            }
        }

        /// <summary>
        /// 应用本次缓动进度。eased 通常落在 [0,1]（过冲曲线可能越界）。
        /// 子类据此插值并写入目标。需结合 <see cref="Reversed"/> 处理 Yoyo 方向。
        /// </summary>
        /// <param name="easedProgress">缓动后的进度。</param>
        protected abstract void ApplyProgress(float easedProgress);

        /// <summary>
        /// Kill(complete:true) 时将状态快照到终点。默认按 Reversed 决定终点方向后应用满进度。
        /// 子类可覆盖以实现更精确的终态。
        /// </summary>
        protected virtual void ApplySnapToEnd()
        {
            ApplyProgress(1f);
        }

        /// <summary>
        /// 循环边界回调：基类在「仍有剩余循环、且已重置 <see cref="Elapsed"/>」之后调用一次，
        /// 供子类复位自身的循环内状态（如 <see cref="Sequence"/> 复位本地时间轴并 Rewind 其子 Tween）。
        /// 默认空实现，非 Sequence 的子类无需关心。
        /// </summary>
        protected virtual void OnLoopRestart()
        {
        }

        /// <summary>
        /// 复位到起点以便重新播放：清空已推进时间 / 完成标记 / 循环计数 / Yoyo 方向，并恢复初始延迟。
        /// 不触发任何回调，也不复活已被 <see cref="Kill"/> 的 Tween。
        /// 供 <see cref="Sequence"/> 在自身循环时复位其子 Tween（同程序集 internal 访问）。
        /// </summary>
        internal void RewindForReplay()
        {
            if (m_IsKilled)
            {
                return;
            }

            m_Elapsed = 0f;
            m_DelayRemaining = m_Delay;
            m_LoopsDone = 0;
            m_Reversed = false;
            m_IsComplete = false;
            m_CompleteFired = false;
        }
    }
}
