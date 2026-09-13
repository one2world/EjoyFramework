//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 空实现后端（Null Object）。所有操作均为无副作用的空操作：
    /// 凡返回房间的回调一律回 null，列表回调回空列表，离开/完成回调直接触发。
    /// 作为 <see cref="IMatchmakingBackend"/> 的安全默认值，避免上层在未注入真实后端时出现空引用。
    /// </summary>
    public sealed class NullMatchmakingBackend : IMatchmakingBackend
    {
        // 复用同一个空数组实例，避免重复分配。
        private static readonly IReadOnlyList<Room> s_Empty = Array.Empty<Room>();

        /// <summary>
        /// 空操作：不创建房间，回调返回 null。
        /// </summary>
        public void CreateRoom(string name, int maxPlayers, string hostId, bool isPrivate, Action<Room> onResult)
        {
            onResult?.Invoke(null);
        }

        /// <summary>
        /// 空操作：不加入任何房间，回调返回 null。
        /// </summary>
        public void JoinRoom(string roomId, string playerId, Action<Room> onResult)
        {
            onResult?.Invoke(null);
        }

        /// <summary>
        /// 空操作：直接触发完成回调。
        /// </summary>
        public void LeaveRoom(string roomId, string playerId, Action onDone)
        {
            onDone?.Invoke();
        }

        /// <summary>
        /// 空操作：返回空房间列表。
        /// </summary>
        public void ListRooms(Action<IReadOnlyList<Room>> onResult)
        {
            onResult?.Invoke(s_Empty);
        }

        /// <summary>
        /// 空操作：不匹配也不创建，回调返回 null。
        /// </summary>
        public void QuickMatch(string playerId, int maxPlayers, Action<Room> onResult)
        {
            onResult?.Invoke(null);
        }
    }
}
