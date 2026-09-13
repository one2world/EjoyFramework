//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using EjoyFramework.GamePlay.TouchInput;

namespace EjoyFramework.GamePlay.Tests.TouchInput
{
    public sealed class TouchInputDriverTests : InputTestFixture
    {
        private GameObject m_GameObject;

        public override void Setup()
        {
            base.Setup();
            m_GameObject = new GameObject("TouchInputDriverTests");
        }

        public override void TearDown()
        {
            Object.DestroyImmediate(m_GameObject);
            base.TearDown();
        }

        [Test]
        public void Update_NewInputSystemTouch_FeedsRecognizer()
        {
            Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
            TouchInputDriver driver = m_GameObject.AddComponent<TouchInputDriver>();
            int tapCount = 0;
            GestureEvent captured = default;
            driver.Recognizer.OnTap += e =>
            {
                tapCount++;
                captured = e;
            };

            BeginTouch(7, new Vector2(120f, 240f), screen: touchscreen);
            driver.ProcessInputFrame(0f);
            EndTouch(7, new Vector2(122f, 241f), screen: touchscreen);
            driver.ProcessInputFrame(0.01f);

            Assert.AreEqual(1, tapCount);
            Assert.AreEqual(122f, captured.X, 0.001f);
            Assert.AreEqual(241f, captured.Y, 0.001f);
        }
    }
}
