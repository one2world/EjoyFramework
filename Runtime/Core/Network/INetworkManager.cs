//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Network
{
    /// <summary>
    /// 网络管理器接口。
    /// </summary>
    public interface INetworkManager
    {
        int NetworkChannelCount { get; }

        event EventHandler<NetworkConnectedEventArgs> NetworkConnected;
        event EventHandler<NetworkClosedEventArgs> NetworkClosed;
        event EventHandler<NetworkErrorEventArgs> NetworkError;
        event EventHandler<NetworkPacketReceivedEventArgs> NetworkPacketReceived;

        /// <summary>自动重连生命周期通知（reconnecting / reconnected / failed）。</summary>
        event EventHandler<NetworkReconnectEventArgs> NetworkReconnect;

        bool HasNetworkChannel(string name);
        INetworkChannel GetNetworkChannel(string name);
        INetworkChannel[] GetAllNetworkChannels();

        INetworkChannel CreateNetworkChannel(string name, INetworkChannelHelper networkChannelHelper);
        bool DestroyNetworkChannel(string name);
    }

    /// <summary>
    /// 网络频道接口。
    /// </summary>
    public interface INetworkChannel
    {
        string Name { get; }
        bool Connected { get; }

        /// <summary>累计发送的包数（含队列里待发送）。</summary>
        int SendPacketCount { get; }

        /// <summary>累计已派发的接收包数。</summary>
        int ReceivePacketCount { get; }

        /// <summary>心跳发送周期（秒）。&lt;=0 表示关闭。</summary>
        float HeartBeatInterval { get; set; }

        /// <summary>心跳超时阈值（秒，从最后一次收包计算）。&lt;=0 表示关闭。</summary>
        float HeartBeatTimeout { get; set; }

        /// <summary>
        /// 自动重连策略（可选，默认关闭）。设置后由 Helper 在意外断线时按退避计划重连。
        /// 显式 Close() 会取消挂起的重连。
        /// </summary>
        ReconnectPolicy ReconnectPolicy { get; set; }

        void Connect(string ipAddress, int port);
        void Connect(string ipAddress, int port, object userData);
        void Close();
        void Send<T>(T packet) where T : Packet;

        // ---- 以下为 Helper 回调入口；业务方不要直接调用 ----
        // 可从任意线程调用：频道会将异线程通知排队，并在下一次主线程 Update 中按入队顺序派发。
        void NotifyConnected(object userData);
        void NotifyClosed();
        void NotifyError(int errorCode, string errorMessage);
        void NotifyReceived(Packet packet);

        /// <summary>Helper 回调：重连生命周期变更（reconnecting / reconnected / failed）。业务方不要直接调用。</summary>
        void NotifyReconnect(NetworkReconnectPhase phase, int attempt, float delaySeconds);
    }

    /// <summary>
    /// 自动重连生命周期阶段。
    /// </summary>
    public enum NetworkReconnectPhase
    {
        /// <summary>检测到意外断线，进入重连等待。</summary>
        Reconnecting,
        /// <summary>重连成功（随后仍会正常触发 Connected）。</summary>
        Reconnected,
        /// <summary>达到最大尝试次数仍失败，放弃重连。</summary>
        Failed,
    }

    /// <summary>
    /// 网络频道辅助器接口。
    /// 负责实际 socket I/O、序列化、跨线程派发。
    /// </summary>
    public interface INetworkChannelHelper
    {
        /// <summary>注册到频道；可缓存回调入口。</summary>
        void Initialize(INetworkChannel networkChannel);

        /// <summary>真正发起连接（实现方常用后台线程；完成后回调 channel.NotifyConnected）。</summary>
        void Connect(string ipAddress, int port, object userData);

        /// <summary>关闭底层连接（实现方应停止收发线程并最终调用 channel.NotifyClosed）。</summary>
        void Close();

        /// <summary>序列化并发送/入队一个包；返回是否成功入队。</summary>
        bool Send<T>(T packet) where T : Packet;

        /// <summary>每帧主线程更新：实现方在此 drain 接收队列并回调 channel.NotifyReceived。</summary>
        void Update(float elapseSeconds, float realElapseSeconds);

        /// <summary>构造心跳包；返回 null 表示该 helper 不支持心跳。</summary>
        Packet CreateHeartBeatPacket();

        /// <summary>关闭并清理。</summary>
        void Shutdown();
    }

    /// <summary>
    /// 可选能力接口：支持自动重连/背压的 Helper 实现。
    /// 频道在设置 <see cref="INetworkChannel.ReconnectPolicy"/> 时把策略透传给实现此接口的 Helper；
    /// 未实现者保持原有行为不变（向后兼容）。
    /// </summary>
    public interface IReconnectableChannelHelper
    {
        /// <summary>设置自动重连策略（线程安全；Connect 之前或之后调用均可）。</summary>
        void SetReconnectPolicy(ReconnectPolicy policy);

        /// <summary>
        /// 主动断开当前连接但视为“意外断线”，以便在启用重连时触发重连流程
        /// （区别于显式 Close —— 后者会取消重连）。用于心跳超时等场景。
        /// </summary>
        void DropForReconnect();
    }

    /// <summary>
    /// 网络消息包基类。Id 继承自 BaseEventArgs；业务子类 override Id 返回协议号。
    /// </summary>
    public abstract class Packet : BaseEventArgs
    {
        /// <summary>消息序号（业务层可用于幂等/重排）。</summary>
        public int Sequence { get; set; }
    }

    public sealed class NetworkConnectedEventArgs : FrameworkEventArgs
    {
        public INetworkChannel NetworkChannel { get; private set; }
        public object UserData { get; private set; }

        public override void Clear() { NetworkChannel = null; UserData = null; }
        public static NetworkConnectedEventArgs Create(INetworkChannel c, object userData)
        {
            var e = ReferencePool.Acquire<NetworkConnectedEventArgs>();
            e.NetworkChannel = c; e.UserData = userData; return e;
        }
    }

    public sealed class NetworkClosedEventArgs : FrameworkEventArgs
    {
        public INetworkChannel NetworkChannel { get; private set; }
        public override void Clear() { NetworkChannel = null; }
        public static NetworkClosedEventArgs Create(INetworkChannel c)
        {
            var e = ReferencePool.Acquire<NetworkClosedEventArgs>();
            e.NetworkChannel = c; return e;
        }
    }

    public sealed class NetworkErrorEventArgs : FrameworkEventArgs
    {
        public INetworkChannel NetworkChannel { get; private set; }
        public int ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }

        public override void Clear() { NetworkChannel = null; ErrorCode = 0; ErrorMessage = null; }
        public static NetworkErrorEventArgs Create(INetworkChannel c, int code, string msg)
        {
            var e = ReferencePool.Acquire<NetworkErrorEventArgs>();
            e.NetworkChannel = c; e.ErrorCode = code; e.ErrorMessage = msg; return e;
        }
    }

    public sealed class NetworkPacketReceivedEventArgs : FrameworkEventArgs
    {
        public INetworkChannel NetworkChannel { get; private set; }
        public Packet Packet { get; private set; }

        public override void Clear() { NetworkChannel = null; Packet = null; }
        public static NetworkPacketReceivedEventArgs Create(INetworkChannel c, Packet p)
        {
            var e = ReferencePool.Acquire<NetworkPacketReceivedEventArgs>();
            e.NetworkChannel = c; e.Packet = p; return e;
        }
    }

    public sealed class NetworkReconnectEventArgs : FrameworkEventArgs
    {
        public INetworkChannel NetworkChannel { get; private set; }
        public NetworkReconnectPhase Phase { get; private set; }

        /// <summary>第几次重连尝试（从 0 开始）。</summary>
        public int Attempt { get; private set; }

        /// <summary>本次尝试前的退避等待秒数。</summary>
        public float DelaySeconds { get; private set; }

        public override void Clear() { NetworkChannel = null; Phase = NetworkReconnectPhase.Reconnecting; Attempt = 0; DelaySeconds = 0f; }
        public static NetworkReconnectEventArgs Create(INetworkChannel c, NetworkReconnectPhase phase, int attempt, float delaySeconds)
        {
            var e = ReferencePool.Acquire<NetworkReconnectEventArgs>();
            e.NetworkChannel = c; e.Phase = phase; e.Attempt = attempt; e.DelaySeconds = delaySeconds; return e;
        }
    }
}
