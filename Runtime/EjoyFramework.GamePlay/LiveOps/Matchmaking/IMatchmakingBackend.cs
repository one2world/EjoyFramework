//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 匹配/房间后端抽象（与传输无关）。真实实现可对接服务器或中继；测试与单机使用内存实现。
    /// 所有方法都以回调返回结果，从而既兼容同步内存实现（确定性、便于测试），也兼容异步网络实现。
    /// 该层只决定“谁进入哪个房间”，不负责字节传输；后续由 netcode 层据此建立连接。
    /// </summary>
    public interface IMatchmakingBackend
    {
        /// <summary>
        /// 创建房间，并令房主立即加入。创建完成后通过回调返回房间实例；失败时回调 null。
        /// </summary>
        /// <param name="name">房间显示名。</param>
        /// <param name="maxPlayers">最大玩家数。</param>
        /// <param name="hostId">房主玩家标识。</param>
        /// <param name="isPrivate">是否私有房间。</param>
        /// <param name="onResult">结果回调；参数为创建出的房间，失败时为 null。</param>
        void CreateRoom(string name, int maxPlayers, string hostId, bool isPrivate, Action<Room> onResult);

        /// <summary>
        /// 加入指定房间。成功时回调对应房间；房间不存在/已满/不可加入时回调 null。
        /// </summary>
        /// <param name="roomId">目标房间标识。</param>
        /// <param name="playerId">加入的玩家标识。</param>
        /// <param name="onResult">结果回调；不可加入时为 null。</param>
        void JoinRoom(string roomId, string playerId, Action<Room> onResult);

        /// <summary>
        /// 离开指定房间。无论房间或玩家是否存在，完成后都会回调 <paramref name="onDone"/>。
        /// </summary>
        /// <param name="roomId">目标房间标识。</param>
        /// <param name="playerId">离开的玩家标识。</param>
        /// <param name="onDone">完成回调。</param>
        void LeaveRoom(string roomId, string playerId, Action onDone);

        /// <summary>
        /// 列出可加入的公开房间：公开（非私有）、未满、且处于 <see cref="RoomState.Waiting"/> 的房间。
        /// </summary>
        /// <param name="onResult">结果回调；参数为只读房间列表（可能为空）。</param>
        void ListRooms(Action<IReadOnlyList<Room>> onResult);

        /// <summary>
        /// 快速匹配：加入首个可用的等待中公开房间（容量匹配），若无则创建一个新房间。
        /// 成功时回调最终所在的房间。
        /// </summary>
        /// <param name="playerId">发起匹配的玩家标识。</param>
        /// <param name="maxPlayers">期望的房间容量。</param>
        /// <param name="onResult">结果回调；参数为最终所在的房间。</param>
        void QuickMatch(string playerId, int maxPlayers, Action<Room> onResult);
    }
}
