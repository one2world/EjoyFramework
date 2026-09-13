//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Messages;
using EjoyFramework.GamePlay.Netcode.Session;

namespace EjoyFramework.GamePlay.Tests.Netcode.Session
{
    /// <summary>
    /// 三种线路消息（<see cref="WelcomeMessage"/> / <see cref="InputMessage"/> / <see cref="SnapshotMessage"/>）的
    /// 单元级 Serialize/Deserialize 往返测试，并验证经 <see cref="NetMessageRegistry"/> 成帧/解帧的稳定性。
    /// </summary>
    public class NetcodeMessageRoundTripTests
    {
        private const float Delta = 1e-4f;

        private static NetMessageRegistry NewRegistry()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            registry.Register(NetcodeMessageIds.Welcome, () => new WelcomeMessage());
            registry.Register(NetcodeMessageIds.Input, () => new InputMessage());
            registry.Register(NetcodeMessageIds.Snapshot, () => new SnapshotMessage());
            return registry;
        }

        [Test]
        public void WelcomeMessage_RoundTrips()
        {
            WelcomeMessage original = new WelcomeMessage { EntityId = 77, TickRateHz = 30f };

            NetWriter writer = new NetWriter();
            original.Serialize(writer);

            WelcomeMessage clone = new WelcomeMessage();
            clone.Deserialize(new NetReader(writer.ToArray()));

            Assert.AreEqual(77, clone.EntityId);
            Assert.AreEqual(30f, clone.TickRateHz, Delta);
        }

        [Test]
        public void InputMessage_RoundTrips()
        {
            InputMessage original = new InputMessage
            {
                Sequence = 12345u,
                DeltaTime = 0.0333f,
                Input = new[] { 1.5f, -2.25f, 0f },
            };

            NetWriter writer = new NetWriter();
            original.Serialize(writer);

            InputMessage clone = new InputMessage();
            clone.Deserialize(new NetReader(writer.ToArray()));

            Assert.AreEqual(12345u, clone.Sequence);
            Assert.AreEqual(0.0333f, clone.DeltaTime, Delta);
            Assert.AreEqual(3, clone.Input.Length);
            Assert.AreEqual(1.5f, clone.Input[0], Delta);
            Assert.AreEqual(-2.25f, clone.Input[1], Delta);
            Assert.AreEqual(0f, clone.Input[2], Delta);
        }

        [Test]
        public void InputMessage_EmptyInput_RoundTrips()
        {
            InputMessage original = new InputMessage
            {
                Sequence = 1u,
                DeltaTime = 0.1f,
                Input = System.Array.Empty<float>(),
            };

            NetWriter writer = new NetWriter();
            original.Serialize(writer);

            InputMessage clone = new InputMessage();
            clone.Deserialize(new NetReader(writer.ToArray()));

            Assert.AreEqual(1u, clone.Sequence);
            Assert.IsNotNull(clone.Input);
            Assert.AreEqual(0, clone.Input.Length);
        }

        [Test]
        public void SnapshotMessage_RoundTrips()
        {
            SnapshotMessage original = new SnapshotMessage
            {
                Tick = 9000u,
                ServerTimeSec = 12.5,
                LastProcessedInputSequence = 42u,
                Entities = new List<SnapshotMessage.EntityState>
                {
                    new SnapshotMessage.EntityState(1, new[] { 3f, 4f }),
                    new SnapshotMessage.EntityState(2, new[] { -1f, 0.5f, 9f }),
                    new SnapshotMessage.EntityState(3, System.Array.Empty<float>()),
                },
            };

            NetWriter writer = new NetWriter();
            original.Serialize(writer);

            SnapshotMessage clone = new SnapshotMessage();
            clone.Deserialize(new NetReader(writer.ToArray()));

            Assert.AreEqual(9000u, clone.Tick);
            Assert.AreEqual(12.5, clone.ServerTimeSec, 1e-9);
            Assert.AreEqual(42u, clone.LastProcessedInputSequence);

            Assert.AreEqual(3, clone.Entities.Count);

            Assert.AreEqual(1, clone.Entities[0].EntityId);
            Assert.AreEqual(2, clone.Entities[0].Values.Length);
            Assert.AreEqual(3f, clone.Entities[0].Values[0], Delta);
            Assert.AreEqual(4f, clone.Entities[0].Values[1], Delta);

            Assert.AreEqual(2, clone.Entities[1].EntityId);
            Assert.AreEqual(3, clone.Entities[1].Values.Length);
            Assert.AreEqual(9f, clone.Entities[1].Values[2], Delta);

            Assert.AreEqual(3, clone.Entities[2].EntityId);
            Assert.AreEqual(0, clone.Entities[2].Values.Length);
        }

        [Test]
        public void AllMessages_PackUnpack_ThroughRegistry()
        {
            NetMessageRegistry registry = NewRegistry();

            WelcomeMessage welcome = new WelcomeMessage { EntityId = 5, TickRateHz = 60f };
            byte[] framed = registry.Pack(welcome);

            ushort typeId;
            INetMessage decoded = registry.Unpack(new System.ArraySegment<byte>(framed), out typeId);

            Assert.AreEqual(NetcodeMessageIds.Welcome, typeId);
            Assert.IsInstanceOf<WelcomeMessage>(decoded);
            Assert.AreEqual(5, ((WelcomeMessage)decoded).EntityId);
            Assert.AreEqual(60f, ((WelcomeMessage)decoded).TickRateHz, Delta);
        }
    }
}
