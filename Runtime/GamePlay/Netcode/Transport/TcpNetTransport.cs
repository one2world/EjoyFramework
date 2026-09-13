//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using EjoyFramework.Core;

namespace EjoyFramework.GamePlay.Netcode.Transport
{
    /// <summary>
    /// 基于 <see cref="System.Net.Sockets"/> 的真实 TCP 传输实现。
    /// <para>
    /// 帧格式：<b>4 字节大端长度前缀 + 负载</b>。服务器在后台线程接受连接，每个连接有独立接收线程；
    /// 客户端有独立接收线程。所有“连接 / 断开 / 数据”事件都进入线程安全队列，<b>仅在 <see cref="Poll"/></b>
    /// 中于调用线程触发——绝不从后台线程触发事件。<see cref="Send"/> / <see cref="Broadcast"/> 线程安全
    /// （按连接对发送加锁）。
    /// </para>
    /// <para>
    /// 关闭语义：<see cref="Shutdown"/> / <see cref="Disconnect"/> / <see cref="StopServer"/> 通过“运行标记 + 关闭套接字”
    /// 解除阻塞读取来停止线程（不使用 Thread.Abort），并吞掉拆除期间预期的 ObjectDisposed / IO 异常。
    /// 所有 <see cref="NetDeliveryMethod"/> 一律按可靠有序处理。绑定 / 连接错误会转化为一次断开事件，不跨线程抛出。
    /// </para>
    /// </summary>
    public sealed class TcpNetTransport : INetTransport
    {
        // 事件种类。
        private enum EventKind
        {
            Connected,
            Disconnected,
            Data,
        }

        // 线程安全队列中的一条事件。
        private readonly struct QueuedEvent
        {
            public readonly EventKind Kind;
            public readonly int ConnectionId;
            public readonly byte[] Payload;

            public QueuedEvent(EventKind kind, int connectionId, byte[] payload)
            {
                Kind = kind;
                ConnectionId = connectionId;
                Payload = payload;
            }
        }

        // 单个连接的运行时状态（服务器侧每客户端一份，或客户端侧自身一份）。
        private sealed class Connection
        {
            public int Id;
            public TcpClient Client;
            public NetworkStream Stream;
            public Thread ReceiveThread;

            // 发送锁：保证同一连接上的写入串行化，避免帧交错。
            public readonly object SendLock = new object();
        }

        private const int LengthPrefixSize = 4;

        // 单条消息允许的最大负载（防御异常长度前缀，避免一次性分配过大内存）。
        private const int MaxMessageLength = 16 * 1024 * 1024;

        // 全局已入队 Data 负载字节总量上限（DoS 防御）：当生产端（后台接收线程）远快于消费端
        // （Poll）时，避免事件队列无界膨胀耗尽内存。超过上限的连接将被丢弃而非继续入队。
        private const long MaxQueuedBytes = 64 * 1024 * 1024;

        private readonly ConcurrentQueue<QueuedEvent> m_EventQueue = new ConcurrentQueue<QueuedEvent>();

        // 当前已入队但尚未被 Poll 消费的 Data 负载字节总量（仅统计 Data 的 payload 长度）。
        // 入队前 Interlocked 累加并校验，出队（Poll）时 Interlocked 递减，保证跨线程一致。
        private long m_QueuedBytes;

        // 服务器侧：connectionId -> 连接。客户端侧仅用 m_ClientConnection。
        private readonly Dictionary<int, Connection> m_Connections = new Dictionary<int, Connection>();
        private readonly object m_ConnectionsLock = new object();

        private TcpListener m_Listener;
        private Thread m_AcceptThread;
        private Connection m_ClientConnection;

        private volatile bool m_IsServer;
        private volatile bool m_IsClient;
        private volatile bool m_Running;
        private volatile NetConnectionState m_ClientState = NetConnectionState.Disconnected;

        private int m_NextConnectionId;

        /// <inheritdoc />
        public bool IsServer
        {
            get { return m_IsServer; }
        }

