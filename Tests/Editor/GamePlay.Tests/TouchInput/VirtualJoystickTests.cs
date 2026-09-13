//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.TouchInput;

namespace EjoyFramework.GamePlay.Tests.TouchInput
{
    public class VirtualJoystickTests
    {
        private const float Delta = 1e-3f;

        [Test]
        public void Press_SetsActive_DynamicCenterAtPressPoint()
        {
            var joy = new VirtualJoystick(100f, dynamicCenter: true);
            Assert.IsFalse(joy.IsActive);

            joy.Press(500f, 500f);

            Assert.IsTrue(joy.IsActive);
            Assert.AreEqual(500f, joy.CenterX, Delta);
            Assert.AreEqual(500f, joy.CenterY, Delta);
            Assert.AreEqual(0f, joy.Magnitude, Delta, "刚按下时幅度为 0");
        }

        [Test]
        public void Drag_RightOfCenter_DirectionXIsPositiveOne()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(500f, 500f);

            joy.Drag(560f, 500f); // 正右方。

            Assert.AreEqual(1f, joy.DirectionX, Delta);
            Assert.AreEqual(0f, joy.DirectionY, Delta);
        }

        [Test]
        public void Drag_BeyondRadius_MagnitudeClampsToOne()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(500f, 500f);

            joy.Drag(1000f, 500f); // 距离 500 >> 半径 100。

            Assert.AreEqual(1f, joy.Magnitude, Delta, "超出半径幅度钳制到 1");
            Assert.AreEqual(1f, joy.DirectionX, Delta);
        }

        [Test]
        public void Drag_WithinRadius_MagnitudeIsProportional()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(500f, 500f);

            joy.Drag(550f, 500f); // 距离 50，半径 100 → 0.5。

            Assert.AreEqual(0.5f, joy.Magnitude, Delta);
            Assert.AreEqual(1f, joy.DirectionX, Delta);
        }

        [Test]
        public void Drag_Diagonal_DirectionIsNormalized()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(0f, 0f);

            joy.Drag(30f, 40f); // 距离 50；归一化方向 (0.6, 0.8)。

            Assert.AreEqual(0.6f, joy.DirectionX, Delta);
            Assert.AreEqual(0.8f, joy.DirectionY, Delta);
            Assert.AreEqual(0.5f, joy.Magnitude, Delta);
        }

        [Test]
        public void Drag_Up_DirectionYIsPositiveOne()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(500f, 500f);

            joy.Drag(500f, 600f); // 正上方（y 增大）。

            Assert.AreEqual(1f, joy.DirectionY, Delta);
            Assert.AreEqual(0f, joy.DirectionX, Delta);
        }

        [Test]
        public void Release_ZerosOut_AndInactive()
        {
            var joy = new VirtualJoystick(100f);
            joy.Press(500f, 500f);
            joy.Drag(560f, 540f);

            joy.Release();

            Assert.IsFalse(joy.IsActive);
            Assert.AreEqual(0f, joy.DirectionX, Delta);
            Assert.AreEqual(0f, joy.DirectionY, Delta);
            Assert.AreEqual(0f, joy.Magnitude, Delta);
        }

        [Test]
        public void Drag_WhenInactive_IsIgnored()
        {
            var joy = new VirtualJoystick(100f);
            joy.Drag(560f, 500f); // 未 Press，应被忽略。

            Assert.IsFalse(joy.IsActive);
            Assert.AreEqual(0f, joy.Magnitude, Delta);
            Assert.AreEqual(0f, joy.DirectionX, Delta);
        }

        [Test]
        public void FixedCenter_DragRelativeToPresetCenter()
        {
            var joy = new VirtualJoystick(100f, dynamicCenter: false);
            joy.SetFixedCenter(200f, 200f);

            joy.Press(260f, 200f); // 按下点已偏离固定圆心。
            // 固定模式下圆心保持 (200,200)，按下点在右侧 60px。
            Assert.AreEqual(200f, joy.CenterX, Delta);
            Assert.AreEqual(0.6f, joy.Magnitude, Delta);
            Assert.AreEqual(1f, joy.DirectionX, Delta);
        }

        [Test]
        public void NonPositiveRadius_FallsBackToOne()
        {
            var joy = new VirtualJoystick(0f);
            Assert.AreEqual(1f, joy.Radius, Delta);
        }

        [Test]
        public void Parameterless_DefaultsToHundredRadiusAndDynamicCenter()
        {
            var joy = new VirtualJoystick();
            Assert.AreEqual(100f, joy.Radius, Delta);

            joy.Press(300f, 300f); // 动态圆心：圆心应落在按下点。
            Assert.AreEqual(300f, joy.CenterX, Delta);
            Assert.AreEqual(300f, joy.CenterY, Delta);
        }

        [Test]
        public void SetRadius_OverridesRadius_AndClampsNonPositiveToOne()
        {
            var joy = new VirtualJoystick();
            joy.SetRadius(50f);
            Assert.AreEqual(50f, joy.Radius, Delta);

            joy.SetRadius(-10f); // 非正值回退为 1f。
            Assert.AreEqual(1f, joy.Radius, Delta);
        }

        [Test]
        public void SetDynamicCenter_False_UsesPresetFixedCenter()
        {
            var joy = new VirtualJoystick();
            joy.SetRadius(100f);
            joy.SetDynamicCenter(false);
            joy.SetFixedCenter(200f, 200f);

            joy.Press(260f, 200f); // 固定圆心模式：圆心保持 (200,200)，按下点右移 60px。

            Assert.AreEqual(200f, joy.CenterX, Delta);
            Assert.AreEqual(0.6f, joy.Magnitude, Delta);
            Assert.AreEqual(1f, joy.DirectionX, Delta);
        }
    }
}
