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
    /// 针对 <see cref="MessageDispatcher"/> 的单元测试：覆盖强类型分派与 connectionId 传递、
    /// 多类型独立路由、未知 typeId 静默忽略、Clear 移除处理器与构造参数校验。
    /// </summary>
    [TestFixture]
    public class MessageDispatcherTests
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
        public void Constructor_NullRegistry_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MessageDispatcher(null));
        }

        [Test]
        public void On_NullHandler_Throws()
        {
            MessageDispatcher dispatcher = new MessageDispatcher(BuildRegistry());
            Assert.Throws<ArgumentNullException>(() => dispatcher.On<MoveMessage>(null));
        }

        [Test]
        public void Dispatch_RoutesToTypedHandler_WithConnectionIdAndPayload()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            int seenConnectionId = -1;
            MoveMessage seenMessage = null;
            dispatcher.On<MoveMessage>((connectionId, message) =>
            {
                seenConnectionId = connectionId;
                seenMessage = message;
            });

            byte[] framed = registry.Pack(new MoveMessage
            {
                EntityId = 77,
                X = 1.25f,
                Y = -3.5f,
                Sprinting = true
            });

            dispatcher.Dispatch(4242, new ArraySegment<byte>(framed));

            Assert.AreEqual(4242, seenConnectionId);
            Assert.IsNotNull(seenMessage);
            Assert.AreEqual(77, seenMessage.EntityId);
            Assert.AreEqual(1.25f, seenMessage.X);
            Assert.AreEqual(-3.5f, seenMessage.Y);
            Assert.IsTrue(seenMessage.Sprinting);
        }

        [Test]
        public void Dispatch_TwoMessageTypes_RoutedIndependently()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            int moveCalls = 0;
            int chatCalls = 0;
            string lastChatText = null;

            dispatcher.On<MoveMessage>((conn, msg) => moveCalls++);
            dispatcher.On<ChatTextMessage>((conn, msg) =>
            {
                chatCalls++;
                lastChatText = msg.Text;
            });

            byte[] moveFramed = registry.Pack(new MoveMessage { EntityId = 1 });
            byte[] chatFramed = registry.Pack(new ChatTextMessage { Sender = "s", Text = "你好" });

            dispatcher.Dispatch(10, new ArraySegment<byte>(moveFramed));
            dispatcher.Dispatch(11, new ArraySegment<byte>(chatFramed));
            dispatcher.Dispatch(12, new ArraySegment<byte>(moveFramed));

            Assert.AreEqual(2, moveCalls);
            Assert.AreEqual(1, chatCalls);
            Assert.AreEqual("你好", lastChatText);
        }

        [Test]
        public void Dispatch_UnknownTypeId_IgnoredSilently()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            bool anyHandlerCalled = false;
            dispatcher.On<MoveMessage>((conn, msg) => anyHandlerCalled = true);

            // 帧头 typeId=9999（未注册）+ 一些垃圾负载。
            NetWriter w = new NetWriter();
            w.WriteUShort(9999);
            w.WriteInt(0);

            Assert.DoesNotThrow(() => dispatcher.Dispatch(1, w.AsSegment()));
            Assert.IsFalse(anyHandlerCalled);
        }

        [Test]
        public void Dispatch_RegisteredTypeWithoutHandler_IgnoredSilently()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            // 注册 MoveMessage 处理器，但发送 ChatTextMessage（已在注册表，但无处理器）。
            bool moveCalled = false;
            dispatcher.On<MoveMessage>((conn, msg) => moveCalled = true);

            byte[] chatFramed = registry.Pack(new ChatTextMessage { Sender = "a", Text = "b" });
            Assert.DoesNotThrow(() => dispatcher.Dispatch(1, new ArraySegment<byte>(chatFramed)));
            Assert.IsFalse(moveCalled);
        }

        [Test]
        public void Dispatch_TruncatedFrame_IgnoredSilently()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);
            dispatcher.On<PingMessage>((conn, msg) => Assert.Fail("不应被调用"));

            // 仅 1 字节，连 typeId 都不完整。
            Assert.DoesNotThrow(
                () => dispatcher.Dispatch(1, new ArraySegment<byte>(new byte[] { 0x03 })));
        }

        [Test]
        public void Clear_RemovesHandler_SubsequentDispatchIgnored()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            int calls = 0;
            dispatcher.On<PingMessage>((conn, msg) => calls++);

            byte[] framed = registry.Pack(new PingMessage { Sequence = 1 });
            dispatcher.Dispatch(1, new ArraySegment<byte>(framed));
            Assert.AreEqual(1, calls);

            dispatcher.Clear<PingMessage>();
            dispatcher.Dispatch(1, new ArraySegment<byte>(framed));
            Assert.AreEqual(1, calls, "Clear 后处理器不应再被调用");
        }

        [Test]
        public void On_SameType_OverridesPreviousHandler()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            int firstCalls = 0;
            int secondCalls = 0;
            dispatcher.On<PingMessage>((conn, msg) => firstCalls++);
            dispatcher.On<PingMessage>((conn, msg) => secondCalls++);

            byte[] framed = registry.Pack(new PingMessage { Sequence = 1 });
            dispatcher.Dispatch(1, new ArraySegment<byte>(framed));

            Assert.AreEqual(0, firstCalls);
            Assert.AreEqual(1, secondCalls);
        }

        [Test]
        public void Clear_UnregisteredHandler_DoesNotThrow()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);
            // 从未 On<MoveMessage>，Clear 应安全无副作用。
            Assert.DoesNotThrow(() => dispatcher.Clear<MoveMessage>());
        }

        [Test]
        public void Dispatch_PreservesDistinctConnectionIds()
        {
            NetMessageRegistry registry = BuildRegistry();
            MessageDispatcher dispatcher = new MessageDispatcher(registry);

            List<int> seen = new List<int>();
            dispatcher.On<PingMessage>((conn, msg) => seen.Add(conn));

            byte[] framed = registry.Pack(new PingMessage { Sequence = 1 });
            dispatcher.Dispatch(100, new ArraySegment<byte>(framed));
            dispatcher.Dispatch(200, new ArraySegment<byte>(framed));
            dispatcher.Dispatch(300, new ArraySegment<byte>(framed));

            Assert.AreEqual(new List<int> { 100, 200, 300 }, seen);
        }
    }
}
