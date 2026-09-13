//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
#if EJOY_INPUTSYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace EjoyFramework.GamePlay.TouchInput
{
    /// <summary>
    /// 触摸输入驱动组件。每帧读取当前 Unity 输入后端的触摸（移动端）/ 鼠标（编辑器调试）输入，
    /// 映射为引擎无关的 <see cref="TouchSample"/> 喂入 <see cref="Recognizer"/>，再驱动其 Update。
    ///
    /// 安装新 Input System 时使用其 Touchscreen / Mouse；否则回退到旧版 Input。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchInputDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("是否在无触摸时使用鼠标作为回退输入（便于编辑器调试）。")]
        private bool m_EnableMouseFallback = true;

        private GestureRecognizer m_Recognizer;

        /// <summary>鼠标回退使用的虚拟手指标识（避免与真实触摸 fingerId 冲突）。</summary>
        private const int MouseFingerId = -1;

        private bool m_MouseDown;

        /// <summary>
        /// 本驱动持有的手势识别器。外部订阅其事件以接收手势。
        /// </summary>
        public GestureRecognizer Recognizer
        {
            get
            {
                if (m_Recognizer == null)
                {
                    m_Recognizer = new GestureRecognizer();
                }
                return m_Recognizer;
            }
        }

        private void Awake()
        {
            if (m_Recognizer == null)
            {
                m_Recognizer = new GestureRecognizer();
            }
        }

        private void Update()
        {
            ProcessInputFrame(Time.time);
        }

        /// <summary>
        /// Reads the active Unity input backend and advances gesture recognition for one frame.
        /// </summary>
        internal void ProcessInputFrame(float now)
        {
#if EJOY_INPUTSYSTEM
            bool hasTouch = FeedInputSystemTouches(now);
#else
            int touchCount = UnityEngine.Input.touchCount;
            bool hasTouch = touchCount > 0;
            if (touchCount > 0)
            {
                // 有真实触摸：逐个映射喂入。
                for (int i = 0; i < touchCount; i++)
                {
                    Touch touch = UnityEngine.Input.GetTouch(i);
                    TouchPhaseKind phase = MapPhase(touch.phase);
                    Recognizer.Feed(new TouchSample(
                        touch.fingerId,
                        touch.position.x,
                        touch.position.y,
                        phase,
                        now));
                }
            }
#endif
            if (!hasTouch && m_EnableMouseFallback)
            {
                // 无触摸：用鼠标左键模拟单指（仅用于编辑器 / 桌面调试）。
                FeedMouse(now);
            }

            Recognizer.Update(now);
        }

#if EJOY_INPUTSYSTEM
        private bool FeedInputSystemTouches(float now)
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            bool hasTouch = false;
            foreach (TouchControl touch in touchscreen.touches)
            {
                UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None)
                {
                    continue;
                }

                Vector2 position = touch.position.ReadValue();
                hasTouch = true;
                Recognizer.Feed(new TouchSample(
                    touch.touchId.ReadValue(),
                    position.x,
                    position.y,
                    MapPhase(phase),
                    now));
            }

            return hasTouch;
        }
#endif

        private void FeedMouse(float now)
        {
#if EJOY_INPUTSYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                m_MouseDown = false;
                return;
            }

            Vector2 mousePos = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                m_MouseDown = true;
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Began, now));
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                m_MouseDown = false;
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Ended, now));
            }
            else if (m_MouseDown && mouse.leftButton.isPressed)
            {
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Moved, now));
            }
#else
            Vector3 mousePos = UnityEngine.Input.mousePosition;

            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                m_MouseDown = true;
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Began, now));
            }
            else if (UnityEngine.Input.GetMouseButtonUp(0))
            {
                m_MouseDown = false;
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Ended, now));
            }
            else if (m_MouseDown && UnityEngine.Input.GetMouseButton(0))
            {
                Recognizer.Feed(new TouchSample(MouseFingerId, mousePos.x, mousePos.y, TouchPhaseKind.Moved, now));
            }
#endif
        }

#if EJOY_INPUTSYSTEM
        private static TouchPhaseKind MapPhase(UnityEngine.InputSystem.TouchPhase phase)
        {
            switch (phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    return TouchPhaseKind.Began;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    return TouchPhaseKind.Moved;
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    return TouchPhaseKind.Stationary;
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    return TouchPhaseKind.Ended;
                case UnityEngine.InputSystem.TouchPhase.Canceled:
                    return TouchPhaseKind.Canceled;
                default:
                    return TouchPhaseKind.Stationary;
            }
        }
#else
        private static TouchPhaseKind MapPhase(UnityEngine.TouchPhase phase)
        {
            switch (phase)
            {
                case UnityEngine.TouchPhase.Began:
                    return TouchPhaseKind.Began;
                case UnityEngine.TouchPhase.Moved:
                    return TouchPhaseKind.Moved;
                case UnityEngine.TouchPhase.Stationary:
                    return TouchPhaseKind.Stationary;
                case UnityEngine.TouchPhase.Ended:
                    return TouchPhaseKind.Ended;
                case UnityEngine.TouchPhase.Canceled:
                    return TouchPhaseKind.Canceled;
                default:
                    return TouchPhaseKind.Stationary;
            }
        }
#endif
    }
}
