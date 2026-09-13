//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Music;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Music
{
    /// <summary>
    /// 针对 <see cref="DuckController"/> 压低 / 恢复 / 叠加语义的单元测试。
    /// </summary>
    [TestFixture]
    public class DuckControllerTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Initial_Multiplier_IsOne()
        {
            DuckController duck = new DuckController();
            Assert.AreEqual(1f, duck.CurrentMultiplier, Delta);
            Assert.AreEqual(0, duck.ActiveDuckCount);
        }

        [Test]
        public void PushDuck_MovesTowardTarget_OverAttack()
        {
            DuckController duck = new DuckController();
            duck.PushDuck(0.3f, 1f); // 目标 0.3，attack 1 秒。

            duck.Tick(0.5f);
            // 从 1.0 朝 0.3，attack 1s，按满量程速率：0.5 秒移动 0.5 => 0.5。
            Assert.AreEqual(0.5f, duck.CurrentMultiplier, Delta);

            duck.Tick(0.5f);
            // 再移动 0.5，但不越过目标 0.3 => 落到 0.3。
            Assert.AreEqual(0.3f, duck.CurrentMultiplier, Delta);

            // 继续 Tick 保持在目标。
            duck.Tick(1f);
            Assert.AreEqual(0.3f, duck.CurrentMultiplier, Delta);
        }

        [Test]
        public void PopDuck_RestoresTowardOne_OverRelease()
        {
            DuckController duck = new DuckController();
            int handle = duck.PushDuck(0.3f, 0f); // 瞬时压到 0.3。
            duck.Tick(0.016f);
            Assert.AreEqual(0.3f, duck.CurrentMultiplier, Delta);

            duck.PopDuck(handle, 1f); // 1 秒恢复到 1。
            Assert.AreEqual(0, duck.ActiveDuckCount);

            duck.Tick(0.5f);
            // 从 0.3 朝 1.0，release 1s，0.5 秒移动 0.5 => 0.8。
            Assert.AreEqual(0.8f, duck.CurrentMultiplier, Delta);

            duck.Tick(0.5f);
            Assert.AreEqual(1f, duck.CurrentMultiplier, Delta);
        }

        [Test]
        public void StackedDucks_TargetIsStrongest()
        {
            DuckController duck = new DuckController();
            // 先压到 0.6（瞬时），再压到 0.3（瞬时）。
            duck.PushDuck(0.6f, 0f);
            duck.Tick(0.016f);
            Assert.AreEqual(0.6f, duck.CurrentMultiplier, Delta);

            int strong = duck.PushDuck(0.3f, 0f);
            duck.Tick(0.016f);
            // 两个叠加，生效目标为更强（更低）的 0.3。
            Assert.AreEqual(0.3f, duck.CurrentMultiplier, Delta);
            Assert.AreEqual(2, duck.ActiveDuckCount);

            // 弹出强压低（0.3），应回退到剩余的 0.6。
            duck.PopDuck(strong, 0f);
            duck.Tick(0.016f);
            Assert.AreEqual(0.6f, duck.CurrentMultiplier, Delta);
            Assert.AreEqual(1, duck.ActiveDuckCount);
        }

        [Test]
        public void PopDuck_InvalidHandle_NoOp()
        {
            DuckController duck = new DuckController();
            duck.PushDuck(0.5f, 0f);
            duck.Tick(0.016f);

            duck.PopDuck(9999, 0.5f); // 无效句柄。
            duck.Tick(0.016f);

            Assert.AreEqual(0.5f, duck.CurrentMultiplier, Delta);
            Assert.AreEqual(1, duck.ActiveDuckCount);
        }

        [Test]
        public void Target_ClampedToUnitRange()
        {
            DuckController duck = new DuckController();
            duck.PushDuck(-1f, 0f); // 钳制到 0。
            duck.Tick(0.016f);
            Assert.AreEqual(0f, duck.CurrentMultiplier, Delta);
        }

        [Test]
        public void Tick_NonPositiveDelta_NoOp()
        {
            DuckController duck = new DuckController();
            duck.PushDuck(0.3f, 1f);

            duck.Tick(0f);
            duck.Tick(-1f);

            Assert.AreEqual(1f, duck.CurrentMultiplier, Delta);
        }

        [Test]
        public void NoActiveDucks_ReturnsToOne()
        {
            DuckController duck = new DuckController();
            int handle = duck.PushDuck(0.2f, 0f);
            duck.Tick(0.016f);
            Assert.AreEqual(0.2f, duck.CurrentMultiplier, Delta);

            duck.PopDuck(handle, 0f); // 瞬时恢复。
            duck.Tick(0.016f);
            Assert.AreEqual(1f, duck.CurrentMultiplier, Delta);
        }
    }
}
