//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Network;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// Network 重连/退避回归。
    /// ReconnectBackoff 是纯函数（无 socket、无 Unity 依赖），可直接单测退避进度、封顶与 attempt 0 == base。
    /// 另验证 ReconnectPolicy 默认关闭以及 NotifyReconnect 事件链路。
    /// </summary>
    public class NetworkReconnectTests
    {
        private const float Eps = 1e-4f;

        // ---- ReconnectBackoff 纯函数 ----

        [Test]
        public void Backoff_Attempt0_EqualsBase()
        {
            Assert.AreEqual(0.5f, ReconnectBackoff.NextDelaySeconds(0, 0.5f, 30f), Eps);
            Assert.AreEqual(1f, ReconnectBackoff.NextDelaySeconds(0, 1f, 30f), Eps);
        }

        [Test]
        public void Backoff_DoublesEachAttempt()
        {
            // 0.5, 1, 2, 4, 8 ...
            Assert.AreEqual(0.5f, ReconnectBackoff.NextDelaySeconds(0, 0.5f, 1000f), Eps);
            Assert.AreEqual(1f, ReconnectBackoff.NextDelaySeconds(1, 0.5f, 1000f), Eps);
            Assert.AreEqual(2f, ReconnectBackoff.NextDelaySeconds(2, 0.5f, 1000f), Eps);
            Assert.AreEqual(4f, ReconnectBackoff.NextDelaySeconds(3, 0.5f, 1000f), Eps);
            Assert.AreEqual(8f, ReconnectBackoff.NextDelaySeconds(4, 0.5f, 1000f), Eps);
        }

        [Test]
        public void Backoff_ClampsToMaxDelay()
        {
            // base 0.5 翻倍很快超过上限 4 → 之后恒为 4。
            Assert.AreEqual(4f, ReconnectBackoff.NextDelaySeconds(4, 0.5f, 4f), Eps);   // 8 -> 钳到 4
            Assert.AreEqual(4f, ReconnectBackoff.NextDelaySeconds(10, 0.5f, 4f), Eps);
            Assert.AreEqual(4f, ReconnectBackoff.NextDelaySeconds(100, 0.5f, 4f), Eps);
        }

        [Test]
        public void Backoff_LargeAttempt_DoesNotOverflowAndStaysCapped()
        {
            // attempt 远大于 30：内部直接钳到 maxDelay，不应溢出/返回负值。
            float d = ReconnectBackoff.NextDelaySeconds(1000, 0.5f, 30f);
            Assert.AreEqual(30f, d, Eps);
        }

        [Test]
        public void Backoff_NegativeAttempt_TreatedAsZero()
        {
            Assert.AreEqual(0.5f, ReconnectBackoff.NextDelaySeconds(-5, 0.5f, 30f), Eps);
        }

        [Test]
        public void Backoff_MaxLessThanBase_ClampsToBase()
        {
            // maxDelay < baseDelay 视为 base（不返回比 base 更小的值）。
            Assert.AreEqual(2f, ReconnectBackoff.NextDelaySeconds(0, 2f, 1f), Eps);
        }

        [Test]
        public void Backoff_NeverNegative()
        {
            Assert.GreaterOrEqual(ReconnectBackoff.NextDelaySeconds(0, 0f, 0f), 0f);
            Assert.GreaterOrEqual(ReconnectBackoff.NextDelaySeconds(5, -1f, -1f), 0f);
        }

        // ---- ReconnectPolicy 默认值 ----

        [Test]
        public void ReconnectPolicy_DefaultIsDisabled()
        {
            var p = ReconnectPolicy.Default;
            Assert.IsFalse(p.Enabled, "Reconnect must be OFF by default for test compatibility.");
            Assert.AreEqual(0.5f, p.BaseDelaySeconds, Eps);
            Assert.AreEqual(30f, p.MaxDelaySeconds, Eps);
            Assert.AreEqual(10, p.MaxAttempts);
        }

        // ---- Channel 默认策略 + NotifyReconnect 链路 ----

        private static INetworkManager NewManager()
        {
            var t = typeof(INetworkManager).Assembly.GetType("EjoyFramework.Core.Network.NetworkManager");
            return (INetworkManager)Activator.CreateInstance(t, true);
        }

        private sealed class FakeHelper : INetworkChannelHelper
        {
            public INetworkChannel Channel;
            public void Initialize(INetworkChannel channel) { Channel = channel; }
            public void Connect(string ip, int port, object userData) { Channel.NotifyConnected(userData); }
            public void Close() { Channel.NotifyClosed(); }
            public bool Send<T>(T packet) where T : Packet => true;
            public void Update(float a, float b) { }
            public Packet CreateHeartBeatPacket() => null;
            public void Shutdown() { }
        }

        [Test]
        public void Channel_ReconnectPolicy_DefaultsOff()
        {
            var nm = NewManager();
            var ch = nm.CreateNetworkChannel("Main", new FakeHelper());
            Assert.IsFalse(ch.ReconnectPolicy.Enabled);
        }

        [Test]
        public void Channel_ReconnectPolicy_RoundTrips()
        {
            var nm = NewManager();
            var ch = nm.CreateNetworkChannel("Main", new FakeHelper());
            ch.ReconnectPolicy = new ReconnectPolicy { Enabled = true, BaseDelaySeconds = 1f, MaxDelaySeconds = 8f, MaxAttempts = 3 };
            Assert.IsTrue(ch.ReconnectPolicy.Enabled);
            Assert.AreEqual(1f, ch.ReconnectPolicy.BaseDelaySeconds, Eps);
            Assert.AreEqual(8f, ch.ReconnectPolicy.MaxDelaySeconds, Eps);
            Assert.AreEqual(3, ch.ReconnectPolicy.MaxAttempts);
        }

        [Test]
        public void NotifyReconnect_FiresEventWithPhaseAndAttempt()
        {
            var nm = NewManager();
            var ch = nm.CreateNetworkChannel("Main", new FakeHelper());
            NetworkReconnectPhase phase = NetworkReconnectPhase.Failed;
            int attempt = -1;
            float delay = -1f;
            nm.NetworkReconnect += (s, e) => { phase = e.Phase; attempt = e.Attempt; delay = e.DelaySeconds; };

            ch.NotifyReconnect(NetworkReconnectPhase.Reconnecting, 2, 4f);

            Assert.AreEqual(NetworkReconnectPhase.Reconnecting, phase);
            Assert.AreEqual(2, attempt);
            Assert.AreEqual(4f, delay, Eps);
        }
    }
}
