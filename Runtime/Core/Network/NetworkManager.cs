//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core.Network
{
    /// <summary>
    /// 重连退避计算（纯函数，无 socket / 无 Unity 依赖，便于单元测试）。
    /// 退避序列：base, base*2, base*4 ... 上限 maxDelay；attempt 从 0 开始（attempt 0 == base）。
    /// </summary>
    public static class ReconnectBackoff
    {
        /// <summary>
        /// 计算第 attempt 次重连前的等待秒数（指数退避并钳制到 maxDelay）。
        /// attempt 0 返回 baseDelay；非法入参做安全钳制，永不抛异常、永不返回负值。
        /// </summary>
        public static float NextDelaySeconds(int attempt, float baseDelay, float maxDelay)
        {
            if (attempt < 0) attempt = 0;
            if (baseDelay < 0f) baseDelay = 0f;
            if (maxDelay < baseDelay) maxDelay = baseDelay;

            // 2^attempt 用 double 计算避免溢出；attempt 较大时直接钳到 maxDelay。
            double factor = attempt >= 30 ? double.PositiveInfinity : (1L << attempt);
            double delay = baseDelay * factor;
            if (double.IsNaN(delay) || delay > maxDelay) delay = maxDelay;
            if (delay < 0d) delay = 0d;
            return (float)delay;
        }
    }

    /// <summary>
    /// 频道重连策略（可选，默认关闭）。值类型，复制即生效。
    /// </summary>
    public struct ReconnectPolicy
    {
        /// <summary>是否启用自动重连。</summary>
        public bool Enabled;
        /// <summary>首次退避秒数（attempt 0）。</summary>
        public float BaseDelaySeconds;
        /// <summary>退避上限秒数。</summary>
        public float MaxDelaySeconds;
        /// <summary>最大重连尝试次数；&lt;=0 表示无限重试。</summary>
        public int MaxAttempts;

        /// <summary>默认策略：关闭，0.5s 起，30s 封顶，最多 10 次。</summary>
        public static ReconnectPolicy Default => new ReconnectPolicy
        {
            Enabled = false,
            BaseDelaySeconds = 0.5f,
            MaxDelaySeconds = 30f,
            MaxAttempts = 10,
        };
    }

    /// <summary>
    /// 发送队列背压策略（可选）。
    /// </summary>
    public struct SendQueuePolicy
    {
        /// <summary>队列容量上限；超过则丢弃最旧/拒绝入队（带日志）。&lt;=0 视为默认上限。</summary>
        public int MaxQueuedPackets;

        /// <summary>默认上限 1024 个待发包。</summary>
        public static SendQueuePolicy Default => new SendQueuePolicy { MaxQueuedPackets = 1024 };
    }

    /// <summary>
    /// 网络管理器。
    /// 维护频道注册；驱动每个频道的 Helper.Update；通过 Notify* 方法接收 helper 回调并广播事件。
    /// </summary>
    internal sealed class NetworkManager : FrameworkModule, INetworkManager
    {
        private readonly Dictionary<string, NetworkChannel> m_Channels = new Dictionary<string, NetworkChannel>(StringComparer.Ordinal);

        // 复用的频道快照缓冲：Update/Shutdown 遍历快照而非字典本身，允许业务在回调里 Create/Destroy 频道而不抛 InvalidOperationException。
        private NetworkChannel[] m_ChannelBuffer = Array.Empty<NetworkChannel>();

        public event EventHandler<NetworkConnectedEventArgs> NetworkConnected;
        public event EventHandler<NetworkClosedEventArgs> NetworkClosed;
        public event EventHandler<NetworkErrorEventArgs> NetworkError;
        public event EventHandler<NetworkPacketReceivedEventArgs> NetworkPacketReceived;
        public event EventHandler<NetworkReconnectEventArgs> NetworkReconnect;

        // Priority 0：业务网络模块，独立于业务依赖图。
        public override int Priority { get { return 0; } }

        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            int count = SnapshotChannels();
            for (int i = 0; i < count; i++)
            {
                NetworkChannel ch = m_ChannelBuffer[i];
                try { ch.Update(elapseSeconds, realElapseSeconds); }
                catch (Exception ex) { FrameworkLog.Error("NetworkChannel.Update threw: {0}", ex); }
            }
        }

        public override void Shutdown()
        {
            int count = SnapshotChannels();
            m_Channels.Clear();
            for (int i = 0; i < count; i++)
            {
                NetworkChannel ch = m_ChannelBuffer[i];
                try { ch.Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("NetworkChannel '{0}' Shutdown threw: {1}", ch.Name, ex); }
                m_ChannelBuffer[i] = null;
            }
        }

        // 把当前频道拷进复用缓冲；返回数量。遍历快照避免迭代期字典被回调改动。
        private int SnapshotChannels()
        {
            int n = m_Channels.Count;
            if (m_ChannelBuffer.Length < n) m_ChannelBuffer = new NetworkChannel[Math.Max(4, n * 2)];
            int i = 0;
            foreach (var kv in m_Channels) m_ChannelBuffer[i++] = kv.Value;
            return i;
        }

        public int NetworkChannelCount { get { return m_Channels.Count; } }

        public bool HasNetworkChannel(string name) { return name != null && m_Channels.ContainsKey(name); }

        public INetworkChannel GetNetworkChannel(string name)
        {
            NetworkChannel c;
            return name != null && m_Channels.TryGetValue(name, out c) ? (INetworkChannel)c : null;
        }

        public INetworkChannel[] GetAllNetworkChannels()
        {
            var arr = new INetworkChannel[m_Channels.Count];
            int i = 0;
            foreach (var kv in m_Channels) arr[i++] = kv.Value;
            return arr;
        }

        public INetworkChannel CreateNetworkChannel(string name, INetworkChannelHelper helper)
        {
            Framework.EnsureMainThread(nameof(CreateNetworkChannel));
            if (string.IsNullOrEmpty(name)) throw new FrameworkException("Channel name is invalid.");
            if (helper == null) throw new FrameworkException("Channel helper is invalid.");
            if (m_Channels.ContainsKey(name)) throw new FrameworkException(Utility.Text.Format("Channel '{0}' already exists.", name));

            var ch = new NetworkChannel(name, helper, this);
            m_Channels.Add(name, ch);
            helper.Initialize(ch);
            return ch;
        }

        public bool DestroyNetworkChannel(string name)
        {
            Framework.EnsureMainThread(nameof(DestroyNetworkChannel));
            NetworkChannel c;
            if (m_Channels.TryGetValue(name, out c))
            {
                try { c.Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("NetworkChannel '{0}' Shutdown threw: {1}", name, ex); }
                m_Channels.Remove(name);
                return true;
            }
            return false;
        }

        internal void RaiseConnected(NetworkChannel c, object userData)
        {
            var h = NetworkConnected;
            if (h == null) return;
            var args = NetworkConnectedEventArgs.Create(c, userData);
            try { h(this, args); } catch (Exception e) { FrameworkLog.Error("NetworkConnected: {0}", e); }
            ReferencePool.Release(args);
        }

        internal void RaiseClosed(NetworkChannel c)
        {
            var h = NetworkClosed;
            if (h == null) return;
            var args = NetworkClosedEventArgs.Create(c);
            try { h(this, args); } catch (Exception e) { FrameworkLog.Error("NetworkClosed: {0}", e); }
            ReferencePool.Release(args);
        }

        internal void RaiseError(NetworkChannel c, int code, string msg)
        {
            var h = NetworkError;
            if (h == null) return;
            var args = NetworkErrorEventArgs.Create(c, code, msg);
            try { h(this, args); } catch (Exception e) { FrameworkLog.Error("NetworkError: {0}", e); }
            ReferencePool.Release(args);
        }

        internal void RaisePacketReceived(NetworkChannel c, Packet p)
        {
            var h = NetworkPacketReceived;
            if (h == null) return;
            var args = NetworkPacketReceivedEventArgs.Create(c, p);
            try { h(this, args); } catch (Exception e) { FrameworkLog.Error("NetworkPacketReceived: {0}", e); }
            ReferencePool.Release(args);
        }

        internal void RaiseReconnect(NetworkChannel c, NetworkReconnectPhase phase, int attempt, float delaySeconds)
        {
            var h = NetworkReconnect;
            if (h == null) return;
            var args = NetworkReconnectEventArgs.Create(c, phase, attempt, delaySeconds);
            try { h(this, args); } catch (Exception e) { FrameworkLog.Error("NetworkReconnect: {0}", e); }
            ReferencePool.Release(args);
        }

        /// <summary>
        /// 网络频道实现。
        /// 负责：心跳/超时定时；驱动 helper.Update；接收 helper Notify 回调并冒泡到 NetworkManager 广播。
        /// </summary>
        internal sealed class NetworkChannel : INetworkChannel
        {
            private readonly string m_Name;
            private readonly INetworkChannelHelper m_Helper;
            private readonly NetworkManager m_Owner;
            private readonly int m_OwnerThreadId;
            private readonly ConcurrentQueue<PendingNotification> m_PendingNotifications =
                new ConcurrentQueue<PendingNotification>();
            private bool m_Connected;
            private int m_SendCount;
            private int m_RecvCount;
            private float m_HeartBeatInterval = 30f;
            private float m_HeartBeatTimeout = 60f;
            private float m_SinceLastHeartBeat;
            private float m_SinceLastRecv;
            private ReconnectPolicy m_ReconnectPolicy = ReconnectPolicy.Default;

            public NetworkChannel(string name, INetworkChannelHelper helper, NetworkManager owner)
            {
                m_Name = name;
                m_Helper = helper;
                m_Owner = owner;
                m_OwnerThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            public string Name { get { return m_Name; } }
            public bool Connected { get { return m_Connected; } }
            public int SendPacketCount { get { return m_SendCount; } }
            public int ReceivePacketCount { get { return m_RecvCount; } }

            public float HeartBeatInterval
            {
                get { return m_HeartBeatInterval; }
                set { m_HeartBeatInterval = value; }
            }

            public float HeartBeatTimeout
            {
                get { return m_HeartBeatTimeout; }
                set { m_HeartBeatTimeout = value; }
            }

            public ReconnectPolicy ReconnectPolicy
            {
                get { return m_ReconnectPolicy; }
                set
                {
                    m_ReconnectPolicy = value;
                    // 把策略透传给支持自动重连的 Helper（不支持的 Helper 静默忽略）。
                    if (m_Helper is IReconnectableChannelHelper r)
                    {
                        try { r.SetReconnectPolicy(value); }
                        catch (Exception ex) { m_Owner.RaiseError(this, -10, "Helper.SetReconnectPolicy threw: " + ex.Message); }
                    }
                }
            }

            public void Connect(string ip, int port) { Connect(ip, port, null); }

            public void Connect(string ip, int port, object userData)
            {
                try { m_Helper.Connect(ip, port, userData); }
                catch (Exception ex) { m_Owner.RaiseError(this, -1, "Connect threw: " + ex.Message); }
            }

            public void Close()
            {
                try { m_Helper.Close(); }
                catch (Exception ex) { m_Owner.RaiseError(this, -2, "Close threw: " + ex.Message); }
            }

            public void Send<T>(T packet) where T : Packet
            {
                if (packet == null) { m_Owner.RaiseError(this, -3, "Send null packet."); return; }
                if (!m_Connected) { m_Owner.RaiseError(this, -4, "Send while not connected."); return; }
                bool ok;
                try { ok = m_Helper.Send(packet); }
                catch (Exception ex) { m_Owner.RaiseError(this, -5, "Helper.Send threw: " + ex.Message); return; }
                if (ok) m_SendCount++;
                else m_Owner.RaiseError(this, -6, "Helper.Send returned false.");
            }

            internal void Update(float elapseSeconds, float realElapseSeconds)
            {
                try { m_Helper.Update(elapseSeconds, realElapseSeconds); }
                catch (Exception ex) { m_Owner.RaiseError(this, -7, "Helper.Update threw: " + ex.Message); }

                DrainPendingNotifications();

                if (!m_Connected) return;

                if (m_HeartBeatInterval > 0f)
                {
                    m_SinceLastHeartBeat += realElapseSeconds;
                    if (m_SinceLastHeartBeat >= m_HeartBeatInterval)
                    {
                        m_SinceLastHeartBeat = 0f;
                        Packet hb = null;
                        try { hb = m_Helper.CreateHeartBeatPacket(); }
                        catch (Exception ex) { m_Owner.RaiseError(this, -7, "Helper.CreateHeartBeatPacket threw: " + ex.Message); }
                        if (hb != null)
                        {
                            try { m_Helper.Send(hb); }
                            catch (Exception ex) { m_Owner.RaiseError(this, -8, "Helper.Send heartbeat threw: " + ex.Message); }
                        }
                    }
                }

                if (m_HeartBeatTimeout > 0f)
                {
                    m_SinceLastRecv += realElapseSeconds;
                    if (m_SinceLastRecv >= m_HeartBeatTimeout)
                    {
                        m_Owner.RaiseError(this, -9, "Heart beat timed out.");
                        m_SinceLastRecv = 0f;
                        // 启用重连时把心跳超时当作意外断线（触发重连）；否则按原行为显式关闭。
                        if (m_ReconnectPolicy.Enabled && m_Helper is IReconnectableChannelHelper r)
                        {
                            try { r.DropForReconnect(); }
                            catch (Exception ex) { m_Owner.RaiseError(this, -11, "Helper.DropForReconnect threw: " + ex.Message); Close(); }
                        }
                        else
                        {
                            Close();
                        }
                    }
                }
            }

            internal void Shutdown()
            {
                try { m_Helper.Shutdown(); }
                catch (Exception ex) { FrameworkLog.Error("NetworkChannel '{0}' Helper.Shutdown threw: {1}", Name, ex); }
            }

            // ---- Helper 回调入口 ----
            public void NotifyConnected(object userData)
            {
                DispatchOrEnqueue(PendingNotification.Connected(userData));
            }

            public void NotifyClosed()
            {
                DispatchOrEnqueue(PendingNotification.Closed());
            }

            public void NotifyError(int code, string msg)
            {
                DispatchOrEnqueue(PendingNotification.Error(code, msg));
            }

            public void NotifyReceived(Packet packet)
            {
                if (packet == null) return;
                DispatchOrEnqueue(PendingNotification.Received(packet));
            }

            public void NotifyReconnect(NetworkReconnectPhase phase, int attempt, float delaySeconds)
            {
                DispatchOrEnqueue(PendingNotification.Reconnect(phase, attempt, delaySeconds));
            }

            private void DispatchOrEnqueue(PendingNotification notification)
            {
                if (Thread.CurrentThread.ManagedThreadId == m_OwnerThreadId)
                {
                    Dispatch(notification);
                }
                else
                {
                    m_PendingNotifications.Enqueue(notification);
                }
            }

            private void DrainPendingNotifications()
            {
                while (m_PendingNotifications.TryDequeue(out var notification))
                {
                    Dispatch(notification);
                }
            }

            private void Dispatch(PendingNotification notification)
            {
                switch (notification.Kind)
                {
                    case NotificationKind.Connected:
                        ApplyConnected(notification.UserData);
                        break;
                    case NotificationKind.Closed:
                        ApplyClosed();
                        break;
                    case NotificationKind.Error:
                        m_Owner.RaiseError(this, notification.ErrorCode, notification.ErrorMessage);
                        break;
                    case NotificationKind.Received:
                        ApplyReceived(notification.Packet);
                        break;
                    case NotificationKind.Reconnect:
                        m_Owner.RaiseReconnect(this, notification.ReconnectPhase, notification.Attempt, notification.DelaySeconds);
                        break;
                }
            }

            private void ApplyConnected(object userData)
            {
                m_Connected = true;
                m_SinceLastRecv = 0f;
                m_SinceLastHeartBeat = 0f;
                m_Owner.RaiseConnected(this, userData);
            }

            private void ApplyClosed()
            {
                if (!m_Connected) return;
                m_Connected = false;
                m_Owner.RaiseClosed(this);
            }

            private void ApplyReceived(Packet packet)
            {
                m_RecvCount++;
                m_SinceLastRecv = 0f;
                m_Owner.RaisePacketReceived(this, packet);
            }

            private enum NotificationKind
            {
                Connected,
                Closed,
                Error,
                Received,
                Reconnect,
            }

            private readonly struct PendingNotification
            {
                public readonly NotificationKind Kind;
                public readonly object UserData;
                public readonly int ErrorCode;
                public readonly string ErrorMessage;
                public readonly Packet Packet;
                public readonly NetworkReconnectPhase ReconnectPhase;
                public readonly int Attempt;
                public readonly float DelaySeconds;

                private PendingNotification(
                    NotificationKind kind,
                    object userData = null,
                    int errorCode = 0,
                    string errorMessage = null,
                    Packet packet = null,
                    NetworkReconnectPhase reconnectPhase = NetworkReconnectPhase.Reconnecting,
                    int attempt = 0,
                    float delaySeconds = 0f)
                {
                    Kind = kind;
                    UserData = userData;
                    ErrorCode = errorCode;
                    ErrorMessage = errorMessage;
                    Packet = packet;
                    ReconnectPhase = reconnectPhase;
                    Attempt = attempt;
                    DelaySeconds = delaySeconds;
                }

                public static PendingNotification Connected(object userData) =>
                    new PendingNotification(NotificationKind.Connected, userData: userData);
                public static PendingNotification Closed() =>
                    new PendingNotification(NotificationKind.Closed);
                public static PendingNotification Error(int code, string message) =>
                    new PendingNotification(NotificationKind.Error, errorCode: code, errorMessage: message);
                public static PendingNotification Received(Packet packet) =>
                    new PendingNotification(NotificationKind.Received, packet: packet);
                public static PendingNotification Reconnect(NetworkReconnectPhase phase, int attempt, float delaySeconds) =>
                    new PendingNotification(
                        NotificationKind.Reconnect,
                        reconnectPhase: phase,
                        attempt: attempt,
                        delaySeconds: delaySeconds);
            }
        }
    }
}
