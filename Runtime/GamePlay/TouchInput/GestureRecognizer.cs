//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 引擎无关的手势识别器框架模块。喂入抽象 <see cref="TouchSample"/>，识别
    /// 单击 / 双击 / 长按 / 滑动 / 拖拽 / 捏合，并通过事件回调输出。
    ///
    /// 使用方式：每当有触摸变化时调用 <see cref="Feed"/>；框架每帧轮询 <see cref="Update(float, float)"/>
    /// 时以累计时间驱动基于时间的判定（长按、双击窗口收尾）。也可显式调用 <see cref="Update(float)"/>
    /// 以绝对时间手动驱动（测试 / 离线回放）。
    ///
    /// 通过 <c>Framework.GetModule&lt;IGestureRecognizer&gt;()</c> 获取实例；无需任何外部配置即可工作。
    /// 不引用任何 UnityEngine 类型；坐标 / 距离单位为像素，时间单位为秒。
    /// </summary>
    public sealed class GestureRecognizer : FrameworkModule, IGestureRecognizer
    {
        /// <summary>单指拖拽触发的最小位移阈值（像素）。小于此值不视为拖拽，避免抖动误触。</summary>
        private const float DragThreshold = 8f;

        private GestureConfig m_Config;

        // 框架轮询累计时间（秒），用于把相对帧时长换算为基于时间判定所需的绝对时间。
        private float m_Now;

        // 单指追踪状态。
        private bool m_HasActiveFinger;
        private int m_ActiveFingerId;
        private float m_StartX;
        private float m_StartY;
        private float m_StartTime;
        private float m_LastX;
        private float m_LastY;
        private bool m_MovedBeyondTap;     // 是否已超出 Tap 允许的移动范围。
        private bool m_DragStarted;        // 是否已进入拖拽态（用于阈值判定）。
        private bool m_LongPressFired;     // 本次按住是否已触发过长按。

        // 双击候选：上一次成功的 Tap。
        private bool m_HasPendingTap;
        private float m_PendingTapX;
        private float m_PendingTapY;
        private float m_PendingTapTime;

        // 多指追踪（FingerId -> 最近位置）。用于捏合。
        private readonly Dictionary<int, FingerPoint> m_Fingers = new Dictionary<int, FingerPoint>();
        private bool m_PinchActive;
        private float m_PrevPinchDistance;

        // 双击近邻判定半径（像素）。复用 Tap 的移动阈值。
        private float DoubleTapMaxOffset
        {
            get { return m_Config.TapMaxMovement; }
        }

        /// <summary>
        /// 构造识别器（框架解析所需的无参构造）。默认使用 <see cref="GestureConfig.Default"/>，
        /// 可后续通过 <see cref="SetConfig"/> 注入自定义阈值。
        /// </summary>
        public GestureRecognizer()
        {
            m_Config = GestureConfig.Default;
        }

        /// <summary>
        /// 以指定配置构造识别器（测试 / 直接 new 的便捷构造）。
        /// 传入 <c>default</c> 配置时使用 <see cref="GestureConfig.Default"/>。
        /// </summary>
        /// <param name="config">阈值配置。</param>
        internal GestureRecognizer(GestureConfig config)
        {
            m_Config = GestureConfig.OrDefault(config);
        }

        /// <summary>
        /// 注入阈值配置（setter 注入）。传入 <c>default</c> 配置时回退到 <see cref="GestureConfig.Default"/>。
        /// 适用于通过 <c>Framework.GetModule</c> 取得模块后再覆盖默认阈值的场景。
        /// </summary>
        /// <param name="config">阈值配置。</param>
        public void SetConfig(GestureConfig config)
        {
            m_Config = GestureConfig.OrDefault(config);
        }

        /// <summary>当前生效的配置（已应用默认值回退）。</summary>
        public GestureConfig Config
        {
            get { return m_Config; }
        }

        /// <summary>单击事件。</summary>
        public event Action<GestureEvent> OnTap;

        /// <summary>双击事件（双击发生时仅触发本事件，不再额外触发第二次 <see cref="OnTap"/>）。</summary>
        public event Action<GestureEvent> OnDoubleTap;

        /// <summary>长按事件。在 <see cref="Update"/> 中按住超过阈值时长触发一次。</summary>
        public event Action<GestureEvent> OnLongPress;

        /// <summary>滑动事件。抬手时若位移超过阈值且耗时较短则触发。</summary>
        public event Action<GestureEvent> OnSwipe;

        /// <summary>拖拽事件。单指按住移动且超过小阈值时持续触发，Magnitude 为本次位移长度。</summary>
        public event Action<GestureEvent> OnDrag;

        /// <summary>捏合事件。双指移动时触发，Magnitude 为缩放比例（当前距离 / 上一帧距离）。</summary>
        public event Action<GestureEvent> OnPinch;

        /// <summary>
        /// 喂入一个触摸采样。应在每次触摸状态变化时调用。
        /// </summary>
        /// <param name="sample">触摸采样。</param>
        public void Feed(TouchSample sample)
        {
            // 将内部时钟前向同步到采样的绝对时间，使框架 Update(elapse) 路径下基于时间的判定
            // （长按触发、双击窗口过期）与以 sample.Time 戳记的 m_StartTime / m_PendingTapTime 同源。
            // 仅前向推进，避免乱序 / 迟到采样把时钟回拨。手动 Update(now) 路径不受影响（Tick 直接用传入 now）。
            if (sample.Time > m_Now)
            {
                m_Now = sample.Time;
            }

            switch (sample.Phase)
            {
                case TouchPhaseKind.Began:
                    HandleBegan(sample);
                    break;
                case TouchPhaseKind.Moved:
                case TouchPhaseKind.Stationary:
                    HandleMoved(sample);
                    break;
                case TouchPhaseKind.Ended:
                    HandleEnded(sample, canceled: false);
                    break;
                case TouchPhaseKind.Canceled:
                    HandleEnded(sample, canceled: true);
                    break;
            }
        }

        /// <summary>
        /// 框架模块优先级。手势识别为纯逻辑模块，使用默认优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 框架每帧轮询：以累计时间推进基于时间的判定（长按触发、双击窗口过期清理）。
        /// 把相对帧时长 <paramref name="elapseSeconds"/> 累加到内部 <c>m_Now</c>，再以绝对时间执行判定，
        /// 与 <see cref="Update(float)"/> 的手动驱动共用同一套逻辑。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（秒），本模块不使用。</param>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_Now += elapseSeconds;
            Tick(m_Now);
        }

        /// <summary>
        /// 以绝对时间手动驱动基于时间的判定（长按触发、双击窗口过期清理）。
        /// 用于测试 / 离线回放等需要精确控制时间轴的场景。
        /// </summary>
        /// <param name="now">当前时间（秒）。</param>
        public void Update(float now)
        {
            Tick(now);
        }

        /// <summary>
        /// 关闭模块：重置全部运行时状态并清零累计时间。
        /// </summary>
        public override void Shutdown()
        {
            Reset();
            m_Now = 0f;
        }

        /// <summary>
        /// 重置全部内部状态，丢弃所有进行中的触摸与双击候选。
        /// </summary>
        public void Reset()
        {
            m_HasActiveFinger = false;
            m_MovedBeyondTap = false;
            m_DragStarted = false;
            m_LongPressFired = false;
            m_HasPendingTap = false;
            m_Fingers.Clear();
            m_PinchActive = false;
            m_PrevPinchDistance = 0f;
        }

        // 基于时间的判定逻辑（长按、双击窗口过期），由两个 Update 重载共用。
        private void Tick(float now)
        {
            // 长按：单指按住、未移动、未触发过长按，且按住时长达到阈值。
            if (m_HasActiveFinger && !m_LongPressFired && !m_MovedBeyondTap && m_Fingers.Count == 1)
            {
                float held = now - m_StartTime;
                if (held >= m_Config.LongPressDuration)
                {
                    m_LongPressFired = true;
                    InvokeLongPress(m_LastX, m_LastY);
                }
            }

            // 双击窗口过期：若挂起的 Tap 超出双击间隔，落地为普通 Tap 已在抬手时发出，
            // 这里仅清理过期的双击候选，避免后续误判为双击。
            if (m_HasPendingTap && now - m_PendingTapTime > m_Config.DoubleTapMaxGap)
            {
                m_HasPendingTap = false;
            }
        }

        private void HandleBegan(TouchSample sample)
        {
            m_Fingers[sample.FingerId] = new FingerPoint(sample.X, sample.Y);

            if (!m_HasActiveFinger)
            {
                // 第一根手指：作为单指手势的主追踪对象。
                m_HasActiveFinger = true;
                m_ActiveFingerId = sample.FingerId;
                m_StartX = sample.X;
                m_StartY = sample.Y;
                m_StartTime = sample.Time;
                m_LastX = sample.X;
                m_LastY = sample.Y;
                m_MovedBeyondTap = false;
                m_DragStarted = false;
                m_LongPressFired = false;
            }

            // 第二根手指按下：进入捏合追踪态，记录初始双指距离。
            if (m_Fingers.Count >= 2)
            {
                m_PinchActive = true;
                m_PrevPinchDistance = CurrentPinchDistance();
            }
        }

        private void HandleMoved(TouchSample sample)
        {
            if (m_Fingers.ContainsKey(sample.FingerId))
            {
                m_Fingers[sample.FingerId] = new FingerPoint(sample.X, sample.Y);
            }

            // 双指：优先处理捏合。
            if (m_PinchActive && m_Fingers.Count >= 2)
            {
                HandlePinchMove();
                // 捏合期间不再派发单指拖拽，避免冲突。
                if (sample.FingerId == m_ActiveFingerId)
                {
                    m_LastX = sample.X;
                    m_LastY = sample.Y;
                }
                return;
            }

            // 单指：仅处理主追踪手指。
            if (!m_HasActiveFinger || sample.FingerId != m_ActiveFingerId)
            {
                return;
            }

            float totalDx = sample.X - m_StartX;
            float totalDy = sample.Y - m_StartY;
            float totalDist = Length(totalDx, totalDy);

            if (totalDist > m_Config.TapMaxMovement)
            {
                m_MovedBeyondTap = true;
            }

            // 拖拽：一旦累计位移超过小阈值即进入拖拽态，并按帧位移持续派发。
            if (m_DragStarted || totalDist > DragThreshold)
            {
                m_DragStarted = true;
                float frameDx = sample.X - m_LastX;
                float frameDy = sample.Y - m_LastY;
                float frameDist = Length(frameDx, frameDy);
                if (frameDist > 0f)
                {
                    InvokeDrag(sample.X, sample.Y, frameDx, frameDy, frameDist);
                }
            }

            m_LastX = sample.X;
            m_LastY = sample.Y;
        }

        private void HandleEnded(TouchSample sample, bool canceled)
        {
            bool wasActive = m_HasActiveFinger && sample.FingerId == m_ActiveFingerId;
            bool wasPinching = m_PinchActive;

            m_Fingers.Remove(sample.FingerId);

            // 双指→单指：退出捏合态。剩余手指不再立即续接单指手势（已被消费）。
            if (m_PinchActive && m_Fingers.Count < 2)
            {
                m_PinchActive = false;
                m_PrevPinchDistance = 0f;
            }

            if (!wasActive)
            {
                return;
            }

            // 主追踪手指抬起：结算单指手势。
            m_HasActiveFinger = false;

            if (canceled || m_LongPressFired || wasPinching)
            {
                // 取消、已长按、或抬手发生在捏合期间：不再产生 Tap / Swipe。
                m_HasPendingTap = false;
                return;
            }

            float dx = sample.X - m_StartX;
            float dy = sample.Y - m_StartY;
            float dist = Length(dx, dy);
            float duration = sample.Time - m_StartTime;

            // 滑动：位移超过阈值且抬手迅速（不超过单击最长时长的判定窗口放宽一倍）。
            if (dist >= m_Config.SwipeMinDistance && duration <= m_Config.TapMaxDuration * 2f)
            {
                InvokeSwipe(sample.X, sample.Y, dx, dy, dist);
                m_HasPendingTap = false;
                return;
            }

            // 单击 / 双击：短按、几乎不移动。
            if (duration <= m_Config.TapMaxDuration && dist <= m_Config.TapMaxMovement && !m_MovedBeyondTap)
            {
                if (m_HasPendingTap
                    && sample.Time - m_PendingTapTime <= m_Config.DoubleTapMaxGap
                    && Length(sample.X - m_PendingTapX, sample.Y - m_PendingTapY) <= DoubleTapMaxOffset)
                {
                    // 双击：发出 DoubleTap，并消费挂起的 Tap。
                    m_HasPendingTap = false;
                    InvokeDoubleTap(sample.X, sample.Y);
                }
                else
                {
                    // 单击：立即发出 Tap，并登记为双击候选。
                    InvokeTap(sample.X, sample.Y);
                    m_HasPendingTap = true;
                    m_PendingTapX = sample.X;
                    m_PendingTapY = sample.Y;
                    m_PendingTapTime = sample.Time;
                }
            }
            else
            {
                // 既非 Swipe 也非 Tap（如缓慢拖拽后抬手）：清理双击候选。
                m_HasPendingTap = false;
            }
        }

        private void HandlePinchMove()
        {
            float current = CurrentPinchDistance();
            if (m_PrevPinchDistance > 0f && current > 0f)
            {
                float ratio = current / m_PrevPinchDistance;
                FingerPoint center = PinchCenter();
                // Magnitude = 缩放比例（>1 张开，<1 捏合）。DeltaX 记录距离变化量。
                InvokePinch(center.X, center.Y, ratio, current - m_PrevPinchDistance);
            }
            m_PrevPinchDistance = current;
        }

        private float CurrentPinchDistance()
        {
            // 取前两根（按插入序的前两个键）手指计算距离。双指场景即为这两根。
            FingerPoint a = default;
            FingerPoint b = default;
            int idx = 0;
            foreach (KeyValuePair<int, FingerPoint> kv in m_Fingers)
            {
                if (idx == 0)
                {
                    a = kv.Value;
                }
                else if (idx == 1)
                {
                    b = kv.Value;
                    break;
                }
                idx++;
            }
            if (idx < 1)
            {
                return 0f;
            }
            return Length(b.X - a.X, b.Y - a.Y);
        }

        private FingerPoint PinchCenter()
        {
            FingerPoint a = default;
            FingerPoint b = default;
            int idx = 0;
            foreach (KeyValuePair<int, FingerPoint> kv in m_Fingers)
            {
                if (idx == 0)
                {
                    a = kv.Value;
                }
                else if (idx == 1)
                {
                    b = kv.Value;
                    break;
                }
                idx++;
            }
            return new FingerPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
        }

        private static float Length(float x, float y)
        {
            return (float)Math.Sqrt(x * x + y * y);
        }

        private void InvokeTap(float x, float y)
        {
            OnTap?.Invoke(new GestureEvent(GestureKind.Tap, x, y, 0f, 0f, 0f));
        }

        private void InvokeDoubleTap(float x, float y)
        {
            OnDoubleTap?.Invoke(new GestureEvent(GestureKind.DoubleTap, x, y, 0f, 0f, 0f));
        }

        private void InvokeLongPress(float x, float y)
        {
            OnLongPress?.Invoke(new GestureEvent(GestureKind.LongPress, x, y, 0f, 0f, 0f));
        }

        private void InvokeSwipe(float x, float y, float dx, float dy, float dist)
        {
            OnSwipe?.Invoke(new GestureEvent(GestureKind.Swipe, x, y, dx, dy, dist));
        }

        private void InvokeDrag(float x, float y, float dx, float dy, float dist)
        {
            OnDrag?.Invoke(new GestureEvent(GestureKind.Drag, x, y, dx, dy, dist));
        }

        private void InvokePinch(float x, float y, float ratio, float deltaDistance)
        {
            // DeltaX 复用为距离变化量，便于消费方按需取用；Magnitude 为缩放比例。
            OnPinch?.Invoke(new GestureEvent(GestureKind.Pinch, x, y, deltaDistance, 0f, ratio));
        }

        /// <summary>单根手指的最近位置（内部追踪用值类型）。</summary>
        private readonly struct FingerPoint
        {
            public readonly float X;
            public readonly float Y;

            public FingerPoint(float x, float y)
            {
                X = x;
                Y = y;
            }
        }
    }
}
