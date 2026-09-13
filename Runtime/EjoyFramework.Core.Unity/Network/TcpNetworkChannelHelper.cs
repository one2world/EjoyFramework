//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EjoyFramework.Core.Network;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.Sockets;
using System.Threading;
#endif

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 基于 TcpClient 的 INetworkChannelHelper 样板实现。
    /// 协议：4字节小端 BodyLength + 4字节小端 PacketId + Body 字节流；序列化由派生类重写 SerializeBody/DeserializeBody。
    /// 收发线程后台跑；接收到的整帧通过 ConcurrentQueue&lt;Packet&gt; 在主线程 Update 派发（无 per-packet 闭包分配）。
    /// 这是样板：业务通常派生此类并实现 SerializeBody/CreatePacket/CreateHeartBeatPacket。
    ///
    /// 线程模型：
    ///   - m_StateLock 统一保护 socket/stream 的读取、写入与释放。Send 与 Close/Cleanup 互斥，
    ///     避免在 Send 持锁写流期间另一线程将 stream 置空/Dispose 而导致 ObjectDisposedException/NRE。
    ///   - 接收线程将完成的 Packet 入 m_ReceivedPackets（ConcurrentQueue），主线程 Update 中批量 drain
    ///     并调用 NotifyReceived；非 Packet 的回调（连接/关闭/错误）仍走 m_MainThreadActions。
    ///
    /// 平台说明：System.Threading.Thread / ThreadPool / Socket 在 WebGL 上不受支持，
    ///   因此线程化 socket 实现以 #if !UNITY_WEBGL || UNITY_EDITOR 包裹；WebGL 路径显式抛出
    ///   FrameworkException 使失败可见，而非运行时神秘崩溃。PC/移动端行为不变。
    /// </summary>
    public abstract class TcpNetworkChannelHelper : INetworkChannelHelper, IReconnectableChannelHelper, IDisposable
    {
        private INetworkChannel m_Channel;
        private readonly ConcurrentQueue<Action> m_MainThreadActions = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<Packet> m_ReceivedPackets = new ConcurrentQueue<Packet>();

#if !UNITY_WEBGL || UNITY_EDITOR
        private TcpClient m_Client;
        private NetworkStream m_Stream;
        private Thread m_RecvThread;
        private Thread m_SendThread;
        private CancellationTokenSource m_Cts;
        // 统一锁：保护 m_Stream / m_Client / m_Cts 的读取、写入与释放。
        private readonly object m_StateLock = new object();
        private volatile bool m_ShuttingDown;

        // ---- 异步发送队列（背压有界）----
        // 队列中存放已序列化好的整帧字节；发送线程在 m_StateLock 下写流，主线程 Send 永不阻塞在 socket I/O。
        private readonly ConcurrentQueue<byte[]> m_SendQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim m_SendSignal = new SemaphoreSlim(0);
        private int m_SendQueueCount; // 近似计数（Interlocked 维护），用于背压判定。
        private volatile int m_MaxQueuedPackets = 1024;
        private volatile bool m_BackpressureLoggedOnce;

        // ---- 自动重连 ----
        private readonly object m_ReconnectLock = new object();
        private ReconnectPolicy m_ReconnectPolicy = ReconnectPolicy.Default;
        private Thread m_ReconnectThread;
        private CancellationTokenSource m_ReconnectCts;
        // 最近一次成功 Connect 的目标，用于重连重拨。
        private string m_LastIp;
        private int m_LastPort;
        private object m_LastUserData;
        private volatile bool m_ExplicitClose; // 显式 Close()：禁止任何重连。
        private volatile bool m_Reconnecting;   // 重连循环进行中，避免并发触发。
#endif

        public void Initialize(INetworkChannel channel)
        {
            m_Channel = channel ?? throw new ArgumentNullException(nameof(channel));
        }

        public void SetReconnectPolicy(ReconnectPolicy policy)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            lock (m_ReconnectLock) { m_ReconnectPolicy = policy; }
#endif
        }

        public void DropForReconnect()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // 关闭当前 stream（但不设 m_ShuttingDown / m_ExplicitClose），
            // 接收线程随即解除阻塞并在 finally 中走 TryStartReconnect。
            if (m_ExplicitClose || m_ShuttingDown) return;
            CleanupSocket();
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR

        public void Connect(string ipAddress, int port, object userData)
        {
            if (m_Channel == null) throw new InvalidOperationException("Helper not initialized.");
            // 一次显式 Connect 视为全新会话：清理旧 socket、取消挂起重连、复位显式关闭标志。
            CancelReconnect();
            CleanupSocket();
            m_ShuttingDown = false;
            m_ExplicitClose = false;
            lock (m_ReconnectLock) { m_LastIp = ipAddress; m_LastPort = port; m_LastUserData = userData; }
            DialInternal(ipAddress, port, userData, isReconnect: false);
        }

        // 真正发起一次连接。isReconnect=true 时连接结果用于驱动重连状态机；
        // 注意：本方法不持有任何锁去阻塞，BeginConnect 的回调在 ThreadPool 线程完成。
        private void DialInternal(string ipAddress, int port, object userData, bool isReconnect)
        {
            try
            {
                var client = new TcpClient { NoDelay = true };
                var cts = new CancellationTokenSource();
                lock (m_StateLock)
                {
                    m_Client = client;
                    m_Cts = cts;
                }
                IAsyncResult ar = client.BeginConnect(ipAddress, port, null, null);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        client.EndConnect(ar);
                        NetworkStream stream = client.GetStream();
                        lock (m_StateLock)
                        {
                            // 连接成功前已被 Close/重新连接取消：弃用该 stream。
                            if (m_ShuttingDown || m_Client != client)
                            {
                                try { stream.Close(); } catch (Exception ex) { FrameworkLog.Warning("Discard stream after cancel threw: {0}", ex); }
                                return;
                            }
                            m_Stream = stream;
                        }
                        StartReceiveThread();
                        StartSendThread();
                        // 唤醒发送循环：重连期间排队的帧应立即随新 stream 上线刷出，而非等下一个 500ms 超时。
                        try { m_SendSignal.Release(); } catch (Exception ex) { FrameworkLog.Warning("SendSignal.Release on dial threw: {0}", ex); }
                        m_OnDialSucceeded?.Invoke();
                        EnqueueMainThread(() => m_Channel.NotifyConnected(userData));
                    }
                    catch (Exception ex)
                    {
                        FrameworkLog.Error("TcpNetworkChannelHelper connect failed: {0}", ex);
                        if (isReconnect)
                        {
                            // 重连期间的连接失败交由重连循环按退避继续重试。
                            m_OnDialFailed?.Invoke();
                        }
                        else
                        {
                            // 首次连接失败：m_Connected 从未置 true，NotifyClosed 会被 NetworkChannel 提前 return 吞掉，
                            // 因此用 NotifyError 通知消费者本次连接失败。
                            EnqueueMainThread(() => m_Channel.NotifyError(-100, "Connect failed: " + ex.Message));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("TcpNetworkChannelHelper TcpClient init failed: {0}", ex);
                if (isReconnect) m_OnDialFailed?.Invoke();
                else m_Channel.NotifyError(-101, "TcpClient init failed: " + ex.Message);
            }
        }

        // 重连循环用：本次拨号成功/失败的回调（仅在重连上下文设置；非重连为 null）。
        private volatile Action m_OnDialSucceeded;
        private volatile Action m_OnDialFailed;

        public void Close()
        {
            // 显式关闭：禁止并取消任何重连，停止收发线程，最终通知关闭。
            m_ExplicitClose = true;
            m_ShuttingDown = true;
            CancelReconnect();
            CleanupSocket();
            EnqueueMainThread(() => m_Channel?.NotifyClosed());
        }

        public bool Send<T>(T packet) where T : Packet
        {
            byte[] frame;
            try
            {
                byte[] body = SerializeBody(packet);
                int packetId = GetPacketId(packet);
                int bodyLen = body?.Length ?? 0;
                frame = new byte[8 + bodyLen];
                WriteLittleEndian(frame, 0, bodyLen);
                WriteLittleEndian(frame, 4, packetId);
                if (bodyLen > 0) Buffer.BlockCopy(body, 0, frame, 8, bodyLen);
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("TcpNetworkChannelHelper SerializeBody threw: {0}", ex);
                m_Channel?.NotifyError(-110, "SerializeBody threw: " + ex.Message);
                return false;
            }

            // 背压：队列超过上限则拒绝入队（不无限增长），仅记一次错误避免刷屏。
            int cap = m_MaxQueuedPackets;
            if (Interlocked.CompareExchange(ref m_SendQueueCount, 0, 0) >= cap)
            {
                if (!m_BackpressureLoggedOnce)
                {
                    m_BackpressureLoggedOnce = true;
                    FrameworkLog.Error("TcpNetworkChannelHelper send queue overflow (cap={0}); dropping packet.", cap);
                }
                m_Channel?.NotifyError(-112, "Send queue overflow; packet dropped.");
                return false;
            }

            // 入队后由发送线程在 m_StateLock 下写流；主线程不阻塞 socket I/O。
            m_SendQueue.Enqueue(frame);
            Interlocked.Increment(ref m_SendQueueCount);
            m_BackpressureLoggedOnce = false;
            try { m_SendSignal.Release(); } catch (Exception ex) { FrameworkLog.Warning("SendSignal.Release threw: {0}", ex); }
            return true;
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 必须先处理连接/关闭/错误回调（含 NotifyConnected），再 drain 收到的 Packet：
            // 否则接收线程抢先入队的服务器首包会在 Connected 事件之前派发，业务以 Connected 门控时会丢包。
            while (m_MainThreadActions.TryDequeue(out var act))
            {
                try { act(); }
                catch (Exception ex) { FrameworkLog.Error("Main-thread dispatch threw: {0}", ex); m_Channel?.NotifyError(-120, "Main-thread dispatch threw: " + ex.Message); }
            }
            while (m_ReceivedPackets.TryDequeue(out var packet))
            {
                try { m_Channel?.NotifyReceived(packet); }
                catch (Exception ex) { FrameworkLog.Error("NotifyReceived threw: {0}", ex); m_Channel?.NotifyError(-121, "NotifyReceived threw: " + ex.Message); }
            }
        }

        public void Shutdown() { Dispose(); }

        public void Dispose()
        {
            m_ExplicitClose = true;
            m_ShuttingDown = true;
            CancelReconnect();
            CleanupSocket();
            try { m_SendSignal.Release(); } catch (Exception ex) { FrameworkLog.Warning("SendSignal.Release on dispose threw: {0}", ex); }
        }

        private void StartReceiveThread()
        {
            m_RecvThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "EjoyTcpRecv" };
            m_RecvThread.Start();
        }

        private void StartSendThread()
        {
            // 仅在没有存活发送线程时启动。重连后会随新 stream 复用同一发送循环。
            if (m_SendThread != null && m_SendThread.IsAlive) return;
            m_SendThread = new Thread(SendLoop) { IsBackground = true, Name = "EjoyTcpSend" };
            m_SendThread.Start();
        }

        // 后台发送循环：等待信号 → 取帧 → 在 m_StateLock 下写流。stream 为空（未连/重连中）时把帧放回不丢弃。
        private void SendLoop()
        {
            while (!m_ShuttingDown)
            {
                try { m_SendSignal.Wait(500); }
                catch (Exception ex) { FrameworkLog.Warning("SendSignal.Wait threw: {0}", ex); }
                if (m_ShuttingDown) break;

                while (m_SendQueue.TryPeek(out byte[] frame))
                {
                    // 仅在锁内快照 stream 引用（瞬时），写操作放到锁外执行：
                    // 阻塞的 Stream.Write/Flush 不再持有 m_StateLock，避免 Close/CleanupSocket 被慢写阻塞。
                    // 与 CleanupSocket 并发时，若 stream 已被 Dispose，下面的 Write 会抛 ObjectDisposed/IOException，
                    // 被 catch 当作写失败处理（break → 等重连），不会损坏状态。
                    NetworkStream stream;
                    lock (m_StateLock) { stream = m_Stream; }
                    // 断线/重连过程中暂无可写 stream：保留队首，等待重连恢复后再发。
                    if (stream == null) break;

                    bool written;
                    try { stream.Write(frame, 0, frame.Length); stream.Flush(); written = true; }
                    catch (Exception ex)
                    {
                        written = false;
                        if (!m_ShuttingDown)
                        {
                            FrameworkLog.Error("TcpNetworkChannelHelper Stream.Write threw: {0}", ex);
                            EnqueueMainThread(() => m_Channel?.NotifyError(-111, "Stream.Write threw: " + ex.Message));
                        }
                    }
                    if (!written) break; // 写失败：等接收线程检测断线并触发重连，帧保留待重发。

                    if (m_SendQueue.TryDequeue(out _)) Interlocked.Decrement(ref m_SendQueueCount);
                }
            }
        }

        private void ReceiveLoop()
        {
            byte[] header = new byte[8];
            try
            {
                while (true)
                {
                    NetworkStream stream;
                    lock (m_StateLock) { stream = m_Stream; }
                    if (m_ShuttingDown || stream == null) break;

                    if (!ReadFully(stream, header, 8)) break;
                    int bodyLen = ReadLittleEndian(header, 0);
                    int packetId = ReadLittleEndian(header, 4);
                    if (bodyLen < 0 || bodyLen > 16 * 1024 * 1024)
                    {
                        EnqueueMainThread(() => m_Channel?.NotifyError(-130, "Bad body length: " + bodyLen));
                        break;
                    }
                    byte[] body = bodyLen == 0 ? Array.Empty<byte>() : new byte[bodyLen];
                    if (bodyLen > 0 && !ReadFully(stream, body, bodyLen)) break;

                    Packet packet;
                    try { packet = CreatePacket(packetId, body); }
                    catch (Exception ex)
                    {
                        FrameworkLog.Error("TcpNetworkChannelHelper CreatePacket threw: {0}", ex);
                        EnqueueMainThread(() => m_Channel?.NotifyError(-131, "CreatePacket threw: " + ex.Message));
                        continue;
                    }
                    if (packet != null) m_ReceivedPackets.Enqueue(packet);
                }
            }
            catch (Exception ex)
            {
                if (!m_ShuttingDown)
                {
                    FrameworkLog.Error("TcpNetworkChannelHelper ReceiveLoop threw: {0}", ex);
                    EnqueueMainThread(() => m_Channel?.NotifyError(-132, "ReceiveLoop threw: " + ex.Message));
                }
            }
            finally
            {
                // 意外断线（非显式 Close / 非 Shutdown）：若启用重连则进入重连状态机，否则通知关闭。
                if (!m_ShuttingDown && !m_ExplicitClose)
                {
                    if (!TryStartReconnect())
                    {
                        EnqueueMainThread(() => m_Channel?.NotifyClosed());
                    }
                }
            }
        }

        // ===== 自动重连状态机 =====

        // 在意外断线点尝试启动重连循环。返回 true 表示已接管（不再 NotifyClosed）。
        private bool TryStartReconnect()
        {
            ReconnectPolicy policy;
            lock (m_ReconnectLock) { policy = m_ReconnectPolicy; }
            if (!policy.Enabled) return false;
            if (m_ExplicitClose || m_ShuttingDown) return false;
            if (m_Reconnecting) return true; // 已在重连中。

            string ip; int port; object ud;
            lock (m_ReconnectLock)
            {
                if (m_LastIp == null) return false;
                ip = m_LastIp; port = m_LastPort; ud = m_LastUserData;
                m_Reconnecting = true;
                m_ReconnectCts = new CancellationTokenSource();
            }

            // 先把断线作为一次 NotifyClosed 上报，让 channel 复位 Connected 状态（重连成功后再 NotifyConnected）。
            EnqueueMainThread(() => m_Channel?.NotifyClosed());

            var thread = new Thread(() => ReconnectLoop(ip, port, ud, policy)) { IsBackground = true, Name = "EjoyTcpReconnect" };
            m_ReconnectThread = thread;
            thread.Start();
            return true;
        }

        private void ReconnectLoop(string ip, int port, object userData, ReconnectPolicy policy)
        {
            CancellationToken token;
            lock (m_ReconnectLock) { token = m_ReconnectCts != null ? m_ReconnectCts.Token : CancellationToken.None; }

            int attempt = 0;
            try
            {
                while (!m_ExplicitClose && !m_ShuttingDown && !token.IsCancellationRequested)
                {
                    if (policy.MaxAttempts > 0 && attempt >= policy.MaxAttempts)
                    {
                        EnqueueMainThread(() => m_Channel?.NotifyReconnect(NetworkReconnectPhase.Failed, attempt, 0f));
                        EnqueueMainThread(() => m_Channel?.NotifyClosed());
                        break;
                    }

                    float delay = ReconnectBackoff.NextDelaySeconds(attempt, policy.BaseDelaySeconds, policy.MaxDelaySeconds);
                    int curAttempt = attempt;
                    EnqueueMainThread(() => m_Channel?.NotifyReconnect(NetworkReconnectPhase.Reconnecting, curAttempt, delay));

                    // 退避等待（可被 Close 取消），不持有任何锁。
                    if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(delay))) break; // 被取消
                    if (m_ExplicitClose || m_ShuttingDown) break;

                    // 一次拨号：用回调驱动成功/失败的握手（DialInternal 内的连接在 ThreadPool 完成）。
                    using (var dialDone = new ManualResetEventSlim(false))
                    {
                        bool succeeded = false;
                        m_OnDialSucceeded = () => { succeeded = true; dialDone.Set(); };
                        m_OnDialFailed = () => { succeeded = false; dialDone.Set(); };

                        CleanupSocketForRedial();
                        DialInternal(ip, port, userData, isReconnect: true);

                        // 等待拨号结束（带超时与取消）。
                        dialDone.Wait(token);
                        m_OnDialSucceeded = null;
                        m_OnDialFailed = null;

                        if (succeeded)
                        {
                            int doneAttempt = attempt;
                            EnqueueMainThread(() => m_Channel?.NotifyReconnect(NetworkReconnectPhase.Reconnected, doneAttempt, 0f));
                            // DialInternal 成功路径已 EnqueueMainThread NotifyConnected，状态机收尾。
                            break;
                        }
                    }
                    attempt++;
                }
            }
            catch (OperationCanceledException) { /* Close 取消重连：正常退出 */ }
            catch (Exception ex)
            {
                FrameworkLog.Error("TcpNetworkChannelHelper ReconnectLoop threw: {0}", ex);
                EnqueueMainThread(() => m_Channel?.NotifyClosed());
            }
            finally
            {
                m_Reconnecting = false;
            }
        }

        // 取消并清理挂起的重连（Close/Dispose/新 Connect 调用）。
        private void CancelReconnect()
        {
            CancellationTokenSource cts;
            lock (m_ReconnectLock)
            {
                cts = m_ReconnectCts;
                m_ReconnectCts = null;
                m_Reconnecting = false;
            }
            try { cts?.Cancel(); } catch (Exception ex) { FrameworkLog.Warning("ReconnectCts.Cancel threw: {0}", ex); }
            try { cts?.Dispose(); } catch (Exception ex) { FrameworkLog.Warning("ReconnectCts.Dispose threw: {0}", ex); }
        }

        private bool ReadFully(NetworkStream stream, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try { n = stream.Read(buf, read, count - read); }
                catch (Exception ex)
                {
                    if (!m_ShuttingDown) FrameworkLog.Error("TcpNetworkChannelHelper Stream.Read threw: {0}", ex);
                    return false;
                }
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private void CleanupSocket()
        {
            CancellationTokenSource cts;
            NetworkStream stream;
            TcpClient client;
            lock (m_StateLock)
            {
                cts = m_Cts;
                stream = m_Stream;
                client = m_Client;
                m_Stream = null;
                m_Client = null;
                m_Cts = null;
            }
            try { cts?.Cancel(); } catch (Exception ex) { FrameworkLog.Warning("Cts.Cancel threw: {0}", ex); }
            try { stream?.Close(); } catch (Exception ex) { FrameworkLog.Warning("Stream.Close threw: {0}", ex); }
            try { client?.Close(); } catch (Exception ex) { FrameworkLog.Warning("Client.Close threw: {0}", ex); }
            try { cts?.Dispose(); } catch (Exception ex) { FrameworkLog.Warning("Cts.Dispose threw: {0}", ex); }
        }

        // 重连重拨前清理上一次失败/断开的 socket，但不触碰 m_ShuttingDown / 发送线程 / 重连状态，
        // 以免误杀正在运行的发送循环或自身重连循环。
        private void CleanupSocketForRedial() { CleanupSocket(); }

#else // UNITY_WEBGL && !UNITY_EDITOR

        // WebGL 不支持 System.Net.Sockets / System.Threading：显式失败而非神秘崩溃。
        private const string WebGlUnsupported = "TCP networking is not supported on WebGL.";

        public void Connect(string ipAddress, int port, object userData)
        {
            throw new FrameworkException(WebGlUnsupported);
        }

        public void Close() { }

        public bool Send<T>(T packet) where T : Packet
        {
            throw new FrameworkException(WebGlUnsupported);
        }

        public void Update(float elapseSeconds, float realElapseSeconds) { }

        public void Shutdown() { Dispose(); }

        public void Dispose() { }

#endif

        public abstract Packet CreateHeartBeatPacket();

        // ---- 派生类实现序列化/反序列化 ----
        protected virtual int GetPacketId(Packet packet)
        {
            if (packet == null) throw new FrameworkException("packet is null.");
            return packet.Id;
        }

        protected abstract byte[] SerializeBody(Packet packet);
        protected abstract Packet CreatePacket(int packetId, byte[] body);

        // ===== 内部 =====

        private void EnqueueMainThread(Action act) { m_MainThreadActions.Enqueue(act); }

        private static void WriteLittleEndian(byte[] dst, int offset, int v)
        {
            dst[offset] = (byte)(v & 0xff);
            dst[offset + 1] = (byte)((v >> 8) & 0xff);
            dst[offset + 2] = (byte)((v >> 16) & 0xff);
            dst[offset + 3] = (byte)((v >> 24) & 0xff);
        }

        private static int ReadLittleEndian(byte[] src, int offset)
        {
            return src[offset] | (src[offset + 1] << 8) | (src[offset + 2] << 16) | (src[offset + 3] << 24);
        }
    }
}
