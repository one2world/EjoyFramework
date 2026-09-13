//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Messages;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Messages
{
    /// <summary>
    /// 针对 <see cref="NetMessageRegistry"/> 的单元测试：覆盖注册/查询 typeId、Pack 帧头、
    /// Pack→Unpack 往返、重复注册校验与未注册类型的异常路径。
    /// </summary>
    [TestFixture]
    public class NetMessageRegistryTests
    {
        private static NetMessageRegistry BuildRegistry()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            registry.Register<MoveMessage>(1, () => new MoveMessage());
            registry.Register<ChatTextMessage>(2, () => new ChatTextMessage());
            registry.Register<PingMessage>(3, () => new PingMessage());
            return registry;
        }

        [Test]
        public void GetTypeId_ReturnsRegisteredId()
        {
            NetMessageRegistry registry = BuildRegistry();
            Assert.AreEqual(1, registry.GetTypeId<MoveMessage>());
            Assert.AreEqual(2, registry.GetTypeId<ChatTextMessage>());
            Assert.AreEqual(3, registry.GetTypeId<PingMessage>());
        }

        [Test]
        public void GetTypeId_Unregistered_Throws()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            Assert.Throws<KeyNotFoundException>(() => registry.GetTypeId<MoveMessage>());
        }

        [Test]
        public void Register_DuplicateTypeId_Throws()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            registry.Register<MoveMessage>(5, () => new MoveMessage());
            Assert.Throws<ArgumentException>(
                () => registry.Register<ChatTextMessage>(5, () => new ChatTextMessage()));
        }

        [Test]
        public void Register_DuplicateType_Throws()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            registry.Register<MoveMessage>(5, () => new MoveMessage());
            Assert.Throws<ArgumentException>(
                () => registry.Register<MoveMessage>(6, () => new MoveMessage()));
        }

        [Test]
        public void Register_NullFactory_Throws()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            Assert.Throws<ArgumentNullException>(
                () => registry.Register<MoveMessage>(1, null));
        }

        [Test]
        public void Pack_PrependsTypeIdAsLittleEndianUShort()
        {
            NetMessageRegistry registry = BuildRegistry();
            PingMessage ping = new PingMessage { Sequence = 99 };

            byte[] framed = registry.Pack(ping);

            // 帧 = [ushort typeId=3 (小端: 03 00)][payload: Sequence=99]
            Assert.AreEqual(3, framed.Length);
            Assert.AreEqual(0x03, framed[0]);
            Assert.AreEqual(0x00, framed[1]);
            Assert.AreEqual(99, framed[2]);
        }

        [Test]
        public void PackThenUnpack_MoveMessage_RoundTrips()
        {
            NetMessageRegistry registry = BuildRegistry();
            MoveMessage original = new MoveMessage
            {
                EntityId = 4242,
                X = 12.5f,
                Y = -7.25f,
                Sprinting = true
            };

            byte[] framed = registry.Pack(original);

            ushort typeId;
            INetMessage decoded = registry.Unpack(new ArraySegment<byte>(framed), out typeId);

            Assert.AreEqual(registry.GetTypeId<MoveMessage>(), typeId);
            Assert.IsInstanceOf<MoveMessage>(decoded);

            MoveMessage move = (MoveMessage)decoded;
            Assert.AreEqual(4242, move.EntityId);
            Assert.AreEqual(12.5f, move.X);
            Assert.AreEqual(-7.25f, move.Y);
            Assert.IsTrue(move.Sprinting);
        }

        [Test]
        public void PackThenUnpack_ChatTextMessage_RoundTrips()
        {
            NetMessageRegistry registry = BuildRegistry();
            ChatTextMessage original = new ChatTextMessage
            {
                Sender = "玩家A",
                Text = "集合 🎯 now"
            };

            byte[] framed = registry.Pack(original);

            ushort typeId;
            INetMessage decoded = registry.Unpack(new ArraySegment<byte>(framed), out typeId);

            Assert.AreEqual(2, typeId);
            ChatTextMessage chat = (ChatTextMessage)decoded;
            Assert.AreEqual("玩家A", chat.Sender);
            Assert.AreEqual("集合 🎯 now", chat.Text);
        }

        [Test]
        public void Unpack_UnknownTypeId_Throws()
        {
            NetMessageRegistry registry = BuildRegistry();
            // 帧头 typeId=999（未注册）。
            NetWriter w = new NetWriter();
            w.WriteUShort(999);
            ushort ignored;
            Assert.Throws<KeyNotFoundException>(
                () => registry.Unpack(w.AsSegment(), out ignored));
        }

        [Test]
        public void Pack_UnregisteredMessage_Throws()
        {
            NetMessageRegistry registry = new NetMessageRegistry();
            Assert.Throws<KeyNotFoundException>(
                () => registry.Pack(new MoveMessage()));
        }

        [Test]
        public void Pack_NullMessage_Throws()
        {
            NetMessageRegistry registry = BuildRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.Pack(null));
        }

        [Test]
        public void PackInto_ProducesSameBytesAsPack()
        {
            NetMessageRegistry registry = BuildRegistry();
            MoveMessage msg = new MoveMessage { EntityId = 7, X = 1.5f, Y = 2.5f, Sprinting = true };

            byte[] viaPack = registry.Pack(msg);

            NetWriter writer = new NetWriter();
            ArraySegment<byte> seg = registry.PackInto(msg, writer);

            Assert.AreEqual(viaPack.Length, seg.Count);
            for (int i = 0; i < viaPack.Length; i++)
            {
                Assert.AreEqual(viaPack[i], seg.Array[seg.Offset + i], "byte " + i);
            }
        }

        [Test]
        public void PackInto_ReusedWriter_RoundTripsEachMessage()
        {
            NetMessageRegistry registry = BuildRegistry();
            NetWriter writer = new NetWriter();

            // 复用同一 writer 连续打包不同消息，各自都应能独立 Unpack 还原（验证 Reset 正确、缓冲被复用）。
            ArraySegment<byte> a = registry.PackInto(new PingMessage { Sequence = 11 }, writer);
            byte[] aCopy = new byte[a.Count];
            Buffer.BlockCopy(a.Array, a.Offset, aCopy, 0, a.Count); // 第二次 PackInto 会覆盖 writer 缓冲，先拷出。
            ushort idA;
            PingMessage pa = (PingMessage)registry.Unpack(new ArraySegment<byte>(aCopy), out idA);
            Assert.AreEqual(11, pa.Sequence);

            ArraySegment<byte> b = registry.PackInto(new MoveMessage { EntityId = 5 }, writer);
            ushort idB;
            MoveMessage mb = (MoveMessage)registry.Unpack(b, out idB);
            Assert.AreEqual(5, mb.EntityId);
        }

        [Test]
        public void PackInto_NullArgs_Throw()
        {
            NetMessageRegistry registry = BuildRegistry();
            Assert.Throws<ArgumentNullException>(() => registry.PackInto(null, new NetWriter()));
            Assert.Throws<ArgumentNullException>(() => registry.PackInto(new PingMessage(), null));
        }

        [Test]
        public void TryPeekTypeId_OnTruncatedFrame_ReturnsFalse()
        {
            NetMessageRegistry registry = BuildRegistry();
            // 仅 1 字节，不足 ushort。
            bool ok = registry.TryPeekTypeId(new ArraySegment<byte>(new byte[] { 0x01 }), out ushort typeId);
            Assert.IsFalse(ok);
        }

        [Test]
        public void TryPeekTypeId_OnRegisteredFrame_ReturnsTrueWithId()
        {
            NetMessageRegistry registry = BuildRegistry();
            byte[] framed = registry.Pack(new PingMessage { Sequence = 1 });
            bool ok = registry.TryPeekTypeId(new ArraySegment<byte>(framed), out ushort typeId);
            Assert.IsTrue(ok);
            Assert.AreEqual(3, typeId);
        }
    }
}