        /// <inheritdoc />
        public bool IsClient
        {
            get { return m_IsClient; }
        }

        /// <inheritdoc />
        public NetConnectionState ClientState
        {
            get { return m_ClientState; }
        }

        /// <inheritdoc />
        public event Action<int> OnClientConnected;

        /// <inheritdoc />
        public event Action<int> OnClientDisconnected;

        /// <inheritdoc />
        public event Action<int, ArraySegment<byte>> OnData;

        /// <inheritdoc />
        public void StartServer(int port)
        {
            if (m_Running)
            {
                return;
            }

            m_IsServer = true;
            m_Running = true;

            try
            {
                m_Listener = new TcpListener(IPAddress.Any, port);
                m_Listener.Start();
            }
            catch (Exception)
            {
                // 绑定失败：复位状态，不向调用线程抛出。
                m_Running = false;
                m_IsServer = false;
                SafeStopListener();
                return;
            }

            m_AcceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "TcpNetTransport.Accept",
            };
            m_AcceptThread.Start();
        }

        /// <inheritdoc />
        public void StopServer()
        {
            if (!m_IsServer)
            {
                return;
            }

            m_Running = false;

            // 关闭监听以解除 AcceptTcpClient 阻塞。
            SafeStopListener();

            // 关闭所有连接（关闭套接字会解除各接收线程的阻塞读取）。
            List<Connection> snapshot;
            lock (m_ConnectionsLock)
            {
                snapshot = new List<Connection>(m_Connections.Values);
                m_Connections.Clear();
            }

            foreach (Connection conn in snapshot)
            {
                CloseConnectionSocket(conn);
            }

            JoinThread(m_AcceptThread);
            m_AcceptThread = null;

            foreach (Connection conn in snapshot)
            {
                JoinThread(conn.ReceiveThread);
            }

            m_IsServer = false;
        }

        /// <summary>
        /// 以客户端角色连接到指定主机与端口。
        /// <para>
        /// <b>非阻塞</b>：实际的 TCP 连接在一次性后台线程上完成（不在调用线程上阻塞）。调用返回后
        /// <see cref="ClientState"/> 立即为 <see cref="NetConnectionState.Connecting"/>；连接成功后入队一次
        /// <see cref="OnClientConnected"/>（connId=0）并启动接收线程，失败则入队一次 <see cref="OnClientDisconnected"/>
        /// 并复位为非客户端角色——两者都仅经 <see cref="Poll"/> 在调用线程触发。
        /// </para>
        /// </summary>
        /// <param name="host">主机地址。</param>
        /// <param name="port">端口。</param>
        public void Connect(string host, int port)
        {
            if (m_IsServer || m_Running)
            {
                return;
            }

            m_IsClient = true;
            m_Running = true;
            m_ClientState = NetConnectionState.Connecting;

            // 不在调用线程上做阻塞 connect：起一个一次性后台线程完成连接（镜像 AcceptLoop 的线程模型）。
            Thread connectThread = new Thread(() => ConnectLoop(host, port))
            {
                IsBackground = true,
                Name = "TcpNetTransport.Connect",
            };
            connectThread.Start();
        }

