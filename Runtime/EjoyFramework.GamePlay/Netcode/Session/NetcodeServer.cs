//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Messages;
using EjoyFramework.GamePlay.Netcode.Transport;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 服务器权威（server-authoritative）网络门面：组合传输层、消息层与确定性模拟步进，
    /// 对外暴露一套「开箱即用」的服务器 API。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// 作为框架模块（<see cref="FrameworkModule"/>）经 <see cref="EjoyFramework.Core.Framework.GetModule{T}"/>
    /// 以 <see cref="INetcodeServer"/> 解析；使用前须通过 <see cref="SetTransport"/> 注入底层传输。
    /// <para>
    /// 职责：
    /// <list type="bullet">
    /// <item>持有所有服务器实体的权威状态（实体 = <c>float[]</c>）。</item>
    /// <item>接收客户端 <see cref="InputMessage"/>，对其受控实体<b>权威地</b>应用模拟步进。</item>
    /// <item>每 <see cref="Tick"/> 构建一份全实体 <see cref="WorldSnapshot"/>，并向各客户端下发携带其专属
    /// ack（已处理输入序号）的 <see cref="SnapshotMessage"/>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 顺序约定：客户端连接时先触发 <see cref="OnClientJoined"/>；随后游戏层调用
    /// <see cref="AssignClientEntity"/> 指定其受控实体，<b>该调用</b>才会向客户端发送 <see cref="WelcomeMessage"/>。
    /// 因此典型流程为：在 <see cref="OnClientJoined"/> 回调里 <see cref="RegisterEntity"/> 出生实体，再
    /// <see cref="AssignClientEntity"/> 把它绑定给该连接。
    /// </para>
    /// </summary>
    public sealed class NetcodeServer : FrameworkModule, INetcodeServer
    {
        // 默认模拟步进：原样返回当前状态（无操作）。保证未注入步进时模块仍可解析并运行。
        private static readonly Func<float[], float[], float, float[]> DefaultSimulateStep =
            (state, input, dt) => state ?? Array.Empty<float>();

        private const double DefaultTickRateHz = 30d;

        // 客户端上报的 DeltaTime 钳制上限（秒）：防止恶意 / 异常的大步长把权威实体一次推进过远。
        private const float MaxClientDeltaSec = 0.25f;

        private INetTransport m_Transport;
        private Func<float[], float[], float, float[]> m_SimulateStep = DefaultSimulateStep;
        private double m_TickRateHz = DefaultTickRateHz;

        private NetMessageRegistry m_Registry;
        private MessageDispatcher m_Dispatcher;

        // 实体 id -> 权威状态向量。
        private readonly Dictionary<int, float[]> m_Entities = new Dictionary<int, float[]>();

        // 连接 id -> 该连接受控的实体 id。
        private readonly Dictionary<int, int> m_ClientEntity = new Dictionary<int, int>();

        // 连接 id -> 服务器已处理到的该连接最后一个输入序号（ack）。
        private readonly Dictionary<int, uint> m_LastProcessedInput = new Dictionary<int, uint>();

        // Tick 复用缓冲（避免每帧/每客户端分配）：实体条目列表 + 连接快照 + 共享 SnapshotMessage 实例。
        private readonly List<SnapshotMessage.EntityState> m_TickEntityStates = new List<SnapshotMessage.EntityState>();
        private readonly List<int> m_TickConnections = new List<int>();
        private readonly SnapshotMessage m_TickSnapshot = new SnapshotMessage();

        // 复用的打包写入器：下发由 Tick（主线程）同步驱动，且两个传输实现都会在 Send 内同步拷贝区间，
        // 故跨客户端复用同一 writer 安全，省去每客户端每 tick 的 NetWriter 分配 + ToArray 拷贝。
        private readonly NetWriter m_PackWriter = new NetWriter();

        // 已连接的客户端集合（用于 Tick 时遍历下发快照）。
        private readonly HashSet<int> m_Connections = new HashSet<int>();

        private uint m_Tick;
        private bool m_Started;

        // 由 Update 驱动时累加的服务器时间（秒），作为快照时间轴。
        private double m_Now;
        private double m_TickAccumulator;

        /// <summary>
        /// 构造服务器门面（无参）。供框架以 <c>Activator.CreateInstance</c> 懒加载，
        /// 之后须通过 <see cref="SetTransport"/> 注入底层传输方可使用。
        /// </summary>
        public NetcodeServer()
        {
            Initialize();
        }

        /// <summary>
        /// 构造服务器门面（便捷重载，主要供测试直接装配）。
        /// </summary>
        /// <param name="transport">底层传输（须为服务器角色或可启动为服务器），不可为 null。</param>
        /// <param name="simulateStep">确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>，不可为 null。</param>
        /// <param name="tickRateHz">服务器 tick 频率（Hz），随 <see cref="WelcomeMessage"/> 下发给客户端，默认 30。</param>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> 或 <paramref name="simulateStep"/> 为 null。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRateHz"/> 不为正。</exception>
        internal NetcodeServer(
            INetTransport transport,
            Func<float[], float[], float, float[]> simulateStep,
            double tickRateHz = DefaultTickRateHz)
        {
            Initialize();
            SetTransport(transport);
            SetSimulateStep(simulateStep);
            SetTickRate(tickRateHz);
        }

        private void Initialize()
        {
            m_Registry = new NetMessageRegistry();
            m_Registry.Register(NetcodeMessageIds.Welcome, () => new WelcomeMessage());
            m_Registry.Register(NetcodeMessageIds.Input, () => new InputMessage());
            m_Registry.Register(NetcodeMessageIds.Snapshot, () => new SnapshotMessage());

            m_Dispatcher = new MessageDispatcher(m_Registry);
            m_Dispatcher.On<InputMessage>(HandleInput);
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
        /// 当前所有服务器实体的权威状态（只读视图）。键为实体 id，值为状态向量。
        /// </summary>
        public IReadOnlyDictionary<int, float[]> Entities
        {
            get { return m_Entities; }
        }

        /// <summary>
        /// 新客户端连接成功时触发，参数为其连接 id。注意：此时尚未发送 <see cref="WelcomeMessage"/>，
        /// 需在回调内（或之后）调用 <see cref="AssignClientEntity"/> 才会下发握手。
        /// </summary>
        public event Action<int> OnClientJoined;

        /// <summary>
        /// 客户端断开时触发：参数为 (连接 id, 其受控实体 id)。断开时该连接经 <see cref="AssignClientEntity"/>
        /// 绑定的受控实体会<b>自动从权威实体表移除</b>，避免无人驱动的“僵尸实体”继续广播进快照；受控实体 id
        /// 一并回传，供上层做最终状态处理 / 重生决策。该连接无受控实体时此参数为 -1。
        /// </summary>
        public event Action<int, int> OnClientLeft;

        /// <summary>
        /// 注入底层传输（须为服务器角色或可启动为服务器），是本模块的必需配置。
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
        /// 注入确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>，与客户端须完全一致。
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
        /// 设置服务器 tick 频率（Hz），随 <see cref="WelcomeMessage"/> 下发给客户端。
        /// </summary>
        /// <param name="tickRateHz">tick 频率（Hz），必须为正。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRateHz"/> 不为正。</exception>
        public void SetTickRate(double tickRateHz)
        {
            if (tickRateHz <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRateHz), "tick 频率必须为正数。");
            }

            m_TickRateHz = tickRateHz;
        }

        /// <summary>
        /// 以服务器角色启动并接管传输事件：监听端口，并把 <see cref="INetTransport.OnData"/> 路由到内部分派器。
        /// </summary>
        /// <param name="port">监听端口（回环传输忽略）。</param>
        /// <exception cref="InvalidOperationException">尚未注入底层传输。</exception>
        public void Start(int port)
        {
            if (m_Started)
            {
                return;
            }

            if (m_Transport == null)
            {
                throw new InvalidOperationException(ConfigurationHint);
            }

            m_Started = true;

            m_Transport.OnClientConnected += HandleClientConnected;
            m_Transport.OnClientDisconnected += HandleClientDisconnected;
            m_Transport.OnData += HandleData;

            m_Transport.StartServer(port);
        }

        /// <summary>
        /// 注册一个服务器拥有的实体（NPC，或将被分配给某客户端的受控实体）。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <param name="initialValues">初始状态向量；null 视为长度 0。内部保存一份拷贝。</param>
        /// <returns>实际存入的状态向量（内部拷贝）。</returns>
        public float[] RegisterEntity(int entityId, float[] initialValues)
        {
            float[] copy = CopyValues(initialValues);
            m_Entities[entityId] = copy;
            return copy;
        }

        /// <summary>
        /// 直接设置某实体的权威状态（如服务器侧 AI/物理推进）。内部保存一份拷贝。
        /// </summary>
        /// <param name="entityId">实体唯一标识（须已注册或将被创建）。</param>
        /// <param name="values">新状态向量；null 视为长度 0。</param>
        public void SetEntityState(int entityId, float[] values)
        {
            m_Entities[entityId] = CopyValues(values);
        }

        /// <summary>
        /// 移除一个服务器实体。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <returns>存在并移除返回 true，否则 false。</returns>
        public bool RemoveEntity(int entityId)
        {
            return m_Entities.Remove(entityId);
        }

        /// <summary>
        /// 把某实体指定为某连接的「受控实体」（其输入将驱动该实体），并立即向该客户端发送
        /// <see cref="WelcomeMessage"/>（携带实体 id 与 tick 频率）。
        /// </summary>
        /// <param name="connectionId">目标连接 id。</param>
        /// <param name="entityId">该连接受控的实体 id。</param>
        public void AssignClientEntity(int connectionId, int entityId)
        {
            m_ClientEntity[connectionId] = entityId;
            if (!m_LastProcessedInput.ContainsKey(connectionId))
            {
                m_LastProcessedInput[connectionId] = 0u;
            }

            WelcomeMessage welcome = new WelcomeMessage
            {
                EntityId = entityId,
                TickRateHz = (float)m_TickRateHz,
            };

            SendTo(connectionId, welcome);
        }

        /// <summary>
        /// 排空传输事件：驱动 <see cref="INetTransport.Poll"/>，从而处理连接/断开与到达的 <see cref="InputMessage"/>。
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
        /// 推进一个服务器 tick：构建一份包含所有实体的 <see cref="WorldSnapshot"/>，
        /// 并向每个已连接客户端下发一条 <see cref="SnapshotMessage"/>（携带该客户端专属的已处理输入序号），最后 tick 自增。
        /// </summary>
        /// <param name="serverTimeSec">当前服务器时间（秒），写入快照作为客户端插值的时间轴。</param>
        public void Tick(double serverTimeSec)
        {
            // 把实体列表物化为线路条目（复用 m_TickEntityStates），所有客户端共享同一份（每客户端仅 ack 不同）。
            // 直接引用 m_Entities 的实时数组（不拷贝）：快照在本方法内经 SendTo 同步序列化为字节，期间 m_Entities
            // 不会被修改（输入处理在接收/Poll 路径，不在 Tick 内），故无别名风险，省去每实体一次 float[] 拷贝。
            m_TickEntityStates.Clear();
            foreach (KeyValuePair<int, float[]> pair in m_Entities)
            {
                m_TickEntityStates.Add(new SnapshotMessage.EntityState(pair.Key, pair.Value ?? Array.Empty<float>()));
            }

            // 快照连接集合到复用列表，避免在下发过程中集合被回调间接修改（Mono/IL2CPP 安全）。
            m_TickConnections.Clear();
            m_TickConnections.AddRange(m_Connections);
            for (int i = 0; i < m_TickConnections.Count; i++)
            {
                int connectionId = m_TickConnections[i];

                uint ack;
                if (!m_LastProcessedInput.TryGetValue(connectionId, out ack))
                {
                    ack = 0u;
                }

                // 复用单个 SnapshotMessage 实例：每客户端仅 ack 不同，逐次重置字段后同步序列化下发。
                m_TickSnapshot.Tick = m_Tick;
                m_TickSnapshot.ServerTimeSec = serverTimeSec;
                m_TickSnapshot.LastProcessedInputSequence = ack;
                m_TickSnapshot.Entities = m_TickEntityStates;

                SendTo(connectionId, m_TickSnapshot);
            }

            m_Tick++;
        }

        /// <summary>
        /// 游戏框架模块轮询：启动后每帧排空传输事件，并按配置的 TickRate 以固定步长推进快照。
        /// 未注入传输或尚未 <see cref="Start"/> 时为空操作。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            if (!m_Started || m_Transport == null)
            {
                return;
            }

            Poll();

            double tickInterval = 1d / m_TickRateHz;
            if (realElapseSeconds > 0f)
            {
                m_TickAccumulator += realElapseSeconds;
            }

            while (m_TickAccumulator >= tickInterval)
            {
                m_TickAccumulator -= tickInterval;
                m_Now += tickInterval;
                Tick(m_Now);
            }
        }

        /// <summary>
        /// 关闭并清理模块运行时状态：解除传输事件、清空实体与连接表，并重置时间/tick。
        /// </summary>
        public override void Shutdown()
        {
            if (m_Transport != null && m_Started)
            {
                m_Transport.OnClientConnected -= HandleClientConnected;
                m_Transport.OnClientDisconnected -= HandleClientDisconnected;
                m_Transport.OnData -= HandleData;
            }

            m_Entities.Clear();
            m_ClientEntity.Clear();
            m_LastProcessedInput.Clear();
            m_Connections.Clear();

            m_Tick = 0u;
            m_Now = 0d;
            m_TickAccumulator = 0d;
            m_Started = false;

            OnClientJoined = null;
            OnClientLeft = null;
        }

        // —— 传输事件处理 ——

        private void HandleClientConnected(int connectionId)
        {
            m_Connections.Add(connectionId);
            if (!m_LastProcessedInput.ContainsKey(connectionId))
            {
                m_LastProcessedInput[connectionId] = 0u;
            }

            OnClientJoined?.Invoke(connectionId);
        }

        private void HandleClientDisconnected(int connectionId)
        {
            m_Connections.Remove(connectionId);
            m_LastProcessedInput.Remove(connectionId);

            // 取回该连接的受控实体（若有），断开时一并从权威实体表移除：否则该实体无人驱动，仍会在后续
            // 每帧快照里作为“僵尸实体”持续广播给其余客户端，长会话下随连接更替累积泄漏。
            int controlledEntityId = -1;
            if (m_ClientEntity.TryGetValue(connectionId, out controlledEntityId))
            {
                m_ClientEntity.Remove(connectionId);
                m_Entities.Remove(controlledEntityId);
            }

            OnClientLeft?.Invoke(connectionId, controlledEntityId);
        }

        private void HandleData(int connectionId, ArraySegment<byte> framed)
        {
            // 分派失败（解帧 / 处理器抛异常 = 协议错误）时，踢出该连接以隔离畸形 / 恶意负载。
            bool ok = m_Dispatcher.Dispatch(connectionId, framed);
            if (!ok)
            {
                FrameworkLog.Error(
                    "NetcodeServer 因协议错误踢出连接 connId={0}（分派失败）。", connectionId);
                m_Transport.Kick(connectionId);
            }
        }

        // —— 消息处理 ——

        private void HandleInput(int connectionId, InputMessage message)
        {
            // 该连接尚未被分配受控实体 —— 忽略其输入。
            int entityId;
            if (!m_ClientEntity.TryGetValue(connectionId, out entityId))
            {
                return;
            }

            // —— 不信任客户端输入：先校验 DeltaTime ——
            // 拒绝非有限步长（NaN / ±∞）：会污染权威模拟。
            if (!float.IsFinite(message.DeltaTime))
            {
                return;
            }

            // 钳制到 [0, MaxClientDeltaSec]：防止恶意 / 异常大步长把实体一次推进过远。
            float deltaTime = message.DeltaTime;
            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }
            else if (deltaTime > MaxClientDeltaSec)
            {
                deltaTime = MaxClientDeltaSec;
            }

            // —— 单调序号：仅当严格大于已处理序号时才处理；否则丢弃（不模拟、不更新 ack）——
            uint lastProcessed;
            if (m_LastProcessedInput.TryGetValue(connectionId, out lastProcessed) &&
                message.Sequence <= lastProcessed)
            {
                // 重放 / 乱序 / 重复的旧输入 —— 直接丢弃。
                return;
            }

            float[] current;
            if (!m_Entities.TryGetValue(entityId, out current))
            {
                // 受控实体已被移除 —— 仍推进 ack（序号已严格前进）以驱动客户端和解，但不应用模拟。
                m_LastProcessedInput[connectionId] = message.Sequence;
                return;
            }

            float[] input = message.Input ?? Array.Empty<float>();

            // 权威地推进：用确定性 step 计算下一状态并存回（使用钳制后的步长）。
            float[] next = m_SimulateStep(current, input, deltaTime);
            m_Entities[entityId] = next ?? Array.Empty<float>();

            // 仅在序号严格前进时更新该连接已处理到的输入序号（ack），供其和解。
            m_LastProcessedInput[connectionId] = message.Sequence;
        }

        // —— 发送辅助 ——

        private void SendTo(int connectionId, INetMessage message)
        {
            // 复用 m_PackWriter 并以零拷贝区间下发：传输层会在 Send 内同步拷贝，下一客户端复用 writer 安全。
            ArraySegment<byte> framed = m_Registry.PackInto(message, m_PackWriter);
            m_Transport.Send(connectionId, framed, NetDeliveryMethod.ReliableOrdered);
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
