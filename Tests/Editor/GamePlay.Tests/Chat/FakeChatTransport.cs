//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Chat;

namespace EjoyFramework.GamePlay.Tests.Chat
{
    /// <summary>
    /// 测试替身传输层：记录最后一次 Send 的参数，并允许测试手动触发 OnReceived 来模拟服务器回送。
    /// 不做任何真实网络 I/O；连接状态可由测试直接设置。
    /// </summary>
    public sealed class FakeChatTransport : IChatTransport
    {
        /// <summary>最后一次 <see cref="Send"/> 收到的频道 Id。未调用过则为 null。</summary>
        public string LastSentChannelId { get; private set; }

        /// <summary>最后一次 <see cref="Send"/> 收到的文本。未调用过则为 null。</summary>
        public string LastSentText { get; private set; }

        /// <summary><see cref="Send"/> 被调用的总次数。</summary>
        public int SendCount { get; private set; }

        /// <summary><see cref="Connect"/> 被调用的总次数。</summary>
        public int ConnectCount { get; private set; }

        /// <summary><see cref="Disconnect"/> 被调用的总次数。</summary>
        public int DisconnectCount { get; private set; }

        /// <summary>可由测试直接读写的连接状态。</summary>
        public bool IsConnected { get; set; }

        /// <summary>记录调用次数，并将状态置为已连接。</summary>
        public void Connect()
        {
            ConnectCount++;
            IsConnected = true;
        }

        /// <summary>记录调用次数，并将状态置为未连接。</summary>
        public void Disconnect()
        {
            DisconnectCount++;
            IsConnected = false;
        }

        /// <summary>记录最后一次发送的参数与调用次数；不触发任何回送。</summary>
        public void Send(string channelId, string text)
        {
            LastSentChannelId = channelId;
            LastSentText = text;
            SendCount++;
        }

        /// <summary>由 <see cref="ChatService"/> 订阅，用于接收模拟到达的消息。</summary>
        public event Action<ChatMessage> OnReceived;

        /// <summary>
        /// 测试钩子：手动触发一条到达消息，模拟服务器回送。
        /// </summary>
        /// <param name="message">要派发的消息。</param>
        public void RaiseReceived(ChatMessage message)
        {
            OnReceived?.Invoke(message);
        }
    }
}