        // 一次性后台连接线程：阻塞地完成 TCP 连接；成功则入队 Connected 并启动接收线程，失败则入队 Disconnected 并复位角色。
        private void ConnectLoop(string host, int port)
        {
            TcpClient client;
            try
            {
                client = new TcpClient();
                client.NoDelay = true;
                client.Connect(host, port);
            }
            catch (Exception)
            {
                // 连接失败：复位（含清除客户端角色），并入队一次断开事件（connId 用 0，与客户端本地 connId 约定一致）。
                m_Running = false;
                m_IsClient = false;
                m_ClientState = NetConnectionState.Disconnected;
                m_EventQueue.Enqueue(new QueuedEvent(EventKind.Disconnected, 0, null));
                return;
            }

            // 连接发起期间若已被拆除（Disconnect/Shutdown），不再继续：关闭刚建立的套接字并入队一次断开。
            if (!m_Running)
            {
                SafeCloseClient(client);
                m_IsClient = false;
                m_ClientState = NetConnectionState.Disconnected;
                m_EventQueue.Enqueue(new QueuedEvent(EventKind.Disconnected, 0, null));
                return;
            }

            Connection conn = new Connection
            {
                Id = 0, // 客户端本地 connId 约定为 0。
                Client = client,
                Stream = client.GetStream(),
            };
            m_ClientConnection = conn;
            m_ClientState = NetConnectionState.Connected;

            // 入队“已连接(0)”，供 Poll 时触发。
            m_EventQueue.Enqueue(new QueuedEvent(EventKind.Connected, 0, null));

            conn.ReceiveThread = new Thread(() => ReceiveLoop(conn, isServerSide: false))
            {
                IsBackground = true,
                Name = "TcpNetTransport.ClientReceive",
            };
            conn.ReceiveThread.Start();
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            if (!m_IsClient)
            {
                return;
            }

            m_Running = false;
            m_ClientState = NetConnectionState.Disconnected;

            Connection conn = m_ClientConnection;
            m_ClientConnection = null;

            if (conn != null)
            {
                CloseConnectionSocket(conn);
                JoinThread(conn.ReceiveThread);
            }

            m_IsClient = false;
        }

        /// <inheritdoc />
        public void Kick(int connectionId)
        {
            if (!m_IsServer)
            {
                return;
            }

            Connection conn;
            lock (m_ConnectionsLock)
            {
                m_Connections.TryGetValue(connectionId, out conn);
            }

            if (conn == null)
            {
                return;
            }

            // 关闭套接字即可：该连接的接收线程会解除阻塞、走 HandleConnectionClosed，
            // 从表中移除并入队一次 Disconnected（经 Poll 在调用线程触发），无需在此重复入队。
            CloseConnectionSocket(conn);
        }

