//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Transport;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 服务器权威（server-authoritative）网络门面的对外抽象：组合传输层、消息层与确定性模拟步进，
    /// 暴露一套「开箱即用」的服务器 API。通过 <see cref="EjoyFramework.Core.Framework.GetModule{T}"/> 解析为
    /// <see cref="NetcodeServer"/>。使用前须经 <see cref="SetTransport"/> 注入底层传输（<see cref="INetTransport"/>）。
    /// </summary>
    public interface INetcodeServer
    {
        /// <summary>
        /// 当前所有服务器实体的权威状态（只读视图）。键为实体 id，值为状态向量。
        /// </summary>
        IReadOnlyDictionary<int, float[]> Entities { get; }

        /// <summary>
        /// 新客户端连接成功时触发，参数为其连接 id。注意：此时尚未发送 <see cref="WelcomeMessage"/>，
        /// 需在回调内（或之后）调用 <see cref="AssignClientEntity"/> 才会下发握手。
        /// </summary>
        event Action<int> OnClientJoined;

        /// <summary>
        /// 客户端断开时触发：参数为 (连接 id, 其受控实体 id；无则 -1)。断开时该连接的受控实体会自动从权威
        /// 实体表移除，避免“僵尸实体”继续进入快照；受控实体 id 一并回传供上层做最终状态 / 重生处理。
        /// </summary>
        event Action<int, int> OnClientLeft;

        /// <summary>
        /// 注入底层传输（须为服务器角色或可启动为服务器），是本模块的必需配置。
        /// </summary>
        /// <param name="transport">网络传输实现，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> 为 null。</exception>
        void SetTransport(INetTransport transport);

        /// <summary>
        /// 注入确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>，与客户端须完全一致。
        /// </summary>
        /// <param name="simulateStep">确定性模拟步进委托，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="simulateStep"/> 为 null。</exception>
        void SetSimulateStep(Func<float[], float[], float, float[]> simulateStep);

        /// <summary>
        /// 设置服务器 tick 频率（Hz），随 <see cref="WelcomeMessage"/> 下发给客户端。
        /// </summary>
        /// <param name="tickRateHz">tick 频率（Hz），必须为正。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRateHz"/> 不为正。</exception>
        void SetTickRate(double tickRateHz);

        /// <summary>
        /// 以服务器角色启动并接管传输事件：监听端口，并把 <see cref="INetTransport.OnData"/> 路由到内部分派器。
        /// </summary>
        /// <param name="port">监听端口（回环传输忽略）。</param>
        void Start(int port);

        /// <summary>
        /// 注册一个服务器拥有的实体（NPC，或将被分配给某客户端的受控实体）。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <param name="initialValues">初始状态向量；null 视为长度 0。内部保存一份拷贝。</param>
        /// <returns>实际存入的状态向量（内部拷贝）。</returns>
        float[] RegisterEntity(int entityId, float[] initialValues);

        /// <summary>
        /// 直接设置某实体的权威状态（如服务器侧 AI/物理推进）。内部保存一份拷贝。
        /// </summary>
        /// <param name="entityId">实体唯一标识（须已注册或将被创建）。</param>
        /// <param name="values">新状态向量；null 视为长度 0。</param>
        void SetEntityState(int entityId, float[] values);

        /// <summary>
        /// 移除一个服务器实体。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <returns>存在并移除返回 true，否则 false。</returns>
        bool RemoveEntity(int entityId);

        /// <summary>
        /// 把某实体指定为某连接的「受控实体」（其输入将驱动该实体），并立即向该客户端发送
        /// <see cref="WelcomeMessage"/>（携带实体 id 与 tick 频率）。
        /// </summary>
        /// <param name="connectionId">目标连接 id。</param>
        /// <param name="entityId">该连接受控的实体 id。</param>
        void AssignClientEntity(int connectionId, int entityId);

        /// <summary>
        /// 排空传输事件：驱动 <see cref="INetTransport.Poll"/>，从而处理连接/断开与到达的 <see cref="InputMessage"/>。
        /// </summary>
        void Poll();

        /// <summary>
        /// 推进一个服务器 tick：构建一份包含所有实体的 <see cref="WorldSnapshot"/>，
        /// 并向每个已连接客户端下发一条 <see cref="SnapshotMessage"/>（携带该客户端专属的已处理输入序号），最后 tick 自增。
        /// </summary>
        /// <param name="serverTimeSec">当前服务器时间（秒），写入快照作为客户端插值的时间轴。</param>
        void Tick(double serverTimeSec);
    }
}
