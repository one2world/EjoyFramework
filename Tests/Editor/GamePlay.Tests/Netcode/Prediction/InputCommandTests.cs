//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Prediction;

namespace EjoyFramework.GamePlay.Tests.Netcode.Prediction
{
    /// <summary>
    /// <see cref="InputCommand{TInput}"/> 的单元测试：构造后字段如实暴露。
    /// </summary>
    public class InputCommandTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Constructor_ExposesAllFields()
        {
            var cmd = new InputCommand<float>(7u, 0.016f, 3.5f);
            Assert.AreEqual(7u, cmd.Sequence);
            Assert.AreEqual(0.016f, cmd.DeltaTime, Delta);
            Assert.AreEqual(3.5f, cmd.Input, Delta);
        }

        [Test]
        public void Default_IsZeroAndDefaultInput()
        {
            var cmd = default(InputCommand<float>);
            Assert.AreEqual(0u, cmd.Sequence);
            Assert.AreEqual(0f, cmd.DeltaTime, Delta);
            Assert.AreEqual(0f, cmd.Input, Delta);
        }

        [Test]
        public void Constructor_WorksWithReferenceInputType()
        {
            var cmd = new InputCommand<string>(2u, 1f, "fire");
            Assert.AreEqual(2u, cmd.Sequence);
            Assert.AreEqual("fire", cmd.Input);
        }
    }
}
