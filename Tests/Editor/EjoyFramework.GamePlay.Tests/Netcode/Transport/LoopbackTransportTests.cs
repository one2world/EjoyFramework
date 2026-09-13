//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Transport;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Transport
{
    /// <summary>
    /// 针对 <see cref="LoopbackTransport"/> 的确定性单元测试，覆盖配对、连接、单发、广播、
    /// 多客户端、断开、以及“事件仅在 Poll 时触发”等核心语义。这是统一传输层的主要覆盖来源。
    /// </summary>
    [TestFixture]
    public class LoopbackTransportTests
    {
        // 收集某实例触发的事件，便于断言顺序与内容。
        private sealed class Recorder
        {
            public readonly List<int> Connected = new List<int>();
            public readonly List<int> Disconnected = new List<int>();
            public readonly List<(int connId, byte[] data)> Data = new List<(int, byte[])>();

            public Recorder(INetTransport transport)
            {
                transport.OnClientConnected += id => Connected.Add(id);
                transport.OnClientDisconnected += id => Disconnected.Add(id);
                transport.OnData += (id, seg) =>
                {
                    // payload 仅在回调期间有效，立即复制。
                    byte[] copy = new byte[seg.Count];
                    Buffer.BlockCopy(seg.Array, seg.Offset, copy, 0, seg.Count);
                    Data.Add((id, copy));
                };
            }
        }

        [Test]
        public void CreatePair_RolesAreServerAndClient()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();

            Assert.IsTrue(server.IsServer);
            Assert.IsFalse(server.IsClient);

            Assert.IsTrue(client.IsClient);
            Assert.IsFalse(client.IsServer);
            Assert.AreEqual(NetConnectionState.Disconnected, client.ClientState);
        }

        [Test]
        public void Connect_RaisesConnectedOnBothSides_OnlyDuringPoll()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);
            Recorder clientRec = new Recorder(client);

            client.Connect("ignored", 0);

            // 关键：Connect 之后、Poll 之前，任何事件都不得触发。
            Assert.AreEqual(0, serverRec.Connected.Count, "服务器在 Poll 前不应触发事件");
            Assert.AreEqual(0, clientRec.Connected.Count, "客户端在 Poll 前不应触发事件");

            // 客户端状态在连接调用后立即可见为 Connected。
            Assert.AreEqual(NetConnectionState.Connected, client.ClientState);

            server.Poll();
            Assert.AreEqual(1, serverRec.Connected.Count);
            Assert.AreEqual(0, serverRec.Connected[0], "首个连接的 connId 应为 0");

            client.Poll();
            Assert.AreEqual(1, clientRec.Connected.Count);
            Assert.AreEqual(0, clientRec.Connected[0], "客户端本地 connId 应为 0");
        }

        [Test]
        public void ClientSend_DeliversToServerOnData_WithConnIdAndBytes()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);

            client.Connect("h", 1);
            server.Poll(); // 消费连接事件

            byte[] payload = { 1, 2, 3, 4, 5 };
            client.Send(0, new ArraySegment<byte>(payload));

            // Poll 前无数据。
            Assert.AreEqual(0, serverRec.Data.Count);

            server.Poll();
            Assert.AreEqual(1, serverRec.Data.Count);
            Assert.AreEqual(0, serverRec.Data[0].connId, "服务器收到的 connId 应为该客户端的 connId");
            CollectionAssert.AreEqual(payload, serverRec.Data[0].data);
        }

        [Test]
        public void ServerSend_DeliversToClientOnData()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder clientRec = new Recorder(client);

            client.Connect("h", 1);
            server.Poll();
            client.Poll(); // 消费客户端连接事件

            byte[] payload = { 9, 8, 7 };
            server.Send(0, new ArraySegment<byte>(payload));

            Assert.AreEqual(0, clientRec.Data.Count);

            client.Poll();
            Assert.AreEqual(1, clientRec.Data.Count);
            Assert.AreEqual(0, clientRec.Data[0].connId);
            CollectionAssert.AreEqual(payload, clientRec.Data[0].data);
        }

        [Test]
        public void Broadcast_DeliversToAllClients()
        {
            (INetTransport server, INetTransport clientA) = LoopbackTransport.CreatePair();
            INetTransport clientB = LoopbackTransport.CreateClient(server);

            Recorder recA = new Recorder(clientA);
            Recorder recB = new Recorder(clientB);

            clientA.Connect("h", 1);
            clientB.Connect("h", 1);
            server.Poll();
            clientA.Poll();
            clientB.Poll();

            byte[] payload = { 42, 43 };
            server.Broadcast(new ArraySegment<byte>(payload));

            clientA.Poll();
            clientB.Poll();

            Assert.AreEqual(1, recA.Data.Count);
            Assert.AreEqual(1, recB.Data.Count);
            CollectionAssert.AreEqual(payload, recA.Data[0].data);
            CollectionAssert.AreEqual(payload, recB.Data[0].data);
        }

        [Test]
        public void MultipleClients_GetDistinctConnectionIds()
        {
            (INetTransport server, INetTransport clientA) = LoopbackTransport.CreatePair();
            INetTransport clientB = LoopbackTransport.CreateClient(server);
            INetTransport clientC = LoopbackTransport.CreateClient(server);

            Recorder serverRec = new Recorder(server);

            clientA.Connect("h", 1);
            clientB.Connect("h", 1);
            clientC.Connect("h", 1);
            server.Poll();

            Assert.AreEqual(3, serverRec.Connected.Count);
            // 三个不同的自增 connId：0,1,2。
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, serverRec.Connected);
            CollectionAssert.AllItemsAreUnique(serverRec.Connected);
        }

        [Test]
        public void ServerSend_TargetsCorrectClientOnly()
        {
            (INetTransport server, INetTransport clientA) = LoopbackTransport.CreatePair();
            INetTransport clientB = LoopbackTransport.CreateClient(server);

            Recorder recA = new Recorder(clientA);
            Recorder recB = new Recorder(clientB);

            clientA.Connect("h", 1); // connId 0
            clientB.Connect("h", 1); // connId 1
            server.Poll();

            byte[] payload = { 100 };
            server.Send(1, new ArraySegment<byte>(payload)); // 仅发给 B

            clientA.Poll();
            clientB.Poll();

            Assert.AreEqual(0, recA.Data.Count, "A 不应收到发给 B 的数据");
            Assert.AreEqual(1, recB.Data.Count);
            CollectionAssert.AreEqual(payload, recB.Data[0].data);
        }

        [Test]
        public void Disconnect_RaisesDisconnectedOnBothSides()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);
            Recorder clientRec = new Recorder(client);

            client.Connect("h", 1);
            server.Poll();
            client.Poll();

            client.Disconnect();
            Assert.AreEqual(NetConnectionState.Disconnected, client.ClientState);

            // Poll 前无断开事件。
            Assert.AreEqual(0, serverRec.Disconnected.Count);
            Assert.AreEqual(0, clientRec.Disconnected.Count);

            server.Poll();
            client.Poll();

            Assert.AreEqual(1, serverRec.Disconnected.Count);
            Assert.AreEqual(0, serverRec.Disconnected[0], "断开的 connId 应为 0");
            Assert.AreEqual(1, clientRec.Disconnected.Count);
        }

        [Test]
        public void StopServer_DisconnectsAllClients()
        {
            (INetTransport server, INetTransport clientA) = LoopbackTransport.CreatePair();
            INetTransport clientB = LoopbackTransport.CreateClient(server);

            Recorder serverRec = new Recorder(server);
            Recorder recA = new Recorder(clientA);
            Recorder recB = new Recorder(clientB);

            clientA.Connect("h", 1);
            clientB.Connect("h", 1);
            server.Poll();
            clientA.Poll();
            clientB.Poll();

            server.StopServer();

            server.Poll();
            clientA.Poll();
            clientB.Poll();

            Assert.AreEqual(2, serverRec.Disconnected.Count, "服务器应为每个客户端各触发一次断开");
            Assert.AreEqual(1, recA.Disconnected.Count);
            Assert.AreEqual(1, recB.Disconnected.Count);
            Assert.AreEqual(NetConnectionState.Disconnected, clientA.ClientState);
            Assert.AreEqual(NetConnectionState.Disconnected, clientB.ClientState);
        }

        [Test]
        public void Delivery_IsOrdered()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);

            client.Connect("h", 1);
            server.Poll();

            for (int i = 0; i < 10; i++)
            {
                client.Send(0, new ArraySegment<byte>(new byte[] { (byte)i }));
            }

            server.Poll();

            Assert.AreEqual(10, serverRec.Data.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.AreEqual((byte)i, serverRec.Data[i].data[0], "投递必须保持入队顺序");
            }
        }

        [Test]
        public void Send_PayloadIsCopied_CallerMayReuseBuffer()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);

            client.Connect("h", 1);
            server.Poll();

            byte[] buffer = { 1, 2, 3 };
            client.Send(0, new ArraySegment<byte>(buffer));

            // 入队后改写调用方缓冲区，不应影响已投递内容。
            buffer[0] = 99;

            server.Poll();
            Assert.AreEqual(1, serverRec.Data.Count);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, serverRec.Data[0].data);
        }

        [Test]
        public void Send_PartialSegment_DeliversOnlyThatSlice()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder serverRec = new Recorder(server);

            client.Connect("h", 1);
            server.Poll();

            byte[] buffer = { 0, 1, 2, 3, 4, 5 };
            client.Send(0, new ArraySegment<byte>(buffer, 2, 3)); // {2,3,4}

            server.Poll();
            CollectionAssert.AreEqual(new byte[] { 2, 3, 4 }, serverRec.Data[0].data);
        }

        [Test]
        public void Shutdown_ServerDisconnectsClients_AndClearsState()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Recorder clientRec = new Recorder(client);

            client.Connect("h", 1);
            server.Poll();
            client.Poll();

            server.Shutdown();

            client.Poll();
            Assert.AreEqual(1, clientRec.Disconnected.Count);
            Assert.IsFalse(server.IsServer);
        }

        [Test]
        public void CreateClient_OnNonServer_Throws()
        {
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();
            Assert.Throws<ArgumentException>(() => LoopbackTransport.CreateClient(client));
        }

        [Test]
        public void Poll_OneHandlerThrows_OtherHandlersStillReceive()
        {
            // 回归：单个 OnData 处理器抛异常，必须被隔离，后续注册的处理器仍应收到本次事件。
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();

            bool firstInvoked = false;
            bool secondInvoked = false;
            server.OnData += (id, seg) =>
            {
                firstInvoked = true;
                throw new InvalidOperationException("故意抛出：验证处理器异常被隔离");
            };
            server.OnData += (id, seg) => secondInvoked = true;

            client.Connect("h", 1);
            server.Poll(); // 消费连接事件

            client.Send(0, new ArraySegment<byte>(new byte[] { 1 }));

            // 排空一次：第一个处理器抛异常不得阻止第二个处理器被调用。
            Assert.DoesNotThrow(() => server.Poll(), "处理器异常不得逸出 Poll");
            Assert.IsTrue(firstInvoked, "第一个处理器应被调用");
            Assert.IsTrue(secondInvoked, "第一个处理器抛异常后，第二个处理器仍应收到事件");
        }

        [Test]
        public void Poll_ThrowingHandler_DoesNotAbortRemainingQueuedEvents()
        {
            // 回归：处理器对某一条事件抛异常，不得中断本帧后续已入队事件的派发。
            (INetTransport server, INetTransport client) = LoopbackTransport.CreatePair();

            List<byte> received = new List<byte>();
            server.OnData += (id, seg) =>
            {
                byte value = seg.Array[seg.Offset];
                received.Add(value);

                // 第一条事件的处理器抛异常，后续两条仍应被派发。
                if (value == 0)
                {
                    throw new InvalidOperationException("故意抛出：验证队列继续派发");
                }
            };

            client.Connect("h", 1);
            server.Poll(); // 消费连接事件

            for (byte i = 0; i < 3; i++)
            {
                client.Send(0, new ArraySegment<byte>(new byte[] { i }));
            }

            Assert.DoesNotThrow(() => server.Poll(), "处理器异常不得逸出 Poll");
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, received, "首条事件抛异常后，其余已入队事件仍须全部派发");
        }
    }
}
