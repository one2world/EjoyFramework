//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 聊天传输层抽象：将真实网络通道（WebSocket / 长轮询 / 其他）与聊天领域逻辑解耦。
    /// 由游戏侧注入具体实现；<see cref="ChatService"/> 仅依赖此接口，从而可在无网络环境下完整单元测试。
    /// 约定：<see cref="Send"/> 后服务器会通过 <see cref="OnReceived"/> 回送（含自己发出的消息），
    /// 故领域层不做本地回显，统一由 <see cref="OnReceived"/> 驱动。
    /// </summary>
    public interface IChatTransport
    {
        /// <summary>是否已连接。未连接时 <see cref="Send"/> 通常应为无操作或入队。</summary>
        bool IsConnected { get; }

        /// <summary>建立连接。可重复调用，实现应保证幂等。</summary>
        void Connect();

        /// <summary>断开连接。可重复调用，实现应保证幂等。</summary>
        void Disconnect();

        /// <summary>
        /// 向指定频道发送一段文本。具体编码与可靠性由实现决定；
        /// 服务器处理后会经 <see cref="OnReceived"/> 回送对应 <see cref="ChatMessage"/>。
        /// </summary>
        /// <param name="channelId">目标频道 Id。</param>
        /// <param name="text">消息正文。</param>
        void Send(string channelId, string text);

        /// <summary>当一条消息从网络到达时触发，携带解析好的 <see cref="ChatMessage"/>。</summary>
        event Action<ChatMessage> OnReceived;
    }
}
