using EjoyFramework.GamePlay.ThreeC;
using UnityEngine;
#if EJOY_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace EjoyFramework.GamePlay.Unity.ThreeC
{
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonInputSource : MonoBehaviour
    {
#if EJOY_INPUTSYSTEM
        [SerializeField] private InputActionReference m_Move;
        [SerializeField] private InputActionReference m_Look;
        [SerializeField] private InputActionReference m_Jump;
        [SerializeField] private InputActionReference m_Sprint;
        [SerializeField] private InputActionReference m_Zoom;
#endif
        private uint m_Sequence;
        public PlayerControlFrame Current { get; private set; }

        private void Update()
        {
            using (ThreeCPerformanceCounters.Input.Measure())
            {
                Vector2 move = Vector2.zero;
                Vector2 look = Vector2.zero;
                float zoom = 0f;
                bool jump = false;
                bool sprint = false;
#if EJOY_INPUTSYSTEM
                if (m_Move != null) move = m_Move.action.ReadValue<Vector2>();
                if (m_Look != null) look = m_Look.action.ReadValue<Vector2>();
                if (m_Zoom != null) zoom = m_Zoom.action.ReadValue<float>();
                jump = m_Jump != null && m_Jump.action.WasPressedThisFrame();
                sprint = m_Sprint != null && m_Sprint.action.IsPressed();

                if (m_Move == null)
                {
                    Keyboard keyboard = Keyboard.current;
                    Gamepad gamepad = Gamepad.current;
                    if (keyboard != null)
                    {
                        move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                        move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                        jump |= keyboard.spaceKey.wasPressedThisFrame;
                        sprint |= keyboard.leftShiftKey.isPressed;
                    }
                    if (gamepad != null)
                    {
                        Vector2 stick = gamepad.leftStick.ReadValue();
                        if (stick.sqrMagnitude > move.sqrMagnitude) move = stick;
                        look += gamepad.rightStick.ReadValue();
                        jump |= gamepad.buttonSouth.wasPressedThisFrame;
                        sprint |= gamepad.leftStickButton.isPressed;
                    }
                }
#endif
                m_Sequence++;
                Current = new PlayerControlFrame(
                    m_Sequence, move.x, move.y, look.x, look.y, zoom, jump, sprint);
            }
        }
    }
}
