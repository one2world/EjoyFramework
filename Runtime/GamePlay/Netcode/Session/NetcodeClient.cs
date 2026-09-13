//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Messages;
using EjoyFramework.GamePlay.Netcode.Prediction;
using EjoyFramework.GamePlay.Netcode.Sync;
using EjoyFramework.GamePlay.Netcode.Transport;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 服务器权威架构下的客户端网络门面：组合传输层、消息层、快照插值与「客户端预测 + 和解」，
    /// 对外暴露一套「开箱即用」的客户端 API。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// 作为框架模块（<see cref="FrameworkModule"/>）经 <see cref="EjoyFramework.Core.Framework.GetModule{T}"/>
    /// 以 <see cref="INetcodeClient"/> 解析；使用前须通过 <see cref="SetTransport"/> 注入底层传输。
    /// <para>
    /// 行为概览：
    /// <list type="bullet">
    /// <item>收到 <see cref="WelcomeMessage"/> → 记录受控实体 id、初始化本地预测器、触发 <see cref="OnConnected"/>。</item>
    /// <item><see cref="SendInput"/> → 本地立即预测（消除手感延迟）并把 <see cref="InputMessage"/> 发往服务器。</item>
    /// <item>收到 <see cref="SnapshotMessage"/> → 入抖动缓冲、喂网络时钟、并用快照中的本地实体权威状态<b>和解</b>预测。</item>
    /// <item><see cref="GetInterpolatedEntities"/> → 对远端实体（<b>排除</b>本地玩家）在渲染时刻做插值。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class NetcodeClient : FrameworkModule, INetcodeClient
    {
        private const int LocalEntityUnassigned = -1;
        private const double DefaultInterpolationDelaySec = 0.1d;

        // 默认模拟步进：原样返回当前状态（无操作）。保证未注入步进时模块仍可解析并运行。
        private static readonly Func<float[], float[], float, float[]> DefaultSimulateStep =
            (state, input, dt) => state ?? Array.Empty<float>();

        private INetTransport m_Transport;
        private Func<float[], float[], float, float[]> m_SimulateStep = DefaultSimulateStep;

        private NetMessageRegistry m_Registry;
        private MessageDispatcher m_Dispatcher;

        private SnapshotBuffer m_SnapshotBuffer;
        private SnapshotInterpolator m_Interpolator;

        // 复用采样缓冲，避免每帧为插值结果分配（GetInterpolatedEntities 的非分配路径用）。
        private readonly List<EntitySnapshot> m_SampleBuffer = new List<EntitySnapshot>();
        private NetworkClock m_Clock;

        private ClientPrediction<float[], float[]> m_Prediction;

        private int m_LocalEntityId = LocalEntityUnassigned;
        private double m_InterpolationDelaySec = DefaultInterpolationDelaySec;
        private double m_LastLocalTimeSec;
        private bool m_Started;

        // 由 Update 驱动时累加的本地时间（秒）。
        private double m_Now;

        /// <summary>
        /// 构造客户端门面（无参）。供框架以 <c>Activator.CreateInstance</c> 懒加载，
        /// 之后须通过 <see cref="SetTransport"/> 注入底层传输方可使用。
        /// </summary>
        public NetcodeClient()
        {
            Initialize();
        }

        /// <summary>
        /// 构造客户端门面（便捷重载，主要供测试直接装配）。
        /// </summary>
        /// <param name="transport">底层传输（客户端角色），不可为 null。</param>
        /// <param name="simulateStep">与服务器<b>完全一致</b>的确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>，不可为 null。</param>
        /// <param name="interpolationDelaySec">远端实体插值延迟（秒），默认 0.1（100ms）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> 或 <paramref name="simulateStep"/> 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interpolationDelaySec"/> 为负。</exception>
        internal NetcodeClient(
            INetTransport transport,
            Func<float[], float[], float, float[]> simulateStep,
            double interpolationDelaySec = DefaultInterpolationDelaySec)
        {
            Initialize();
            SetTransport(transport);
            SetSimulateStep(simulateStep);
            SetInterpolationDelay(interpolationDelaySec);
        }

        private void Initialize()
        {
            m_Registry = new NetMessageRegistry();
            m_Registry.Register(NetcodeMessageIds.Welcome, () => new WelcomeMessage());
            m_Registry.Register(NetcodeMessageIds.Input, () => new InputMessage());
            m_Registry.Register(NetcodeMessageIds.Snapshot, () => new SnapshotMessage());

            m_Dispatcher = new MessageDispatcher(m_Registry);
            m_Dispatcher.On<WelcomeMessage>(HandleWelcome);
            m_Dispatcher.On<SnapshotMessage>(HandleSnapshot);

            m_SnapshotBuffer = new SnapshotBuffer();
            m_Interpolator = new SnapshotInterpolator(m_SnapshotBuffer, m_InterpolationDelaySec);
            m_Clock = new NetworkClock();
        }

        /// <summary>
        /// 获取游戏框架模块优先级。
        /// </summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>
        /// 本模块要求外部配置（注入 <see cref="INetTransport"/>）方可工作。
        /// </summary>
        public override bool RequiresConfiguration
        {
            get { return true; }
        }

        /// <summary>
        /// 当且仅当已注入底层传输时视为完成配置。
        /// </summary>
        public override bool IsModuleConfigured
        {
            get { return m_Transport != null; }
        }

        /// <summary>
        /// 未配置时的修复提示。
        /// </summary>
        public override string ConfigurationHint
        {
            get { return "Call SetTransport(INetTransport) before use."; }
        }

        /// <summary>
        /// 客户端连接状态。
        /// </summary>
        public NetConnectionState State
        {
            get { return m_Transport == null ? NetConnectionState.Disconnected : m_Transport.ClientState; }
        }

        /// <summary>
        /// 本地玩家受控的实体 id；在收到 <see cref="WelcomeMessage"/> 前为 -1。
        /// </summary>
        public int LocalEntityId
        {
            get { return m_LocalEntityId; }
        }

        /// <summary>
        /// 本地玩家的<b>预测</b>状态向量。未握手前为空数组。
        /// </summary>
        public float[] PredictedLocalState
        {
            get
            {
                if (m_Prediction == null)
                {
                    return Array.Empty<float>();
                }

                return m_Prediction.PredictedState ?? Array.Empty<float>();
            }
        }

        /// <summary>
        /// 收到 <see cref="WelcomeMessage"/>（握手完成）时触发一次。
        /// </summary>
        public event Action OnConnected;

        /// <summary>
        /// 与服务器断开时触发。
        /// </summary>
        public event Action OnDisconnected;

        /// <summary>
        /// 注入底层传输（客户端角色），是本模块的必需配置。
        /// </summary>
        /// <param name="transport">网络传输实现，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> 为 null。</exception>
        public void SetTransport(INetTransport transport)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            m_Transport = transport;
        }

        /// <summary>
        /// 注入与服务器<b>完全一致</b>的确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>。
        /// </summary>
        /// <param name="simulateStep">确定性模拟步进委托，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="simulateStep"/> 为 null。</exception>
        public void SetSimulateStep(Func<float[], float[], float, float[]> simulateStep)
        {
            if (simulateStep == null)
            {
                throw new ArgumentNullException(nameof(simulateStep));
            }

            m_SimulateStep = simulateStep;
        }

        /// <summary>
        /// 设置远端实体插值延迟（秒）。须在 <see cref="Connect"/> 之前设置以生效。
        /// </summary>
        /// <param name="interpolationDelaySec">插值延迟（秒），不可为负。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interpolationDelaySec"/> 为负。</exception>
        public void SetInterpolationDelay(double interpolationDelaySec)
        {
            if (interpolationDelaySec < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interpolationDelaySec), "插值延迟不可为负数。");
            }

            m_InterpolationDelaySec = interpolationDelaySec;
            // 重建插值器以采用新延迟；绑定同一抖动缓冲，已入缓冲的快照不受影响。
            m_Interpolator = new SnapshotInterpolator(m_SnapshotBuffer, m_InterpolationDelaySec);
        }

        /// <summary>
        /// 接管传输事件并发起连接：把 <see cref="INetTransport.OnData"/> 路由到内部分派器，随后连接服务器。
        /// </summary>
        /// <param name="host">主机地址（回环传输忽略）。</param>
        /// <param name="port">端口（回环传输忽略）。</param>
        /// <exception cref="InvalidOperationException">尚未注入底层传输。</exception>
        public void Connect(string host, int port)
        {
            if (m_Transport == null)
            {
                throw new InvalidOperationException(ConfigurationHint);
            }

            if (!m_Started)
            {
                m_Started = true;
                m_Transport.OnClientDisconnected += HandleClientDisconnected;
                m_Transport.OnData += HandleData;
            }

            m_Transport.Connect(host, port);
        }

        /// <summary>
        /// 本地输入：立即本地预测（推进 <see cref="PredictedLocalState"/>）并把对应 <see cref="InputMessage"/> 发往服务器。
        /// 在收到 <see cref="WelcomeMessage"/> 之前调用将被忽略（尚无受控实体与预测器）。
        /// </summary>
        /// <param name="input">输入向量（如 [vx, vy]）；null 视为长度 0。</param>
        /// <param name="deltaTime">该输入对应的模拟步长（秒）。</param>
        public void SendInput(float[] input, float deltaTime)
        {
            if (m_Prediction == null)
            {
                return;
            }

            float[] safeInput = input ?? Array.Empty<float>();

            // 本地预测：分配序号并推进预测状态，返回待发送的指令。
            InputCommand<float[]> command = m_Prediction.AddInput(safeInput, deltaTime);

            InputMessage message = new InputMessage
            {
                Sequence = command.Sequence,
                DeltaTime = command.DeltaTime,
                Input = command.Input,
            };

            byte[] framed = m_Registry.Pack(message);
            m_Transport.Send(0, new ArraySegment<byte>(framed), NetDeliveryMethod.ReliableOrdered);
        }

        /// <summary>
        /// 排空传输事件：驱动 <see cref="INetTransport.Poll"/>，从而处理握手 / 快照 / 断开。
        /// </summary>
        public void Poll()
        {
            if (m_Transport == null)
            {
                return;
            }

            m_Transport.Poll();
        }

        /// <summary>
        /// 推进客户端本地时间：记录当前本地时间以供快照到达时喂入网络时钟与渲染采样。
        /// 插值在 <see cref="GetInterpolatedEntities"/> 中按需进行，本方法不直接产生可见副作用。
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        public void Tick(double localTimeSec)
        {
            m_LastLocalTimeSec = localTimeSec;
        }

        /// <summary>
        /// 在给定本地时间上，返回所有<b>远端</b>实体（排除本地玩家受控实体）的插值状态。
        /// <para>
        /// 渲染时间取「估计服务器时间 - 插值延迟」，由 <see cref="SnapshotInterpolator"/> 在抖动缓冲上插值得到。
        /// </para>
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        /// <returns>远端实体插值状态集合（新列表，已排除本地玩家）。</returns>
        public IReadOnlyList<EntitySnapshot> GetInterpolatedEntities(double localTimeSec)
        {
            var result = new List<EntitySnapshot>();
            GetInterpolatedEntities(localTimeSec, result);
            return result;
        }

        /// <summary>
        /// 非分配重载：把所有<b>远端</b>实体（排除本地玩家受控实体）的插值状态填入 <paramref name="into"/>（先清空）。
        /// 复用内部采样缓冲，避免每帧为列表分配；各实体的状态数组仍按需新建（调用方读取后不应长期持有）。
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        /// <param name="into">装载结果的列表，调用前会被清空；不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> 为 null 时抛出。</exception>
        public void GetInterpolatedEntities(double localTimeSec, List<EntitySnapshot> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            m_LastLocalTimeSec = localTimeSec;

            double serverTime = m_Clock.EstimatedServerTimeSec(localTimeSec);
            m_Interpolator.Sample(serverTime, m_SampleBuffer);

            into.Clear();
            for (int i = 0; i < m_SampleBuffer.Count; i++)
            {
                EntitySnapshot snapshot = m_SampleBuffer[i];
                if (snapshot.EntityId == m_LocalEntityId)
                {
                    continue;
                }

                into.Add(snapshot);
            }
        }

        /// <summary>
        /// 游戏框架模块轮询：累加本地时间、排空传输事件并推进本地时间轴。
        /// 未注入传输或尚未 <see cref="Connect"/> 时为空操作。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_Now += elapseSeconds;

            if (!m_Started || m_Transport == null)
            {
                return;
            }

            Poll();
            Tick(m_Now);
        }

        /// <summary>
        /// 关闭并清理模块运行时状态：解除传输事件、释放预测器、清空抖动缓冲并重置时间。
        /// </summary>
        public override void Shutdown()
        {
            if (m_Transport != null && m_Started)
            {
                m_Transport.OnClientDisconnected -= HandleClientDisconnected;
                m_Transport.OnData -= HandleData;
            }

            m_Prediction = null;
            m_SnapshotBuffer = new SnapshotBuffer();
            m_Interpolator = new SnapshotInterpolator(m_SnapshotBuffer, m_InterpolationDelaySec);
            m_Clock = new NetworkClock();

            m_LocalEntityId = LocalEntityUnassigned;
            m_LastLocalTimeSec = 0d;
            m_Now = 0d;
            m_Started = false;

            OnConnected = null;
            OnDisconnected = null;
        }

        // —— 传输事件处理 ——

        private void HandleData(int connectionId, ArraySegment<byte> framed)
        {
            m_Dispatcher.Dispatch(connectionId, framed);
        }

        private void HandleClientDisconnected(int connectionId)
        {
            OnDisconnected?.Invoke();
        }

        // —— 消息处理 ——

        private void HandleWelcome(int connectionId, WelcomeMessage message)
        {
            m_LocalEntityId = message.EntityId;

            // 初始化预测器；初始状态以零向量起步，首个含本地实体的快照会通过和解校正为权威基线。
            if (m_Prediction == null)
            {
                m_Prediction = new ClientPrediction<float[], float[]>(m_SimulateStep, Array.Empty<float>());
            }
            else
            {
                m_Prediction.Reset(Array.Empty<float>());
            }

            // 用 tick 频率初始化插值延迟无需变更：保持构造时给定的延迟。
            OnConnected?.Invoke();
        }

        private void HandleSnapshot(int connectionId, SnapshotMessage message)
        {
            // 1) 组装 WorldSnapshot 并入抖动缓冲，供远端插值。
            WorldSnapshot world = new WorldSnapshot(message.Tick, message.ServerTimeSec);
            float[] authoritativeLocalState = null;
            bool hasLocal = false;

            List<SnapshotMessage.EntityState> entities = message.Entities ?? new List<SnapshotMessage.EntityState>();
            for (int i = 0; i < entities.Count; i++)
            {
                SnapshotMessage.EntityState entity = entities[i];
                float[] values = entity.Values ?? Array.Empty<float>();
                world.AddEntity(new EntitySnapshot(entity.EntityId, CopyValues(values)));

                if (entity.EntityId == m_LocalEntityId)
                {
                    authoritativeLocalState = CopyValues(values);
                    hasLocal = true;
                }
            }

            m_SnapshotBuffer.Add(world);

            // 2) 喂网络时钟：以快照携带的服务器时间与当前本地时间估计偏移（回环 RTT≈0）。
            m_Clock.OnServerTimeReceived(message.ServerTimeSec, m_LastLocalTimeSec, 0d);

            // 3) 和解本地预测：若快照含本地实体，用其权威状态 + 服务器已处理输入序号纠正预测。
            if (hasLocal && m_Prediction != null)
            {
                m_Prediction.Reconcile(authoritativeLocalState, message.LastProcessedInputSequence);
            }
        }

        private static float[] CopyValues(float[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<float>();
            }

            float[] copy = new float[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
