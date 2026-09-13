//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 聊天服务：聚合若干 <see cref="ChatChannel"/>，订阅注入的 <see cref="IChatTransport"/>，
    /// 将到达消息按 <see cref="ChatMessage.ChannelId"/> 路由进对应频道，并维护屏蔽名单。
    ///
    /// 到达处理（<see cref="HandleTransportReceived"/> 回调）流程：
    /// 1) 若发送者被屏蔽（<see cref="IsMuted"/> 按 <see cref="ChatMessage.SenderId"/> 判定）=> 丢弃，不入历史、不触发事件。
    /// 2) 否则按 ChannelId 查频道；若频道不存在 => 丢弃（频道创建是调用方职责，本服务不自动建频道）。
    /// 3) 将消息 Append 到该频道（触发频道自身的 OnMessage），随后触发 <see cref="OnMessageReceived"/>。
    ///
    /// 发送（<see cref="Send"/>）仅委托给传输层；服务器会经 <see cref="IChatTransport.OnReceived"/> 回送，故本服务不做本地回显。
    /// 作为 <see cref="FrameworkModule"/> 经 <see cref="Framework.GetModule{T}"/> 获取；使用前须先调用
    /// <see cref="SetTransport"/> 注入真实传输层。单线程使用，非线程安全。纯逻辑、与引擎无关。
    /// </summary>
    public sealed class ChatService : FrameworkModule, IChatService
    {
        private IChatTransport m_Transport;
        private readonly Dictionary<string, ChatChannel> m_Channels =
            new Dictionary<string, ChatChannel>(StringComparer.Ordinal);
        private readonly HashSet<string> m_MutedPlayerIds =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 无参构造：满足 <see cref="Framework.GetModule{T}"/> 经 Activator 实例化的契约。
        /// 默认使用 <see cref="NullChatTransport"/>，业务侧须随后调用 <see cref="SetTransport"/> 注入真实传输层。
        /// </summary>
        public ChatService()
        {
            SetTransport(null);
        }

        /// <summary>
        /// 便捷构造：直接注入传输层。供单元测试与少量手动装配场景使用。
        /// </summary>
        /// <param name="transport">注入的传输层；为 null 时使用 <see cref="NullChatTransport"/>。</param>
        internal ChatService(IChatTransport transport)
        {
            SetTransport(transport);
        }

        /// <summary>聊天服务无需逐帧驱动，故优先级取默认值 0。</summary>
        public override int Priority
        {
            get { return 0; }
        }

        /// <summary>本模块要求外部注入 <see cref="IChatTransport"/> 后方可正常工作。</summary>
        public override bool RequiresConfiguration
        {
            get { return true; }
        }

        /// <summary>
        /// 是否已注入<b>真实</b>传输层。默认 <see cref="NullChatTransport"/> 视为未配置，
        /// 提示业务侧调用 <see cref="SetTransport"/> 注入网络通道。
        /// </summary>
        public override bool IsModuleConfigured
        {
            get { return m_Transport != null && !(m_Transport is NullChatTransport); }
        }

        /// <summary>未配置时的修复提示。</summary>
        public override string ConfigurationHint
        {
            get { return "Call SetTransport(IChatTransport) before use."; }
        }

        /// <summary>当前使用的传输层（永不为 null）。</summary>
        public IChatTransport Transport
        {
            get { return m_Transport; }
        }

        /// <summary>
        /// 一条非屏蔽消息被成功 Append 到其频道后触发：(服务, 消息)。
        /// 发往未知频道或来自屏蔽者的消息不会触发本事件。
        /// </summary>
        public event Action<IChatService, ChatMessage> OnMessageReceived;

        /// <summary>
        /// 注入聊天传输层并订阅其到达事件。可重复调用以替换传输层；
        /// 替换时会先退订旧传输层的到达事件，再订阅新传输层。
        /// </summary>
        /// <param name="transport">注入的传输层；为 null 时使用 <see cref="NullChatTransport"/>。</param>
        public void SetTransport(IChatTransport transport)
        {
            if (m_Transport != null)
            {
                m_Transport.OnReceived -= HandleTransportReceived;
            }

            m_Transport = transport ?? new NullChatTransport();
            m_Transport.OnReceived += HandleTransportReceived;
        }

        /// <summary>
        /// 创建并注册一个频道。
        /// </summary>
        /// <param name="id">频道 Id，不可为空，且不可与已有频道重复。</param>
        /// <param name="type">频道类型。</param>
        /// <param name="capacity">历史环形缓冲容量，默认 100。</param>
        /// <returns>新建的频道。</returns>
        /// <exception cref="ArgumentException">id 为空。</exception>
        /// <exception cref="InvalidOperationException">id 已存在。</exception>
        /// <exception cref="ArgumentOutOfRangeException">capacity 不为正。</exception>
        public ChatChannel CreateChannel(string id, ChatChannelType type, int capacity = 100)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("频道 Id 不能为空。", nameof(id));
            }

            if (m_Channels.ContainsKey(id))
            {
                throw new InvalidOperationException($"频道 Id 已存在：{id}。");
            }

            ChatChannel channel = new ChatChannel(id, type, capacity);
            m_Channels.Add(id, channel);
            return channel;
        }

        /// <summary>
        /// 按 Id 取频道。
        /// </summary>
        /// <param name="id">频道 Id。</param>
        /// <returns>对应频道；不存在或 id 为空则返回 null。</returns>
        public ChatChannel GetChannel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            m_Channels.TryGetValue(id, out ChatChannel channel);
            return channel;
        }

        /// <summary>
        /// 移除一个频道。
        /// </summary>
        /// <param name="id">频道 Id。</param>
        /// <returns>移除成功返回 true；频道不存在或 id 为空返回 false。</returns>
        public bool RemoveChannel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            return m_Channels.Remove(id);
        }

        /// <summary>
        /// 发送一条文本到指定频道：委托给传输层。
        /// 不做本地回显——服务器会经 <see cref="IChatTransport.OnReceived"/> 回送实际入历史的消息。
        /// </summary>
        /// <param name="channelId">目标频道 Id。</param>
        /// <param name="text">消息正文。</param>
        public void Send(string channelId, string text)
        {
            m_Transport.Send(channelId, text);
        }

        /// <summary>
        /// 屏蔽某玩家：其后续到达消息将在本服务被丢弃。按 senderId 匹配，幂等。
        /// </summary>
        /// <param name="playerId">被屏蔽玩家的 Id。空 Id 被忽略。</param>
        public void Mute(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            m_MutedPlayerIds.Add(playerId);
        }

        /// <summary>
        /// 取消屏蔽某玩家。幂等。
        /// </summary>
        /// <param name="playerId">玩家 Id。空 Id 被忽略。</param>
        public void Unmute(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return;
            }

            m_MutedPlayerIds.Remove(playerId);
        }

        /// <summary>
        /// 查询某玩家是否被屏蔽。
        /// </summary>
        /// <param name="playerId">玩家 Id。</param>
        /// <returns>已屏蔽返回 true；否则（含空 Id）返回 false。</returns>
        public bool IsMuted(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            return m_MutedPlayerIds.Contains(playerId);
        }

        /// <summary>聊天服务无逐帧工作：到达消息由传输层事件驱动，故此处为空实现。</summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
        }

        /// <summary>
        /// 关闭并清理：退订传输层到达事件，清空频道与屏蔽名单，复位为空传输层。
        /// </summary>
        public override void Shutdown()
        {
            if (m_Transport != null)
            {
                m_Transport.OnReceived -= HandleTransportReceived;
            }

            m_Channels.Clear();
            m_MutedPlayerIds.Clear();
            OnMessageReceived = null;
            m_Transport = new NullChatTransport();
        }

        // 传输层到达回调：屏蔽过滤 -> 路由到频道 -> 触发 OnMessageReceived。
        private void HandleTransportReceived(ChatMessage message)
        {
            // 1) 屏蔽发送者 => 丢弃。
            if (IsMuted(message.SenderId))
            {
                return;
            }

            // 2) 未知频道 => 丢弃（不自动建频道）。
            ChatChannel channel = GetChannel(message.ChannelId);
            if (channel == null)
            {
                return;
            }

            // 3) 入历史（触发频道 OnMessage），再触发服务级事件。
            channel.Append(message);
            OnMessageReceived?.Invoke(this, message);
        }
    }
}
