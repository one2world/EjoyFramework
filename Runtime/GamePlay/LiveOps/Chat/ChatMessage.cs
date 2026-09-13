//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 一条聊天消息：不可变值对象，在传输层与领域逻辑之间传递。
    /// 由网络层解析到达消息后构造，经 <see cref="ChatService"/> 路由进入对应 <see cref="ChatChannel"/>。
    /// 纯逻辑、与引擎无关。
    /// </summary>
    public readonly struct ChatMessage
    {
        private readonly string m_Id;
        private readonly string m_ChannelId;
        private readonly string m_SenderId;
        private readonly string m_SenderName;
        private readonly string m_Text;
        private readonly long m_EpochMs;

        /// <summary>
        /// 构造一条聊天消息。所有字段均原样保存，不做校验（消息可能来自不可信网络，
        /// 校验/清洗交由上层管线处理）。
        /// </summary>
        /// <param name="id">消息唯一 Id（由服务器分配，可用于去重）。</param>
        /// <param name="channelId">目标频道 Id。</param>
        /// <param name="senderId">发送者唯一 Id（屏蔽以此为准）。</param>
        /// <param name="senderName">发送者显示名。</param>
        /// <param name="text">消息正文。</param>
        /// <param name="epochMs">发送时间戳（Unix 毫秒）。</param>
        public ChatMessage(string id, string channelId, string senderId, string senderName, string text, long epochMs)
        {
            m_Id = id;
            m_ChannelId = channelId;
            m_SenderId = senderId;
            m_SenderName = senderName;
            m_Text = text;
            m_EpochMs = epochMs;
        }

        /// <summary>消息唯一 Id。</summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>目标频道 Id。</summary>
        public string ChannelId
        {
            get { return m_ChannelId; }
        }

        /// <summary>发送者唯一 Id。屏蔽以此字段为准。</summary>
        public string SenderId
        {
            get { return m_SenderId; }
        }

        /// <summary>发送者显示名。</summary>
        public string SenderName
        {
            get { return m_SenderName; }
        }

        /// <summary>消息正文。</summary>
        public string Text
        {
            get { return m_Text; }
        }

        /// <summary>发送时间戳（Unix 毫秒）。</summary>
        public long EpochMs
        {
            get { return m_EpochMs; }
        }
    }
}
