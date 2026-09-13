//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core;

namespace EjoyFramework.GamePlay.Netcode.Messages
{
    /// <summary>
    /// 消息路由器：把到达的「成帧字节」解帧为具体消息，并分派给按类型注册的强类型处理器。
    ///
    /// 工作流（<see cref="Dispatch"/>）：
    /// 1) 读出帧头的 typeId；若该 id 未注册或无对应处理器 => 静默忽略（不抛异常）。
    /// 2) 经 <see cref="NetMessageRegistry"/> 构造实例并反序列化负载。
    /// 3) 以 <c>(connectionId, message)</c> 调用对应的强类型处理器。
    ///
    /// 处理器以 typeId 为键存储，外层包装把 <see cref="INetMessage"/> 强转为目标类型 T 后回调，
    /// 因此 typeId 与 T 必须由 <see cref="NetMessageRegistry"/> 的注册保证一致。
    /// 每个 typeId 仅保留最后一次 <see cref="On{T}"/> 注册的处理器（覆盖式）。
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关（仅依赖 System.*）。
    /// </summary>
    public sealed class MessageDispatcher
    {
        private readonly NetMessageRegistry m_Registry;
        private readonly Dictionary<ushort, Action<int, INetMessage>> m_HandlerById =
            new Dictionary<ushort, Action<int, INetMessage>>();

        /// <summary>
        /// 构造分派器。
        /// </summary>
        /// <param name="registry">类型注册表，不可为 null。处理器类型须先在此注册。</param>
        /// <exception cref="ArgumentNullException"><paramref name="registry"/> 为 null。</exception>
        public MessageDispatcher(NetMessageRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            m_Registry = registry;
        }

        /// <summary>
        /// 为消息类型 <typeparamref name="T"/> 注册处理器。重复注册同类型会覆盖既有处理器。
        /// </summary>
        /// <typeparam name="T">已在注册表登记的消息类型。</typeparam>
        /// <param name="handler">回调，参数为 (connectionId, message)。</param>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> 为 null。</exception>
        /// <exception cref="KeyNotFoundException">类型 <typeparamref name="T"/> 未在注册表登记。</exception>
        public void On<T>(Action<int, T> handler) where T : INetMessage
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            ushort typeId = m_Registry.GetTypeId<T>();
            // 包装为非泛型签名；分派时 message 必为 T（由 typeId<->T 注册一致性保证），故强转安全。
            m_HandlerById[typeId] = (connectionId, message) => handler(connectionId, (T)message);
        }

        /// <summary>
        /// 移除消息类型 <typeparamref name="T"/> 的处理器（若存在）。
        /// </summary>
        /// <typeparam name="T">已在注册表登记的消息类型。</typeparam>
        /// <exception cref="KeyNotFoundException">类型 <typeparamref name="T"/> 未在注册表登记。</exception>
        public void Clear<T>() where T : INetMessage
        {
            ushort typeId = m_Registry.GetTypeId<T>();
            m_HandlerById.Remove(typeId);
        }

        /// <summary>
        /// 解帧并分派一条到达消息。
        /// 未知 typeId、无对应处理器，或帧不足 2 字节时静默忽略（返回 <c>true</c>，不视为协议错误）。
        /// <para>
        /// 反序列化（<see cref="NetMessageRegistry.Unpack"/>）或处理器回调抛出异常时，本方法<b>不会</b>
        /// 让异常逸出：异常被捕获、记一次错误日志，并返回 <c>false</c>。调用方可据此对该连接采取
        /// 处置（如踢出），从而把畸形 / 恶意负载与正常流量隔离。
        /// </para>
        /// </summary>
        /// <param name="connectionId">来源连接 id，将原样传给处理器。</param>
        /// <param name="framed">「<c>[ushort typeId][payload]</c>」字节区间。</param>
        /// <returns>
        /// <c>true</c> 表示成功分派或被安全忽略（无处理器 / 未知类型 / 帧截断）；
        /// <c>false</c> 表示解帧或处理器执行期间抛出了异常（协议错误），调用方宜对该连接采取处置。
        /// </returns>
        public bool Dispatch(int connectionId, ArraySegment<byte> framed)
        {
            ushort typeId;
            if (!m_Registry.TryPeekTypeId(framed, out typeId))
            {
                // 帧截断或类型 id 未注册 —— 静默忽略（非协议错误）。
                return true;
            }

            Action<int, INetMessage> handler;
            if (!m_HandlerById.TryGetValue(typeId, out handler))
            {
                // 已注册类型但无处理器 —— 静默忽略（非协议错误）。
                return true;
            }

            try
            {
                ushort unpackedTypeId;
                INetMessage message = m_Registry.Unpack(framed, out unpackedTypeId);
                handler(connectionId, message);
                return true;
            }
            catch (Exception ex)
            {
                // 反序列化或处理器抛异常：隔离之，记一次日志并上报失败，由调用方决定是否踢出该连接。
                FrameworkLog.Error(
                    "MessageDispatcher.Dispatch failed for connId={0}, typeId={1}: {2}", connectionId, typeId, ex);
                return false;
            }
        }
    }
}
