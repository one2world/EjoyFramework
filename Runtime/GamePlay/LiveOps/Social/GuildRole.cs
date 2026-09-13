//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Social
{
    /// <summary>
    /// 公会成员职位。数值由低到高表示权限等级，便于 <see cref="Guild.Promote"/>/<see cref="Guild.Demote"/> 按层级升降。
    /// </summary>
    public enum GuildRole
    {
        /// <summary>普通成员，最低层级。</summary>
        Member = 0,

        /// <summary>精英成员。</summary>
        Elder = 1,

        /// <summary>副会长。</summary>
        ViceLeader = 2,

        /// <summary>会长，最高层级；任一时刻全公会至多一名。</summary>
        Leader = 3,
    }
}
