//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Network
{
    /// <summary>
    /// 包处理器：业务侧实现并注册到 PacketRegistry，按 packet.Id 派发。
    /// </summary>
    public interface IPacketHandler
    {
        int PacketId { get; }
        void Handle(Packet packet, INetworkChannel channel);
    }

    /// <summary>
    /// 强类型 helper：业务直接继承 <see cref="PacketHandler{T}"/>，无需手动 cast。
    /// </summary>
    public abstract class PacketHandler<T> : IPacketHandler where T : Packet
    {
        public abstract int PacketId { get; }
        public void Handle(Packet packet, INetworkChannel channel) => OnHandle((T)packet, channel);
        protected abstract void OnHandle(T packet, INetworkChannel channel);
    }

    /// <summary>
    /// PacketId → Handler 注册表 + 收到 Packet 时按 Id 派发。
    /// 线程安全：仅主线程读写（业务 helper 应把网络线程的 packet 投到主线程后再调 Dispatch）。
    /// </summary>
    public sealed class PacketRegistry
    {
        private readonly Dictionary<int, IPacketHandler> m_Handlers = new Dictionary<int, IPacketHandler>();

        public int HandlerCount => m_Handlers.Count;

        public void Register(IPacketHandler handler)
        {
            if (handler == null) throw new FrameworkException("Handler is invalid.");
            if (m_Handlers.ContainsKey(handler.PacketId))
                throw new FrameworkException(string.Format("PacketHandler for id {0} already registered.", handler.PacketId));
            m_Handlers[handler.PacketId] = handler;
        }

        public bool Unregister(int packetId) => m_Handlers.Remove(packetId);
        public void Clear() => m_Handlers.Clear();

        /// <summary>派发收到的 packet；找不到 handler 不报错（业务可监听 NetworkChannel.UnhandledPacket 事件）。</summary>
        public bool Dispatch(Packet packet, INetworkChannel channel)
        {
            if (packet == null) return false;
            if (!m_Handlers.TryGetValue(packet.Id, out var h)) return false;
            try { h.Handle(packet, channel); return true; }
            catch (Exception ex)
            {
                FrameworkLog.Error("PacketHandler for id {0} threw: {1}", packet.Id, ex);
                return false;
            }
        }
    }
}
