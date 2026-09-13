using System;

namespace EjoyFramework.GamePlay.ThreeC
{
    public readonly struct ThreeCVector3
    {
        public ThreeCVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float HorizontalSqrMagnitude => X * X + Z * Z;

        public static ThreeCVector3 operator *(ThreeCVector3 value, float scale)
        {
            return new ThreeCVector3(value.X * scale, value.Y * scale, value.Z * scale);
        }
    }

    public readonly struct PlayerControlFrame
    {
        public PlayerControlFrame(
            uint sequence,
            float moveX,
            float moveY,
            float lookX,
            float lookY,
            float zoom,
            bool jumpPressed,
            bool sprintHeld)
        {
            Sequence = sequence;
            MoveX = Clamp(moveX);
            MoveY = Clamp(moveY);
            LookX = Clamp(lookX);
            LookY = Clamp(lookY);
            Zoom = Clamp(zoom);
            JumpPressed = jumpPressed;
            SprintHeld = sprintHeld;
        }

        public uint Sequence { get; }
        public float MoveX { get; }
        public float MoveY { get; }
        public float LookX { get; }
        public float LookY { get; }
        public float Zoom { get; }
        public bool JumpPressed { get; }
        public bool SprintHeld { get; }

        private static float Clamp(float value)
        {
            if (value < -1f) return -1f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct CharacterMotorFeedback
    {
        public CharacterMotorFeedback(bool isGrounded)
        {
            IsGrounded = isGrounded;
        }

        public bool IsGrounded { get; }
    }

    public readonly struct CharacterMotorStep
    {
        public CharacterMotorStep(in ThreeCVector3 velocity, in ThreeCVector3 displacement, float facingYaw)
        {
            Velocity = velocity;
            Displacement = displacement;
            FacingYaw = facingYaw;
        }

        public ThreeCVector3 Velocity { get; }
        public ThreeCVector3 Displacement { get; }
        public float FacingYaw { get; }
    }

    public readonly struct OrbitCameraState
    {
        public OrbitCameraState(float yaw, float pitch, float distance)
        {
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
        }

        public float Yaw { get; }
        public float Pitch { get; }
        public float Distance { get; }
    }
}