        /// <inheritdoc />
        public void Send(int connectionId, ArraySegment<byte> data, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
        {
            if (m_IsServer)
            {
                Connection conn;
                lock (m_ConnectionsLock)
                {
                    m_Connections.TryGetValue(connectionId, out conn);
                }

                if (conn != null)
                {
                    SendFramed(conn, data);
                }

                return;
            }

            if (m_IsClient && m_ClientState == NetConnectionState.Connected)
            {
                Connection conn = m_ClientConnection;
                if (conn != null)
                {
                    SendFramed(conn, data);
                }
            }
        }

        /// <inheritdoc />
        public void Broadcast(ArraySegment<byte> data, NetDeliveryMethod method = NetDeliveryMethod.ReliableOrdered)
        {
            if (!m_IsServer)
            {
                return;
            }

            // 快照后再发送，避免在持锁期间执行阻塞写。
            List<Connection> snapshot;
            lock (m_ConnectionsLock)
            {
                snapshot = new List<Connection>(m_Connections.Values);
            }

            foreach (Connection conn in snapshot)
            {
                SendFramed(conn, data);
            }
        }

        /// <inheritdoc />
        public void Poll()
        {
            // 仅排空“当前已入队”的事件：用快照计数，避免回调内入队导致本帧无限处理。
            int count = m_EventQueue.Count;
            for (int i = 0; i < count; i++)
            {
                if (!m_EventQueue.TryDequeue(out QueuedEvent evt))
                {
                    break;
                }

                // 出队 Data 时递减全局已入队字节量（与 ReceiveLoop 入队前的累加对称）。
                if (evt.Kind == EventKind.Data && evt.Payload != null)
                {
                    Interlocked.Add(ref m_QueuedBytes, -evt.Payload.Length);
                }

                Dispatch(evt);
            }
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            if (m_IsServer)
            {
                StopServer();
            }

            if (m_IsClient)
            {
                Disconnect();
            }

            m_Running = false;
            m_ClientState = NetConnectionState.Disconnected;

            // 清空残留事件队列，并复位已入队字节量计数。
            while (m_EventQueue.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref m_QueuedBytes, 0L);
        }

        // ---------------- 后台线程逻辑 ----------------

        // 接受连接循环（服务器后台线程）。
        private void AcceptLoop()
        {
            while (m_Running)
            {
                TcpClient client;
                try
                {
                    client = m_Listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    // 监听被关闭 / 出错：退出循环（拆除时的预期异常）。
                    break;
                }

                if (!m_Running)
                {
                    SafeCloseClient(client);
                    break;
                }

                client.NoDelay = true;

                int id = Interlocked.Increment(ref m_NextConnectionId);
                Connection conn = new Connection
                {
                    Id = id,
                    Client = client,
                    Stream = client.GetStream(),
                };

                lock (m_ConnectionsLock)
                {
                    m_Connections[id] = conn;
                }

                m_EventQueue.Enqueue(new QueuedEvent(EventKind.Connected, id, null));

                conn.ReceiveThread = new Thread(() => ReceiveLoop(conn, isServerSide: true))
                {
                    IsBackground = true,
                    Name = "TcpNetTransport.ServerReceive#" + id,
                };
                conn.ReceiveThread.Start();
            }
        }

        // 接收循环（每连接一个后台线程）：按 4 字节大端长度前缀解帧，入队 Data；连接终止时入队 Disconnected。
        private void ReceiveLoop(Connection conn, bool isServerSide)
        {
            byte[] header = new byte[LengthPrefixSize];

            try
            {
                while (m_Running)
                {
                    if (!ReadExact(conn.Stream, header, 0, LengthPrefixSize))
                    {
                        break;
                    }

                    int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    if (length < 0 || length > MaxMessageLength)
                    {
                        // 非法长度：视为协议错误，断开该连接。
                        break;
                    }

                    byte[] payload = length == 0 ? Array.Empty<byte>() : new byte[length];
                    if (length > 0 && !ReadExact(conn.Stream, payload, 0, length))
                    {
                        break;
                    }

                    // DoS 防御：入队前先原子累加并校验全局已入队字节量；越限则丢弃该连接而非继续入队。
                    long queued = Interlocked.Add(ref m_QueuedBytes, length);
                    if (queued > MaxQueuedBytes)
                    {
                        // 回滚本次累加（该 payload 不会入队），断开该连接以泄压。
                        Interlocked.Add(ref m_QueuedBytes, -length);
                        FrameworkLog.Error(
                            "TcpNetTransport 事件队列超过上限 {0} 字节，丢弃连接 connId={1} 以防御 DoS。",
                            MaxQueuedBytes, conn.Id);
                        break;
                    }

                    m_EventQueue.Enqueue(new QueuedEvent(EventKind.Data, conn.Id, payload));
                }
            }
            catch (Exception)
            {
                // 读取期间套接字被关闭 / 出错：吞掉，走下方断开流程。
            }

            HandleConnectionClosed(conn, isServerSide);
        }

        // 处理连接关闭：从表中移除并入队一次断开事件（仅当之前确实在表中，避免重复）。
        private void HandleConnectionClosed(Connection conn, bool isServerSide)
        {
            bool wasTracked;

            if (isServerSide)
            {
                lock (m_ConnectionsLock)
                {
                    wasTracked = m_Connections.Remove(conn.Id);
                }
            }
            else
            {
                // 客户端侧：仅当当前连接仍是它本身时才算“被跟踪”。
                wasTracked = ReferenceEquals(m_ClientConnection, conn);
                if (wasTracked)
                {
                    m_ClientConnection = null;
                    m_ClientState = NetConnectionState.Disconnected;
                }
            }

            CloseConnectionSocket(conn);

            if (wasTracked)
            {
                m_EventQueue.Enqueue(new QueuedEvent(EventKind.Disconnected, conn.Id, null));
            }
        }

        // ---------------- 发送 ----------------

        // 带长度前缀的发送（线程安全：按连接的 SendLock 串行化）。失败时关闭该连接。
        private void SendFramed(Connection conn, ArraySegment<byte> data)
        {
            int count = data.Count;
            byte[] frame = new byte[LengthPrefixSize + count];
            frame[0] = (byte)((count >> 24) & 0xFF);
            frame[1] = (byte)((count >> 16) & 0xFF);
            frame[2] = (byte)((count >> 8) & 0xFF);
            frame[3] = (byte)(count & 0xFF);

            if (count > 0)
            {
                Buffer.BlockCopy(data.Array, data.Offset, frame, LengthPrefixSize, count);
            }

            try
            {
                lock (conn.SendLock)
                {
                    NetworkStream stream = conn.Stream;
                    if (stream != null)
                    {
                        stream.Write(frame, 0, frame.Length);
                    }
                }
            }
            catch (Exception)
            {
                // 写失败：关闭套接字，由接收线程的断开流程上报断开事件。
                CloseConnectionSocket(conn);
            }
        }

        // ---------------- 工具方法 ----------------

        // 从流中精确读取 count 字节；对端正常关闭（读到 0）或异常时返回 false。
        private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try
                {
                    n = stream.Read(buffer, offset + read, count - read);
                }
                catch (IOException)
                {
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }

                if (n <= 0)
                {
                    // 对端关闭。
                    return false;
                }

                read += n;
            }

