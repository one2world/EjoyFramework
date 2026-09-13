using System;
using EjoyFramework.Core.Performance;

namespace EjoyFramework.GamePlay.ThreeC
{
    public static class ThreeCPerformanceCounters
    {
        public static readonly DurationPerformanceCounter Input =
            new DurationPerformanceCounter("EjoyFramework.ThreeC.Input");
        public static readonly DurationPerformanceCounter Motor =
            new DurationPerformanceCounter("EjoyFramework.ThreeC.Motor");
        public static readonly DurationPerformanceCounter Camera =
            new DurationPerformanceCounter("EjoyFramework.ThreeC.Camera");
        public static readonly ValuePerformanceCounter MotorSteps =
            new ValuePerformanceCounter("EjoyFramework.ThreeC.MotorSteps", "count");
    }

    public sealed class ThirdPersonMotor
    {
        private readonly float m_WalkSpeed;
        private readonly float m_SprintSpeed;
        private readonly float m_Acceleration;
        private readonly float m_Deceleration;
        private readonly float m_JumpHeight;
        private readonly float m_Gravity;
        private readonly float m_TerminalVelocity;
        private ThreeCVector3 m_Velocity;
        private float m_FacingYaw;

        public ThirdPersonMotor(
            float walkSpeed = 4f,
            float sprintSpeed = 7f,
            float acceleration = 24f,
            float deceleration = 30f,
            float jumpHeight = 1.2f,
            float gravity = 25f,
            float terminalVelocity = 50f)
        {
            m_WalkSpeed = Math.Max(0f, walkSpeed);
            m_SprintSpeed = Math.Max(m_WalkSpeed, sprintSpeed);
            m_Acceleration = Math.Max(0f, acceleration);
            m_Deceleration = Math.Max(0f, deceleration);
            m_JumpHeight = Math.Max(0f, jumpHeight);
            m_Gravity = Math.Max(0.01f, gravity);
            m_TerminalVelocity = Math.Max(0.01f, terminalVelocity);
        }

        public CharacterMotorStep Step(
            in PlayerControlFrame control,
            in CharacterMotorFeedback feedback,
            float cameraYawDegrees,
            float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return new CharacterMotorStep(in m_Velocity, in m_Velocity, m_FacingYaw);

            float inputX = control.MoveX;
            float inputY = control.MoveY;
            float magnitudeSquared = inputX * inputX + inputY * inputY;
            if (magnitudeSquared > 1f)
            {
                float inverse = 1f / (float)Math.Sqrt(magnitudeSquared);
                inputX *= inverse;
                inputY *= inverse;
            }

            float yawRadians = cameraYawDegrees * ((float)Math.PI / 180f);
            float sin = (float)Math.Sin(yawRadians);
            float cos = (float)Math.Cos(yawRadians);
            float worldX = inputX * cos + inputY * sin;
            float worldZ = -inputX * sin + inputY * cos;
            float speed = control.SprintHeld ? m_SprintSpeed : m_WalkSpeed;
            float desiredX = worldX * speed;
            float desiredZ = worldZ * speed;
            float rate = magnitudeSquared > 0.0001f ? m_Acceleration : m_Deceleration;
            float velocityX = MoveTowards(m_Velocity.X, desiredX, rate * deltaSeconds);
            float velocityZ = MoveTowards(m_Velocity.Z, desiredZ, rate * deltaSeconds);
            float velocityY = m_Velocity.Y;

            if (feedback.IsGrounded)
            {
                if (velocityY < 0f) velocityY = -2f;
                if (control.JumpPressed)
                    velocityY = (float)Math.Sqrt(2f * m_Gravity * m_JumpHeight);
            }
            else
            {
                velocityY = Math.Max(-m_TerminalVelocity, velocityY - m_Gravity * deltaSeconds);
            }

            if (worldX * worldX + worldZ * worldZ > 0.0001f)
                m_FacingYaw = (float)(Math.Atan2(worldX, worldZ) * 180.0 / Math.PI);

            m_Velocity = new ThreeCVector3(velocityX, velocityY, velocityZ);
            ThreeCVector3 displacement = m_Velocity * deltaSeconds;
            ThreeCPerformanceCounters.MotorSteps.Record(1);
            return new CharacterMotorStep(in m_Velocity, in displacement, m_FacingYaw);
        }

        public void Reset()
        {
            m_Velocity = default;
            m_FacingYaw = 0f;
        }

        private static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (Math.Abs(delta) <= maxDelta) return target;
            return current + Math.Sign(delta) * maxDelta;
        }
    }

    public sealed class OrbitCameraController
    {
        private readonly float m_LookSpeed;
        private readonly float m_ZoomSpeed;
        private readonly float m_MinPitch;
        private readonly float m_MaxPitch;
        private readonly float m_MinDistance;
        private readonly float m_MaxDistance;
        private OrbitCameraState m_State;

        public OrbitCameraController(
            float initialDistance = 5f,
            float lookSpeed = 180f,
            float zoomSpeed = 8f,
            float minPitch = -35f,
            float maxPitch = 75f,
            float minDistance = 1.5f,
            float maxDistance = 10f)
        {
            m_LookSpeed = Math.Max(0f, lookSpeed);
            m_ZoomSpeed = Math.Max(0f, zoomSpeed);
            m_MinPitch = Math.Min(minPitch, maxPitch);
            m_MaxPitch = Math.Max(minPitch, maxPitch);
            m_MinDistance = Math.Max(0.1f, Math.Min(minDistance, maxDistance));
            m_MaxDistance = Math.Max(m_MinDistance, maxDistance);
            m_State = new OrbitCameraState(0f, 15f, Clamp(initialDistance, m_MinDistance, m_MaxDistance));
        }

        public OrbitCameraState State => m_State;

        public OrbitCameraState Step(in PlayerControlFrame control, float deltaSeconds)
        {
            float delta = Math.Max(0f, deltaSeconds);
            float yaw = Repeat(m_State.Yaw + control.LookX * m_LookSpeed * delta, 360f);
            float pitch = Clamp(m_State.Pitch - control.LookY * m_LookSpeed * delta, m_MinPitch, m_MaxPitch);
            float distance = Clamp(m_State.Distance - control.Zoom * m_ZoomSpeed * delta, m_MinDistance, m_MaxDistance);
            m_State = new OrbitCameraState(yaw, pitch, distance);
            return m_State;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static float Repeat(float value, float length)
        {
            return value - (float)Math.Floor(value / length) * length;
        }
    }
}
