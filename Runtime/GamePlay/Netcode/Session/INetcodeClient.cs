//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Sync;
using EjoyFramework.GamePlay.Netcode.Transport;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 服务器权威架构下客户端网络门面的对外抽象：组合传输层、消息层、快照插值与「客户端预测 + 和解」，
    /// 暴露一套「开箱即用」的客户端 API。通过 <see cref="EjoyFramework.Core.Framework.GetModule{T}"/> 解析为
    /// <see cref="NetcodeClient"/>。使用前须经 <see cref="SetTransport"/> 注入底层传输（<see cref="INetTransport"/>）。
    /// </summary>
    public interface INetcodeClient
    {
        /// <summary>
        /// 客户端连接状态。
        /// </summary>
        NetConnectionState State { get; }

        /// <summary>
        /// 本地玩家受控的实体 id；在收到 <see cref="WelcomeMessage"/> 前为 -1。
        /// </summary>
        int LocalEntityId { get; }

        /// <summary>
        /// 本地玩家的<b>预测</b>状态向量。未握手前为空数组。
        /// </summary>
        float[] PredictedLocalState { get; }

        /// <summary>
        /// 收到 <see cref="WelcomeMessage"/>（握手完成）时触发一次。
        /// </summary>
        event Action OnConnected;

        /// <summary>
        /// 与服务器断开时触发。
        /// </summary>
        event Action OnDisconnected;

        /// <summary>
        /// 注入底层传输（客户端角色），是本模块的必需配置。
        /// </summary>
        /// <param name="transport">网络传输实现，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="transport"/> 为 null。</exception>
        void SetTransport(INetTransport transport);

        /// <summary>
        /// 注入与服务器<b>完全一致</b>的确定性模拟步进 <c>(state, input, dt) =&gt; nextState</c>。
        /// </summary>
        /// <param name="simulateStep">确定性模拟步进委托，不可为 null。</param>
        /// <exception cref="ArgumentNullException"><paramref name="simulateStep"/> 为 null。</exception>
        void SetSimulateStep(Func<float[], float[], float, float[]> simulateStep);

        /// <summary>
        /// 设置远端实体插值延迟（秒）。须在 <see cref="Connect"/> 之前设置以生效。
        /// </summary>
        /// <param name="interpolationDelaySec">插值延迟（秒），不可为负。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interpolationDelaySec"/> 为负。</exception>
        void SetInterpolationDelay(double interpolationDelaySec);

        /// <summary>
        /// 接管传输事件并发起连接：把 <see cref="INetTransport.OnData"/> 路由到内部分派器，随后连接服务器。
        /// </summary>
        /// <param name="host">主机地址（回环传输忽略）。</param>
        /// <param name="port">端口（回环传输忽略）。</param>
        void Connect(string host, int port);

        /// <summary>
        /// 本地输入：立即本地预测（推进 <see cref="PredictedLocalState"/>）并把对应 <see cref="InputMessage"/> 发往服务器。
        /// 在收到 <see cref="WelcomeMessage"/> 之前调用将被忽略（尚无受控实体与预测器）。
        /// </summary>
        /// <param name="input">输入向量（如 [vx, vy]）；null 视为长度 0。</param>
        /// <param name="deltaTime">该输入对应的模拟步长（秒）。</param>
        void SendInput(float[] input, float deltaTime);

        /// <summary>
        /// 排空传输事件：驱动 <see cref="INetTransport.Poll"/>，从而处理握手 / 快照 / 断开。
        /// </summary>
        void Poll();

        /// <summary>
        /// 推进客户端本地时间：记录当前本地时间以供快照到达时喂入网络时钟与渲染采样。
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        void Tick(double localTimeSec);

        /// <summary>
        /// 在给定本地时间上，返回所有<b>远端</b>实体（排除本地玩家受控实体）的插值状态。
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        /// <returns>远端实体插值状态集合（新列表，已排除本地玩家）。</returns>
        IReadOnlyList<EntitySnapshot> GetInterpolatedEntities(double localTimeSec);

        /// <summary>
        /// 非分配重载：把所有远端实体（排除本地玩家）的插值状态填入 <paramref name="into"/>（先清空）。
        /// 适合每帧调用以避免 GC 压力。
        /// </summary>
        /// <param name="localTimeSec">当前本地时间（秒）。</param>
        /// <param name="into">装载结果的列表，调用前会被清空；不可为 null。</param>
        void GetInterpolatedEntities(double localTimeSec, List<EntitySnapshot> into);
    }
}