            return true;
        }

        // 触发单条事件对应的回调。逐处理器隔离：单个处理器抛异常既不影响同一事件的其余处理器，也不
        // 中断本帧后续事件的派发（与 Core EventPool 的逐处理器异常隔离一致）。
        private void Dispatch(QueuedEvent evt)
        {
            switch (evt.Kind)
            {
                case EventKind.Connected:
                    SafeInvoke(OnClientConnected, evt.ConnectionId, evt.Kind);
                    break;

                case EventKind.Disconnected:
                    SafeInvoke(OnClientDisconnected, evt.ConnectionId, evt.Kind);
                    break;

                case EventKind.Data:
                    SafeInvoke(OnData, evt.ConnectionId, new ArraySegment<byte>(evt.Payload), evt.Kind);
                    break;
            }
        }

        // 遍历多播委托的 invocation list，逐处理器 try-catch，单个处理器异常被捕获记日志而不波及其余处理器。
        private static void SafeInvoke(Action<int> handlers, int connId, EventKind kind)
        {
            if (handlers == null) return;
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<int>)list[i])(connId); }
                catch (Exception ex)
                {
                    FrameworkLog.Error("TcpNetTransport handler threw on event kind={0}, connId={1}: {2}", kind, connId, ex);
                }
            }
        }

        private static void SafeInvoke(Action<int, ArraySegment<byte>> handlers, int connId, ArraySegment<byte> data, EventKind kind)
        {
            if (handlers == null) return;
            Delegate[] list = handlers.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action<int, ArraySegment<byte>>)list[i])(connId, data); }
                catch (Exception ex)
                {
                    FrameworkLog.Error("TcpNetTransport handler threw on event kind={0}, connId={1}: {2}", kind, connId, ex);
                }
            }
        }

        private void SafeStopListener()
        {
            TcpListener listener = m_Listener;
            m_Listener = null;
            if (listener == null)
            {
                return;
            }

            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
                // 忽略拆除期异常。
            }
        }

        private static void CloseConnectionSocket(Connection conn)
        {
            if (conn == null)
            {
                return;
            }

            try
            {
                conn.Stream?.Close();
            }
            catch (Exception)
            {
                // 忽略。
            }

            try
            {
                conn.Client?.Close();
            }
            catch (Exception)
            {
                // 忽略。
            }
        }

        private static void SafeCloseClient(TcpClient client)
        {
            try
            {
                client?.Close();
            }
            catch (Exception)
            {
                // 忽略。
            }
        }

        // 等待后台线程结束（不在自身线程上 Join，留有限超时以防极端卡死）。
        private static void JoinThread(Thread thread)
        {
            if (thread == null || !thread.IsAlive)
            {
                return;
            }

            if (Thread.CurrentThread == thread)
            {
                return;
            }

            try
            {
                thread.Join(2000);
            }
            catch (Exception)
            {
                // 忽略。
            }
        }
    }
}
