//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 单个聊天频道：维护一段固定容量的历史消息环形缓冲（满后丢弃最旧的一条）。
    /// 每追加一条消息触发一次 <see cref="OnMessage"/>。容量上限由构造时的 capacity 决定。
    /// 单线程使用，非线程安全。纯逻辑、与引擎无关。
    /// </summary>
    public sealed class ChatChannel
    {
        private readonly string m_Id;
        private readonly ChatChannelType m_Type;
        private readonly int m_Capacity;

        // 环形缓冲：m_Buffer 存底层槽位，m_Head 指向最旧元素，m_Count 为当前条数（<= Capacity）。
        private readonly ChatMessage[] m_Buffer;
        private int m_Head;
        private int m_Count;

        /// <summary>
        /// 构造频道。
        /// </summary>
        /// <param name="id">频道 Id，不可为空。</param>
        /// <param name="type">频道类型。</param>
        /// <param name="capacity">历史环形缓冲容量，必须为正，默认 100。</param>
        /// <exception cref="ArgumentException">id 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">capacity 不为正。</exception>
        public ChatChannel(string id, ChatChannelType type, int capacity = 100)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("频道 Id 不能为空。", nameof(id));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "频道容量必须为正。");
            }

            m_Id = id;
            m_Type = type;
            m_Capacity = capacity;
            m_Buffer = new ChatMessage[capacity];
            m_Head = 0;
            m_Count = 0;
        }

        /// <summary>频道 Id。</summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>频道类型。</summary>
        public ChatChannelType Type
        {
            get { return m_Type; }
        }

        /// <summary>历史环形缓冲容量。</summary>
        public int Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>
        /// 历史消息快照，按由旧到新的顺序排列，最多 <see cref="Capacity"/> 条。
        /// 每次访问返回一个新的只读拷贝，调用方持有后不受后续 <see cref="Append"/> 影响。
        /// </summary>
        public IReadOnlyList<ChatMessage> History
        {
            get
            {
                ChatMessage[] ordered = new ChatMessage[m_Count];
                for (int i = 0; i < m_Count; i++)
                {
                    ordered[i] = m_Buffer[(m_Head + i) % m_Capacity];
                }

                return ordered;
            }
        }

        /// <summary>
        /// 当本频道追加一条新消息时触发：(频道, 消息)。
        /// </summary>
        public event Action<ChatChannel, ChatMessage> OnMessage;

        /// <summary>
        /// 追加一条消息到环形缓冲。若已满，则覆盖（丢弃）最旧的一条；随后触发 <see cref="OnMessage"/>。
        /// </summary>
        /// <param name="message">要追加的消息。</param>
        public void Append(ChatMessage message)
        {
            if (m_Count < m_Capacity)
            {
                // 未满：写入下一个空槽（head + count），条数 +1。
                int tail = (m_Head + m_Count) % m_Capacity;
                m_Buffer[tail] = message;
                m_Count++;
            }
            else
            {
                // 已满：覆盖最旧槽（即 head），并将 head 前移——等效丢弃最旧一条。
                m_Buffer[m_Head] = message;
                m_Head = (m_Head + 1) % m_Capacity;
            }

            OnMessage?.Invoke(this, message);
        }
    }
}
