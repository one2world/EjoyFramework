using EjoyFramework.GamePlay.ThreeC;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests
{
    public sealed class ThreeCControllerTests
    {
        [Test]
        public void Motor_UsesCameraRelativeForward()
        {
            var motor = new ThirdPersonMotor(walkSpeed: 4f, acceleration: 100f);
            var control = new PlayerControlFrame(1, 0f, 1f, 0f, 0f, 0f, false, false);
            var feedback = new CharacterMotorFeedback(true);

            CharacterMotorStep step = motor.Step(in control, in feedback, 90f, 0.1f);

            Assert.That(step.Velocity.X, Is.EqualTo(4f).Within(0.001f));
            Assert.That(step.Velocity.Z, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Motor_JumpAndGravityAreBounded()
        {
            var motor = new ThirdPersonMotor(jumpHeight: 2f, gravity: 20f, terminalVelocity: 30f);
            var jump = new PlayerControlFrame(1, 0f, 0f, 0f, 0f, 0f, true, false);
            var grounded = new CharacterMotorFeedback(true);
            CharacterMotorStep first = motor.Step(in jump, in grounded, 0f, 0.02f);
            Assert.That(first.Velocity.Y, Is.GreaterThan(0f));

            var idle = new PlayerControlFrame(2, 0f, 0f, 0f, 0f, 0f, false, false);
            var airborne = new CharacterMotorFeedback(false);
            CharacterMotorStep current = first;
            for (int i = 0; i < 200; i++)
                current = motor.Step(in idle, in airborne, 0f, 0.02f);

            Assert.That(current.Velocity.Y, Is.EqualTo(-30f).Within(0.001f));
        }

        [Test]
        public void Camera_ClampsPitchAndDistance()
        {
            var camera = new OrbitCameraController(initialDistance: 5f);
            var control = new PlayerControlFrame(1, 0f, 0f, 0f, -1f, 1f, false, false);
            OrbitCameraState state = default;
            for (int i = 0; i < 100; i++)
                state = camera.Step(in control, 0.1f);

            Assert.That(state.Pitch, Is.EqualTo(75f));
            Assert.That(state.Distance, Is.EqualTo(1.5f));
        }
    }
}
