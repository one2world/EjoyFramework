//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Input;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class InputManagerTests
    {
        private InputManager m_IM;
        private MockHelper m_H;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_IM = new InputManager();
            m_H = new MockHelper();
            m_IM.SetHelper(m_H);
        }

        [Test]
        public void GetButton_ReturnsTrue_WhenPrimaryKeyDown()
        {
            m_IM.SetBinding("Jump", InputBinding.Key("Space"));
            m_H.Keys.Add("Space");
            Assert.IsTrue(m_IM.GetButton("Jump"));
        }

        [Test]
        public void GetButton_ReturnsTrue_FromSecondaryKey()
        {
            m_IM.SetBinding("Jump", InputBinding.Key("Space", "Return"));
            m_H.Keys.Add("Return");
            Assert.IsTrue(m_IM.GetButton("Jump"));
        }

        [Test]
        public void GetAxis_ReturnsValue_FromHelper()
        {
            m_IM.SetBinding("MoveX", InputBinding.Axis("Horizontal"));
            m_H.Axes["Horizontal"] = 0.7f;
            Assert.AreEqual(0.7f, m_IM.GetAxis("MoveX"), 0.001f);
        }

        [Test]
        public void Context_NonGame_DisablesInput()
        {
            m_IM.SetBinding("Jump", InputBinding.Key("Space"));
            m_H.Keys.Add("Space");
            m_IM.PushContext(InputContext.UI);
            Assert.IsFalse(m_IM.GetButton("Jump"));
            m_IM.PopContext();
            Assert.IsTrue(m_IM.GetButton("Jump"));
        }

        [Test]
        public void PopContext_OnRootStack_Noop()
        {
            // 栈底总是 Game
            m_IM.PopContext();
            m_IM.PopContext();
            Assert.AreEqual(InputContext.Game, m_IM.CurrentContext);
        }

        [Test]
        public void UnknownAction_ReturnsFalseOrZero()
        {
            Assert.IsFalse(m_IM.GetButton("Unknown"));
            Assert.AreEqual(0f, m_IM.GetAxis("Unknown"), 0.001f);
        }

        [Test]
        public void GetBinding_AfterSet_RoundTrip()
        {
            var binding = InputBinding.Key("Space", "Return");
            m_IM.SetBinding("Jump", binding);
            var got = m_IM.GetBinding("Jump");
            Assert.AreSame(binding, got);
        }

        [Test]
        public void ClearBindings_RemovesAll()
        {
            m_IM.SetBinding("A", InputBinding.Key("Space"));
            m_IM.SetBinding("B", InputBinding.Axis("Horizontal"));
            m_IM.ClearBindings();
            Assert.IsNull(m_IM.GetBinding("A"));
            Assert.IsNull(m_IM.GetBinding("B"));
        }

        private sealed class MockHelper : IInputHelper
        {
            public bool IsTouchSupported => false;
            public readonly HashSet<string> Keys = new HashSet<string>();
            public readonly HashSet<string> KeysDown = new HashSet<string>();
            public readonly HashSet<string> KeysUp = new HashSet<string>();
            public readonly Dictionary<string, float> Axes = new Dictionary<string, float>();
            public bool GetKey(string k) => Keys.Contains(k);
            public bool GetKeyDown(string k) => KeysDown.Contains(k);
            public bool GetKeyUp(string k) => KeysUp.Contains(k);
            public float GetAxisRaw(string a) => Axes.TryGetValue(a, out var v) ? v : 0f;
        }
    }
}
