//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Chat
{
    /// <summary>
    /// 聊天频道类型。决定一个 <see cref="ChatChannel"/> 的语义分类（世界/公会/队伍/私聊/系统），
    /// 不影响其历史与广播逻辑——这些仅由 <see cref="ChatChannel"/> 的环形缓冲与容量决定。
    /// </summary>
    public enum ChatChannelType
    {
        /// <summary>世界频道：全服可见的公共聊天。</summary>
        World,

        /// <summary>公会频道：公会成员间的聊天。</summary>
        Guild,

        /// <summary>队伍频道：临时队伍内的聊天。</summary>
        Team,

        /// <summary>私聊频道：两名玩家之间的一对一会话。</summary>
        Private,

        /// <summary>系统频道：服务器广播、公告等系统消息。</summary>
        System,
    }
}
