//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Threading;
using NUnit.Framework;
using EjoyFramework.Core.Network;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// Phase 6.2 — Network 模块回归。
    /// 验证 INetworkChannelHelper 与 NetworkChannel 的 Connect/Send/Close/Receive/Heartbeat/Timeout 链路。
    /// </summary>
    public class NetworkManagerTests
    {
        private sealed class TestPacket : Packet
        {
            private readonly int m_Id;
            public TestPacket(int id) { m_Id = id; }
            public override int Id => m_Id;
            public override void Clear() { Sequence = 0; }
        }

        private sealed class FakeHelper : INetworkChannelHelper
        {
            public INetworkChannel Channel;
            public int SendCalls;
            public bool SendReturn = true;
            public int ConnectCalls;
            public int CloseCalls;
            public int UpdateCalls;
            public Packet HeartBeatTemplate;
            public bool ThrowOnSend;

            public void Initialize(INetworkChannel channel) { Channel = channel; }
            public void Connect(string ip, int port, object userData)
            {
                ConnectCalls++;
                Channel.NotifyConnected(userData);
            }
            public void Close() { CloseCalls++; Channel.NotifyClosed(); }
            public bool Send<T>(T packet) where T : Packet
            {
                SendCalls++;
                if (ThrowOnSend) throw new InvalidOperationException("boom");
                return SendReturn;
            }
            public void Update(float a, float b) { UpdateCalls++; }
            public Packet CreateHeartBeatPacket() => HeartBeatTemplate;
            public void Shutdown() { }
        }

        private static INetworkManager NewManager()
        {
            var t = typeof(INetworkManager).Assembly.GetType("EjoyFramework.Core.Network.NetworkManager");
            return (INetworkManager)Activator.CreateInstance(t, true);
        }

        private static void TickUpdate(INetworkManager nm, float seconds)
        {
            ((FrameworkModule)nm).Update(seconds, seconds);
        }

        [Test]
        public void Create_Connect_FiresConnectedAndSetsState()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            INetworkChannel firedChannel = null;
            object firedUserData = null;
            nm.NetworkConnected += (s, e) => { firedChannel = e.NetworkChannel; firedUserData = e.UserData; };

            var ch = nm.CreateNetworkChannel("Main", helper);
            var ud = new object();
            ch.Connect("127.0.0.1", 1000, ud);

            Assert.AreSame(ch, firedChannel);
            Assert.AreSame(ud, firedUserData);
            Assert.IsTrue(ch.Connected);
            Assert.AreEqual(1, helper.ConnectCalls);
        }

        [Test]
        public void Send_WhenConnected_IncrementsCounter()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.Connect("ip", 1, null);

            ch.Send(new TestPacket(101));
            ch.Send(new TestPacket(102));
            Assert.AreEqual(2, ch.SendPacketCount);
            Assert.AreEqual(2, helper.SendCalls);
        }

        [Test]
        public void Send_NotConnected_FiresError()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            int errCode = 0;
            nm.NetworkError += (s, e) => errCode = e.ErrorCode;

            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.Send(new TestPacket(1));
            Assert.AreEqual(-4, errCode);
            Assert.AreEqual(0, helper.SendCalls);
        }

        [Test]
        public void Send_HelperThrows_FiresError()
        {
            var nm = NewManager();
            var helper = new FakeHelper { ThrowOnSend = true };
            int errCode = 0;
            nm.NetworkError += (s, e) => errCode = e.ErrorCode;

            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.Connect("ip", 1, null);
            ch.Send(new TestPacket(1));
            Assert.AreEqual(-5, errCode);
        }

        [Test]
        public void Close_FiresClosedAndClearsState()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            bool closedFired = false;
            nm.NetworkClosed += (s, e) => closedFired = true;

            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.Connect("ip", 1, null);
            ch.Close();
            Assert.IsTrue(closedFired);
            Assert.IsFalse(ch.Connected);
        }

        [Test]
        public void NotifyReceived_FiresEventAndIncrementsCounter()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            Packet receivedPacket = null;
            nm.NetworkPacketReceived += (s, e) => receivedPacket = e.Packet;

            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.Connect("ip", 1, null);
            var p = new TestPacket(42);
            ch.NotifyReceived(p);

            Assert.AreSame(p, receivedPacket);
            Assert.AreEqual(1, ch.ReceivePacketCount);
        }

        [Test]
        public void NotifyCallbacks_FromWorkerThread_AreDeferredUntilUpdate()
        {
            Framework.MarkMainThread();
            int mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var nm = NewManager();
            var helper = new FakeHelper();
            var ch = nm.CreateNetworkChannel("Main", helper);
            var packet = new TestPacket(42);
            int connected = 0;
            int closed = 0;
            int errors = 0;
            int received = 0;
            int reconnects = 0;
            int callbackThreadId = -1;
            nm.NetworkConnected += (_, __) => { connected++; callbackThreadId = Thread.CurrentThread.ManagedThreadId; };
            nm.NetworkClosed += (_, __) => { closed++; callbackThreadId = Thread.CurrentThread.ManagedThreadId; };
            nm.NetworkError += (_, __) => { errors++; callbackThreadId = Thread.CurrentThread.ManagedThreadId; };
            nm.NetworkPacketReceived += (_, __) => { received++; callbackThreadId = Thread.CurrentThread.ManagedThreadId; };
            nm.NetworkReconnect += (_, __) => { reconnects++; callbackThreadId = Thread.CurrentThread.ManagedThreadId; };

            var worker = new Thread(() =>
            {
                ch.NotifyConnected(null);
                ch.NotifyError(99, "worker");
                ch.NotifyReceived(packet);
                ch.NotifyReconnect(NetworkReconnectPhase.Reconnecting, 1, 0.5f);
                ch.NotifyClosed();
            });
            worker.Start();
            worker.Join();

            Assert.AreEqual(0, connected + closed + errors + received + reconnects);
            Assert.IsFalse(ch.Connected);

            TickUpdate(nm, 0f);

            Assert.AreEqual(1, connected);
            Assert.AreEqual(1, closed);
            Assert.AreEqual(1, errors);
            Assert.AreEqual(1, received);
            Assert.AreEqual(1, reconnects);
            Assert.AreEqual(mainThreadId, callbackThreadId);
            Assert.IsFalse(ch.Connected);
        }

        [Test]
        public void HeartBeat_FiresSendAtInterval()
        {
            var nm = NewManager();
            var helper = new FakeHelper { HeartBeatTemplate = new TestPacket(0xFF) };
            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.HeartBeatInterval = 1f;
            ch.HeartBeatTimeout = 0f; // disable timeout
            ch.Connect("ip", 1, null);

            TickUpdate(nm, 0.5f);
            Assert.AreEqual(0, helper.SendCalls, "Should not heartbeat before interval");

            TickUpdate(nm, 0.6f); // cumulative 1.1s
            Assert.AreEqual(1, helper.SendCalls, "Should heartbeat once at interval");
        }

        [Test]
        public void HeartBeat_TimeoutClosesChannelAndFiresError()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            int errCode = 0;
            bool closed = false;
            nm.NetworkError += (s, e) => errCode = e.ErrorCode;
            nm.NetworkClosed += (s, e) => closed = true;

            var ch = nm.CreateNetworkChannel("Main", helper);
            ch.HeartBeatInterval = 0f;
            ch.HeartBeatTimeout = 1f;
            ch.Connect("ip", 1, null);

            TickUpdate(nm, 2f); // exceed timeout
            Assert.AreEqual(-9, errCode);
            Assert.IsTrue(closed);
            Assert.IsFalse(ch.Connected);
        }

        [Test]
        public void Destroy_RemovesChannel()
        {
            var nm = NewManager();
            var helper = new FakeHelper();
            nm.CreateNetworkChannel("A", helper);
            Assert.IsTrue(nm.HasNetworkChannel("A"));
            Assert.IsTrue(nm.DestroyNetworkChannel("A"));
            Assert.IsFalse(nm.HasNetworkChannel("A"));
        }

        [Test]
        public void Duplicate_Create_Throws()
        {
            var nm = NewManager();
            nm.CreateNetworkChannel("A", new FakeHelper());
            Assert.Throws<FrameworkException>(() => nm.CreateNetworkChannel("A", new FakeHelper()));
        }
    }
}
