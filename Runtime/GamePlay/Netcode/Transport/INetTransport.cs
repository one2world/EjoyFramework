//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Netcode.Transport
{
    /// <summary>
    /// 消息投递方式。统一传输层的所有实现都接收此参数：
    /// <para><see cref="ReliableOrdered"/>：可靠且有序（TCP 语义）。</para>
    /// <para><see cref="Unreliable"/>：不可靠、不保证顺序、不保证送达。</para>
    /// <para><see cref="UnreliableSequenced"/>：不可靠但丢弃过期包，只保留最新（不保证送达）。</para>
    /// TCP 实现会把所有方式都视为 <see cref="ReliableOrdered"/>；未来的 UDP / KCP 实现才会真正区分。
    /// </summary>
    public enum NetDeliveryMethod
    {
        /// <summary>可靠且有序（TCP 默认语义）。</summary>
        ReliableOrdered = 0,

        /// <summary>不可靠、无序、不保证送达。</summary>
        Unreliable = 1,

        /// <summary>不可靠但有序丢旧：过期包被丢弃，仅保留最新。</summary>
        UnreliableSequenced = 2,
    }

    /// <summary>
    /// 客户端角色的连接状态。
    /// </summary>
    public enum NetConnectionState
    {
        /// <summary>未连接（初始状态或已断开）。</summary>
        Disconnected = 0,

        /// <summary>正在连接中。</summary>
        Connecting = 1,

        /// <summary>已连接。</summary>
        Connected = 2,
    }

    /// <summary>
    /// 统一网络传输接口——所有传输实现（Loopback / TCP / 未来的 UDP / KCP）都实现它。
    /// 设计目标：上层只依赖本接口，底层从 TCP 切到 UDP / KCP 时上层零改动。
    /// <para>
    /// 一个实例只扮演服务器<b>或</b>客户端其中一种角色（取决于先调用了 <see cref="StartServer"/> 还是 <see cref="Connect"/>）。
    /// </para>
    /// <para>
    /// 线程模型：所有事件（<see cref="OnClientConnected"/> / <see cref="OnClientDisconnected"/> / <see cref="OnData"/>）
    /// 只在调用 <see cref="Poll"/> 的线程上触发，绝不会从后台线程触发。因此上层处理器始终运行在调用 <see cref="Poll"/> 的线程（通常是游戏主线程）。
    /// </para>
    /// </summary>
    public interface INetTransport
    {
        /// <summary>当前实例是否处于服务器角色。</summary>
        bool IsServer { get; }

        /// <summary>当前实例是否处于客户端角色。</summary>
        bool IsClient { get; }

        /// <summary>客户端角色下的连接状态；服务器角色下无意义。</summary>
        NetConnectionState ClientState { get; }

        /// <summary>
        /// 以服务器角色在指定端口启动监听。
        /// </summary>
        /// <param name="port">监听端口。</param>
        void StartServer(int port);

        /// <summary>
        /// 停止服务器：断开所有连接、关闭监听、回收线程，恢复到未启动状态。
        /// </summary>
        void StopServer();

        /// <summary>
        /// 以客户端角色连接到指定主机与端口。
        /// </summary>
        /// <param name="host">主机地址。</param>
        /// <param name="port">端口。</param>
        void Connect(string host, int port);

        /// <summary>
        /// 客户端角色下主动断开与服务器的连接。
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 服务器角色下主动断开（踢出）指定连接。
        /// <para>用于上层在检测到该连接的协议错误 / 恶意负载时将其逐出。</para>
        /// <para>断开会像对端正常断开一样，最终经 <see cref="Poll"/> 触发一次 <see cref="OnClientDisconnected"/>。</para>
        /// <para>非服务器角色、或 <paramref name="connectionId"/> 不存在时为空操作（幂等，不抛异常）。</para>
        /// </summary>
        /// <param name="connectionId">待踢出的连接标识。</param>
        void Kick(int connectionId);

        /// <summary>
        /// 发送数据。
        /// <para>服务器角色：发送给指定 <paramref name="connectionId"/> 的单个连接。</para>
        /// <para>客户端角色：忽略 <paramref name="connectionId"/>，发送给服务器。</para>
        /// </summary>
        /// <param name="connectionId">目标连接标识（客户端角色下忽略）。</param>
        /// <param name="data">待发送的数据段；调用返回后调用方可复用该缓冲区。</param>
        /// <param name="method">投递方式；TCP 一律按可靠有序处理。</param>
        void Send(int connectionId, ArraySegment<byte> data, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered);

        /// <summary>
        /// 服务器角色下，向所有已连接客户端广播数据。
        /// </summary>
        /// <param name="data">待发送的数据段；调用返回后调用方可复用该缓冲区。</param>
        /// <param name="method">投递方式；TCP 一律按可靠有序处理。</param>
        void Broadcast(ArraySegment<byte> data, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered);

        /// <summary>
        /// 在调用线程上排空已排队的网络事件，依序触发下方的事件（使处理器运行在游戏线程）。
        /// </summary>
        void Poll();

        /// <summary>
        /// 关闭一切：停止服务器 / 断开客户端、关闭套接字与线程，恢复到未连接状态。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 新连接建立时触发。
        /// <para>服务器角色：参数为新连接的 connectionId。</para>
        /// <para>客户端角色：连接成功后触发一次，参数为本地 connectionId（例如 0）。</para>
        /// </summary>
        event Action<int> OnClientConnected;

        /// <summary>
        /// 连接断开时触发，参数为断开的 connectionId。
        /// </summary>
        event Action<int> OnClientDisconnected;

        /// <summary>
        /// 收到数据时触发，参数为 (connectionId, payload)。
        /// <para><b>payload 仅在回调期间有效</b>，如需保留请在回调内自行复制。</para>
        /// </summary>
        event Action<int, ArraySegment<byte>> OnData;
    }
}
