//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Netcode.Messages;

namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 加入握手消息（服务器→客户端）。
    /// <para>
    /// 当服务器为某个连接分配其「受控实体」后，向该客户端发送本消息，
    /// 告知它所控制的实体 id 以及服务器的 tick 频率，客户端据此初始化本地预测器。
    /// </para>
    /// </summary>
    public sealed class WelcomeMessage : INetMessage
    {
        /// <summary>
        /// 服务器分配给该客户端、由其输入驱动的受控实体 id。
        /// </summary>
        public int EntityId { get; set; }

        /// <summary>
        /// 服务器每秒模拟 tick 数（Hz）。客户端可据此估算网络时钟与渲染节奏。
        /// </summary>
        public float TickRateHz { get; set; }

        /// <inheritdoc />
        public void Serialize(NetWriter writer)
        {
            writer.WriteInt(EntityId);
            writer.WriteFloat(TickRateHz);
        }

        /// <inheritdoc />
        public void Deserialize(NetReader reader)
        {
            EntityId = reader.ReadInt();
            TickRateHz = reader.ReadFloat();
        }
    }
}
