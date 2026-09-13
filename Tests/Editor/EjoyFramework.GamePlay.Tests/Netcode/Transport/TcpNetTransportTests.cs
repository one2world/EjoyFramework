//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EjoyFramework.GamePlay.Netcode.Transport;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Netcode.Transport
{
    /// <summary>
    /// 针对 <see cref="TcpNetTransport"/> 的本地回环往返冒烟测试。
    /// <para>
    /// 该测试涉及真实套接字与后台线程，时序在 EditMode 下不完全确定，因此标记为 <see cref="ExplicitAttribute"/>，
    /// 不进入默认套件以避免偶发抖动；统一传输层的主要确定性覆盖由 <see cref="LoopbackTransportTests"/> 承担。
    /// 需要时可在 Test Runner 中手动运行本测试以验证真实 TCP 链路。
    /// </para>
    /// </summary>
    [TestFixture]
    public class TcpNetTransportTests
    {
        // 收集事件，供轮询循环断言。
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
                    byte[] copy = new byte[seg.Count];
                    Buffer.BlockCopy(seg.Array, seg.Offset, copy, 0, seg.Count);
                    Data.Add((id, copy));
                };
            }
        }

        [Test]
        [Explicit("涉及真实套接字与后台线程，时序非确定；按需手动运行。确定性覆盖见 LoopbackTransportTests。")]
        public void LocalhostRoundTrip_ClientAndServerExchangeBytes()
        {
            int port = FindFreePort();

            TcpNetTransport server = new TcpNetTransport();
            TcpNetTransport client = new TcpNetTransport();
            Recorder serverRec = new Recorder(server);
            Recorder clientRec = new Recorder(client);

            try
            {
                server.StartServer(port);
                client.Connect("127.0.0.1", port);

                // 等待服务器观察到连接（截止 ~5s）。
                bool connected = SpinUntil(server, client, () =>
                    serverRec.Connected.Count >= 1 && clientRec.Connected.Count >= 1);
                Assert.IsTrue(connected, "服务器与客户端应在截止时间内都观察到连接事件");

                int connId = serverRec.Connected[0];

                // 客户端 -> 服务器。
                byte[] up = { 10, 20, 30, 40 };
                client.Send(0, new ArraySegment<byte>(up));
                bool gotUp = SpinUntil(server, client, () => serverRec.Data.Count >= 1);
                Assert.IsTrue(gotUp, "服务器应收到客户端数据");
                CollectionAssert.AreEqual(up, serverRec.Data[0].data);
                Assert.AreEqual(connId, serverRec.Data[0].connId);

                // 服务器 -> 客户端。
                byte[] down = { 5, 6, 7 };
                server.Send(connId, new ArraySegment<byte>(down));
                bool gotDown = SpinUntil(server, client, () => clientRec.Data.Count >= 1);
                Assert.IsTrue(gotDown, "客户端应收到服务器数据");
                CollectionAssert.AreEqual(down, clientRec.Data[0].data);
            }
            finally
            {
                client.Shutdown();
                server.Shutdown();
            }
        }

        // 在截止时间内反复 Poll 双方并检查条件；命中即返回 true。
        private static bool SpinUntil(INetTransport server, INetTransport client, Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                server.Poll();
                client.Poll();

                if (condition())
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            // 末次再排空一遍，给最后到达的事件一次机会。
            server.Poll();
            client.Poll();
            return condition();
        }

        // 借助系统分配一个空闲端口（立即释放后复用，存在极小竞态，仅用于显式测试）。
        private static int FindFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
