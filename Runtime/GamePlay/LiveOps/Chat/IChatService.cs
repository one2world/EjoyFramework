//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 聊天服务接口：聚合若干 <see cref="ChatChannel"/>，订阅注入的 <see cref="IChatTransport"/>，
    /// 将到达消息按 <see cref="ChatMessage.ChannelId"/> 路由进对应频道，并维护屏蔽名单。
    /// 通过 <see cref="Framework.GetModule{T}"/> 获取实现；使用前须先调用
    /// <see cref="SetTransport"/> 注入真实传输层（未注入时默认使用 <see cref="NullChatTransport"/>）。
    /// </summary>
    public interface IChatService
    {
        /// <summary>当前使用的传输层（永不为 null）。</summary>
        IChatTransport Transport { get; }

        /// <summary>
        /// 一条非屏蔽消息被成功 Append 到其频道后触发：(服务, 消息)。
        /// 发往未知频道或来自屏蔽者的消息不会触发本事件。
        /// </summary>
        event Action<IChatService, ChatMessage> OnMessageReceived;

        /// <summary>
        /// 注入聊天传输层并订阅其到达事件。可重复调用以替换传输层；
        /// 替换时会先退订旧传输层的到达事件，再订阅新传输层。
        /// </summary>
        /// <param name="transport">注入的传输层；为 null 时使用 <see cref="NullChatTransport"/>。</param>
        void SetTransport(IChatTransport transport);

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
        ChatChannel CreateChannel(string id, ChatChannelType type, int capacity = 100);

        /// <summary>
        /// 按 Id 取频道。
        /// </summary>
        /// <param name="id">频道 Id。</param>
        /// <returns>对应频道；不存在或 id 为空则返回 null。</returns>
        ChatChannel GetChannel(string id);

        /// <summary>
        /// 移除一个频道。
        /// </summary>
        /// <param name="id">频道 Id。</param>
        /// <returns>移除成功返回 true；频道不存在或 id 为空返回 false。</returns>
        bool RemoveChannel(string id);

        /// <summary>
        /// 发送一条文本到指定频道：委托给传输层。
        /// 不做本地回显——服务器会经 <see cref="IChatTransport.OnReceived"/> 回送实际入历史的消息。
        /// </summary>
        /// <param name="channelId">目标频道 Id。</param>
        /// <param name="text">消息正文。</param>
        void Send(string channelId, string text);

        /// <summary>
        /// 屏蔽某玩家：其后续到达消息将在本服务被丢弃。按 senderId 匹配，幂等。
        /// </summary>
        /// <param name="playerId">被屏蔽玩家的 Id。空 Id 被忽略。</param>
        void Mute(string playerId);

        /// <summary>
        /// 取消屏蔽某玩家。幂等。
        /// </summary>
        /// <param name="playerId">玩家 Id。空 Id 被忽略。</param>
        void Unmute(string playerId);

        /// <summary>
        /// 查询某玩家是否被屏蔽。
        /// </summary>
        /// <param name="playerId">玩家 Id。</param>
        /// <returns>已屏蔽返回 true；否则（含空 Id）返回 false。</returns>
        bool IsMuted(string playerId);
    }
}
