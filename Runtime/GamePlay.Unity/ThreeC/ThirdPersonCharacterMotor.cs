using System;
using EjoyFramework.GamePlay.ThreeC;
using UnityEngine;

namespace EjoyFramework.GamePlay.Unity.ThreeC
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonCharacterMotor : MonoBehaviour
    {
        [SerializeField] private ThirdPersonInputSource m_Input;
        [SerializeField] private Transform m_CameraTransform;
        [SerializeField] private float m_WalkSpeed = 4f;
        [SerializeField] private float m_SprintSpeed = 7f;
        [SerializeField] private float m_Acceleration = 24f;
        [SerializeField] private float m_Deceleration = 30f;
        [SerializeField] private float m_JumpHeight = 1.2f;
        [SerializeField] private float m_Gravity = 25f;
        [SerializeField] private float m_TurnSpeed = 720f;

        private CharacterController m_Controller;
        private ThirdPersonMotor m_Motor;
        private CharacterControllerStepper m_Stepper;

        private void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Stepper = new CharacterControllerStepper(m_Controller);
            if (m_Input == null) m_Input = GetComponent<ThirdPersonInputSource>();
            if (m_CameraTransform == null && Camera.main != null) m_CameraTransform = Camera.main.transform;
            m_Motor = new ThirdPersonMotor(
                m_WalkSpeed, m_SprintSpeed, m_Acceleration, m_Deceleration, m_JumpHeight, m_Gravity);
        }

        private void Update()
        {
            if (m_Input == null || m_Motor == null) return;
            using (ThreeCPerformanceCounters.Motor.Measure())
            {
                PlayerControlFrame control = m_Input.Current;
                var feedback = new CharacterMotorFeedback(m_Controller.isGrounded);
                float cameraYaw = m_CameraTransform != null ? m_CameraTransform.eulerAngles.y : transform.eulerAngles.y;
                CharacterMotorStep step = m_Motor.Step(in control, in feedback, cameraYaw, Time.deltaTime);
                var displacement = new Vector3(step.Displacement.X, step.Displacement.Y, step.Displacement.Z);
                if (Time.deltaTime > 0f) m_Stepper.Move(transform.position, displacement, Time.deltaTime);
                if (step.Velocity.HorizontalSqrMagnitude > 0.0001f)
                {
                    Quaternion target = Quaternion.Euler(0f, step.FacingYaw, 0f);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, target, m_TurnSpeed * Time.deltaTime);
                }
            }
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) ||
                float.IsNaN(position.y) || float.IsInfinity(position.y) ||
                float.IsNaN(position.z) || float.IsInfinity(position.z))
                throw new ArgumentOutOfRangeException(nameof(position));
            // Pool preparation may teleport an inactive or disabled controller. Tick movement
            // still requires an active controller; preserve the caller's enabled/active state here.
            bool wasEnabled = m_Controller.enabled;
            m_Controller.enabled = false;
            try { transform.SetPositionAndRotation(position, rotation); }
            finally { m_Controller.enabled = wasEnabled; }
            Physics.SyncTransforms();
            m_Motor.Reset();
        }
    }
}
