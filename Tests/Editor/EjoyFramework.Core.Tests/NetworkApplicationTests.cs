//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Network;
using EjoyFramework.Core.Network.Encryption;
using EjoyFramework.Core.Network.Rpc;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class NetworkApplicationTests
    {
        // ===== PacketRegistry =====

        [Test]
        public void PacketRegistry_DispatchesToCorrectHandler()
        {
            var reg = new PacketRegistry();
            int gotA = 0, gotB = 0;
            reg.Register(new LambdaHandler(1001, _ => gotA++));
            reg.Register(new LambdaHandler(1002, _ => gotB++));
            reg.Dispatch(new MockPacket(1001), null);
            reg.Dispatch(new MockPacket(1002), null);
            reg.Dispatch(new MockPacket(1001), null);
            Assert.AreEqual(2, gotA);
            Assert.AreEqual(1, gotB);
        }

        [Test]
        public void PacketRegistry_DuplicateId_Throws()
        {
            var reg = new PacketRegistry();
            reg.Register(new LambdaHandler(7, _ => { }));
            Assert.Throws<FrameworkException>(() => reg.Register(new LambdaHandler(7, _ => { })));
        }

        [Test]
        public void PacketRegistry_UnknownPacket_NoThrow()
        {
            var reg = new PacketRegistry();
            Assert.IsFalse(reg.Dispatch(new MockPacket(9999), null));
        }

        // ===== RpcManager =====

        [Test]
        public void Rpc_Call_AssignsSequence_AndSends()
        {
            var ch = new FakeChannel();
            var rpc = new RpcManager();
            int seq = rpc.Call<MockPacket>(ch, new MockPacket(100), replyPacketId: 200, onReply: _ => { });
            Assert.AreNotEqual(0, seq);
            Assert.AreEqual(seq, ch.LastSent.Sequence);
            Assert.AreEqual(1, rpc.PendingCount);
        }

        [Test]
        public void Rpc_TryResolveReply_TriggersCallback_AndClearsPending()
        {
            var ch = new FakeChannel();
            var rpc = new RpcManager();
            MockPacket gotReply = null;
            int seq = rpc.Call<MockPacket>(ch, new MockPacket(100), 200, p => gotReply = p);

            var reply = new MockPacket(200) { Sequence = seq };
            Assert.IsTrue(rpc.TryResolveReply(reply));
            Assert.AreSame(reply, gotReply);
            Assert.AreEqual(0, rpc.PendingCount);
        }

        [Test]
        public void Rpc_ReplyPacketIdMismatch_DoesNotResolve()
        {
            var ch = new FakeChannel();
            var rpc = new RpcManager();
            int seq = rpc.Call<MockPacket>(ch, new MockPacket(100), 200, _ => Assert.Fail("should not fire"));
            // 错误 packet id 不应消费 pending
            var wrong = new MockPacket(999) { Sequence = seq };
            Assert.IsFalse(rpc.TryResolveReply(wrong));
            Assert.AreEqual(1, rpc.PendingCount);
        }

        [Test]
        public void Rpc_Timeout_FiresAfterAccumulatedTime()
        {
            var ch = new FakeChannel();
            var rpc = new RpcManager();
            bool timedOut = false;
            rpc.Call<MockPacket>(ch, new MockPacket(100), 200,
                onReply: _ => { },
                onTimeout: () => timedOut = true,
                timeoutSeconds: 1.0f);

            rpc.Update(0.6f);
            Assert.IsFalse(timedOut);
            rpc.Update(0.5f);
            Assert.IsTrue(timedOut);
            Assert.AreEqual(0, rpc.PendingCount);
        }

        [Test]
        public void Rpc_Call_SendThrows_RemovesPending_AndRethrows()
        {
            var ch = new ThrowingChannel();
            var rpc = new RpcManager();
            // 同步发送失败必须把异常抛给调用方，且不留下孤儿 pending 条目。
            Assert.Throws<FrameworkException>(() =>
                rpc.Call<MockPacket>(ch, new MockPacket(100), 200, _ => Assert.Fail("should not fire")));
            Assert.AreEqual(0, rpc.PendingCount);
        }

        [Test]
        public void Rpc_CallAsync_SendThrows_FaultsTask_AndRemovesPending()
        {
            var ch = new ThrowingChannel();
            var rpc = new RpcManager();
            var task = rpc.CallAsync<MockPacket>(ch, new MockPacket(100), 200);
            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<FrameworkException>(task.Exception.InnerException);
            Assert.AreEqual(0, rpc.PendingCount);
        }

        // ===== AesPacketCrypto =====

        [Test]
        public void AesPacketCrypto_RoundTrip_Decrypts()
        {
            byte[] key = NewBytes(32, 1), hmac = NewBytes(32, 2);
            var crypto = new AesPacketCrypto(key, hmac);
            byte[] plain = System.Text.Encoding.UTF8.GetBytes("hello world this is secret");
            byte[] env = crypto.Encrypt(plain);
            byte[] back = crypto.Decrypt(env);
            CollectionAssert.AreEqual(plain, back);
        }

        [Test]
        public void AesPacketCrypto_TamperedEnvelope_RejectedByHmac()
        {
            byte[] key = NewBytes(32, 3), hmac = NewBytes(32, 4);
            var crypto = new AesPacketCrypto(key, hmac);
            byte[] env = crypto.Encrypt(System.Text.Encoding.UTF8.GetBytes("payload"));
            env[20] ^= 0xFF;   // 翻一个字节
            Assert.Throws<FrameworkException>(() => crypto.Decrypt(env));
        }

        [Test]
        public void AesPacketCrypto_WrongHmacKey_RejectsDecryption()
        {
            var c1 = new AesPacketCrypto(NewBytes(32, 5), NewBytes(32, 6));
            byte[] env = c1.Encrypt(System.Text.Encoding.UTF8.GetBytes("x"));
            var c2 = new AesPacketCrypto(NewBytes(32, 5), NewBytes(32, 99));
            Assert.Throws<FrameworkException>(() => c2.Decrypt(env));
        }

        // ===== helpers =====

        private static byte[] NewBytes(int n, byte seed)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(seed + i);
            return b;
        }

        private sealed class MockPacket : Packet
        {
            private readonly int m_Id;
            public MockPacket(int id) { m_Id = id; }
            public override int Id => m_Id;
            public override void Clear() { }
        }

        private sealed class LambdaHandler : IPacketHandler
        {
            private readonly Action<Packet> m_Action;
            public LambdaHandler(int id, Action<Packet> action) { PacketId = id; m_Action = action; }
            public int PacketId { get; }
            public void Handle(Packet packet, INetworkChannel channel) => m_Action(packet);
        }

        private sealed class FakeChannel : INetworkChannel
        {
            public Packet LastSent;
            public string Name => "fake";
            public bool Connected => true;
            public int SendPacketCount => 0;
            public int ReceivePacketCount => 0;
            public float HeartBeatInterval { get; set; } = 0f;
            public float HeartBeatTimeout { get; set; } = 0f;
            public void Connect(string ipAddress, int port) { }
            public void Connect(string ipAddress, int port, object userData) { }
            public void Close() { }
            public void Send<T>(T packet) where T : Packet { LastSent = packet; }
            public void NotifyConnected(object userData) { }
            public void NotifyClosed() { }
            public void NotifyError(int errorCode, string errorMessage) { }
            public void NotifyReceived(Packet packet) { }
            public ReconnectPolicy ReconnectPolicy { get; set; }
            public void NotifyReconnect(NetworkReconnectPhase phase, int attempt, float delaySeconds) { }
        }

        // Send 同步抛出，用于验证 RpcManager 不会留下孤儿 pending 条目。
        private sealed class ThrowingChannel : INetworkChannel
        {
            public string Name => "throwing";
            public bool Connected => true;
            public int SendPacketCount => 0;
            public int ReceivePacketCount => 0;
            public float HeartBeatInterval { get; set; } = 0f;
            public float HeartBeatTimeout { get; set; } = 0f;
            public void Connect(string ipAddress, int port) { }
            public void Connect(string ipAddress, int port, object userData) { }
            public void Close() { }
            public void Send<T>(T packet) where T : Packet => throw new FrameworkException("send failed.");
            public void NotifyConnected(object userData) { }
            public void NotifyClosed() { }
            public void NotifyError(int errorCode, string errorMessage) { }
            public void NotifyReceived(Packet packet) { }
            public ReconnectPolicy ReconnectPolicy { get; set; }
            public void NotifyReconnect(NetworkReconnectPhase phase, int attempt, float delaySeconds) { }
        }
    }
}
