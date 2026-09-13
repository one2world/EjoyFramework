//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 匹配/房间客户端接口。封装“本地玩家如何进出房间”的流程：把创建/加入/快速匹配/离开/拉列表
    /// 统一路由到注入的 <see cref="IMatchmakingBackend"/>，并跟踪当前所在房间 <see cref="CurrentRoom"/>，
    /// 在状态变迁时对外抛出事件。后端可为空实现、内存实现或真实网络实现，客户端逻辑对此无感知（传输无关）。
    /// 通过 <see cref="Framework.GetModule{T}"/> 以 <see cref="IMatchmakingClient"/> 获取，使用前须先
    /// <see cref="SetBackend"/> 注入后端、<see cref="SetLocalPlayerId"/> 设置本地玩家标识。
    /// </summary>
    public interface IMatchmakingClient
    {
        /// <summary>
        /// 本地玩家唯一标识。未通过 <see cref="SetLocalPlayerId"/> 设置时为 null。
        /// </summary>
        string LocalPlayerId { get; }

        /// <summary>
        /// 当前所在房间；未在任何房间内时为 null。
        /// </summary>
        Room CurrentRoom { get; }

        /// <summary>
        /// 注入匹配后端。为 null 时退化为 <see cref="NullMatchmakingBackend"/>。
        /// </summary>
        /// <param name="backend">匹配后端实现。</param>
        void SetBackend(IMatchmakingBackend backend);

        /// <summary>
        /// 设置本地玩家唯一标识。
        /// </summary>
        /// <param name="localPlayerId">本地玩家唯一标识。</param>
        void SetLocalPlayerId(string localPlayerId);

        /// <summary>
        /// 创建房间（本地玩家作为房主）。成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>。
        /// </summary>
        /// <param name="name">房间显示名。</param>
        /// <param name="maxPlayers">最大玩家数。</param>
        /// <param name="isPrivate">是否私有房间。</param>
        void CreateRoom(string name, int maxPlayers, bool isPrivate = false);

        /// <summary>
        /// 加入指定房间。成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>；失败则不改变当前状态。
        /// </summary>
        /// <param name="roomId">目标房间标识。</param>
        void JoinRoom(string roomId);

        /// <summary>
        /// 快速匹配：加入一个等待中公开房间，或在无可用房间时新建。
        /// 成功后设置 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomJoined"/>。
        /// </summary>
        /// <param name="maxPlayers">期望的房间容量。</param>
        void QuickMatch(int maxPlayers);

        /// <summary>
        /// 离开当前房间。无论后端结果如何，本地都会清空 <see cref="CurrentRoom"/> 并触发 <see cref="OnRoomLeft"/>。
        /// 未在任何房间内时为空操作。
        /// </summary>
        void Leave();

        /// <summary>
        /// 拉取可加入的公开房间列表，结果通过 <see cref="OnRoomList"/> 抛出。
        /// </summary>
        void RefreshRoomList();

        /// <summary>
        /// 成功进入某房间（创建/加入/快速匹配）后触发，参数为所进入的房间。
        /// </summary>
        event Action<Room> OnRoomJoined;

        /// <summary>
        /// 离开当前房间后触发。
        /// </summary>
        event Action OnRoomLeft;

        /// <summary>
        /// <see cref="RefreshRoomList"/> 完成后触发，参数为可加入的公开房间列表。
        /// </summary>
        event Action<IReadOnlyList<Room>> OnRoomList;
    }
}
