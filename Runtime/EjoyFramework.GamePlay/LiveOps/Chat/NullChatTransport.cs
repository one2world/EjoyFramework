//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 空实现传输层（空对象模式）：所有操作均为无操作，<see cref="IsConnected"/> 恒为 false，
    /// 永不触发 <see cref="OnReceived"/>。作为 <see cref="ChatService"/> 的默认传输，
    /// 使聊天系统在未注入真实网络通道时仍可安全运行（消息只能本地构造并直接 Append 到频道）。
    /// </summary>
    public sealed class NullChatTransport : IChatTransport
    {
        /// <summary>恒为 false：空传输从不建立连接。</summary>
        public bool IsConnected
        {
            get { return false; }
        }

        /// <summary>无操作。</summary>
        public void Connect()
        {
        }

        /// <summary>无操作。</summary>
        public void Disconnect()
        {
        }

        /// <summary>无操作：空传输不发送任何数据。</summary>
        /// <param name="channelId">忽略。</param>
        /// <param name="text">忽略。</param>
        public void Send(string channelId, string text)
        {
        }

        /// <summary>
        /// 永不触发。保留事件声明以满足接口契约；订阅者不会收到任何回调。
        /// （显式 add/remove 为空实现，避免编译器为未触发事件生成告警。）
        /// </summary>
        public event Action<ChatMessage> OnReceived
        {
            add { }
            remove { }
        }
    }
}
