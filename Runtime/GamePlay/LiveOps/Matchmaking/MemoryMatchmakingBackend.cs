//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 进程内内存后端。把所有房间保存在本地字典中，回调全部同步触发，因此行为完全确定、便于单元测试，
    /// 也可直接用于单机/本地多客户端场景。房间 Id 由内部自增计数生成，保证唯一与稳定。
    /// 不涉及任何线程或网络；调用方在同一线程内即可观察到全部结果。
    /// </summary>
    public sealed class MemoryMatchmakingBackend : IMatchmakingBackend
    {
        // roomId -> Room；保持插入顺序，使 ListRooms/QuickMatch 的选择具有确定性。
        private readonly Dictionary<string, Room> m_Rooms;

        // 维护稳定的房间顺序，便于确定性匹配（Dictionary 枚举顺序不作保证）。
        private readonly List<string> m_Order;

        private int m_NextRoomId;

        /// <summary>
        /// 构造一个空的内存后端。
        /// </summary>
        public MemoryMatchmakingBackend()
        {
            m_Rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
            m_Order = new List<string>();
            m_NextRoomId = 1;
        }

        /// <summary>
        /// 当前后端持有的所有房间（按创建顺序）。只读视图，含已关闭房间，主要用于测试断言。
        /// </summary>
        public IReadOnlyList<Room> AllRooms
        {
            get
            {
                List<Room> all = new List<Room>(m_Order.Count);
                for (int i = 0; i < m_Order.Count; i++)
                {
                    all.Add(m_Rooms[m_Order[i]]);
                }

                return all;
            }
        }

        /// <summary>
        /// 创建房间并令房主立即加入；同步回调返回新房间。
        /// </summary>
        public void CreateRoom(string name, int maxPlayers, string hostId, bool isPrivate, Action<Room> onResult)
        {
            string roomId = AllocateRoomId();
            Room room = new Room(roomId, name, maxPlayers, hostId, isPrivate);

            m_Rooms[roomId] = room;
            m_Order.Add(roomId);

            onResult?.Invoke(room);
        }

        /// <summary>
        /// 加入指定房间；成功同步回调对应房间，房间缺失或不可加入时回调 null。
        /// </summary>
        public void JoinRoom(string roomId, string playerId, Action<Room> onResult)
        {
            if (string.IsNullOrEmpty(roomId) || !m_Rooms.TryGetValue(roomId, out Room room))
            {
                onResult?.Invoke(null);
                return;
            }

            // 已在房内视为加入成功（幂等），否则尝试加入。
            bool joinable = room.Contains(playerId) || room.Join(playerId);
            onResult?.Invoke(joinable ? room : null);
        }

        /// <summary>
        /// 离开指定房间；无论是否实际改变状态，完成后同步触发回调。
        /// </summary>
        public void LeaveRoom(string roomId, string playerId, Action onDone)
        {
            if (!string.IsNullOrEmpty(roomId) && m_Rooms.TryGetValue(roomId, out Room room))
            {
                room.Leave(playerId);
            }

            onDone?.Invoke();
        }

        /// <summary>
        /// 列出公开、未满、且处于 <see cref="RoomState.Waiting"/> 的房间。
        /// </summary>
        public void ListRooms(Action<IReadOnlyList<Room>> onResult)
        {
            List<Room> result = new List<Room>();
            for (int i = 0; i < m_Order.Count; i++)
            {
                Room room = m_Rooms[m_Order[i]];
                if (IsListable(room))
                {
                    result.Add(room);
                }
            }

            onResult?.Invoke(result);
        }

        /// <summary>
        /// 快速匹配：按创建顺序加入首个可列出且容量匹配的房间；若无则创建一个新房间并由该玩家成为房主。
        /// </summary>
        public void QuickMatch(string playerId, int maxPlayers, Action<Room> onResult)
        {
            for (int i = 0; i < m_Order.Count; i++)
            {
                Room room = m_Rooms[m_Order[i]];
                if (!IsListable(room) || room.MaxPlayers != maxPlayers)
                {
                    continue;
                }

                if (room.Join(playerId))
                {
                    onResult?.Invoke(room);
                    return;
                }
            }

            // 无可用房间：创建一个并让该玩家成为房主。
            CreateRoom("QuickMatch", maxPlayers, playerId, false, onResult);
        }

        /// <summary>
        /// 判断房间是否应出现在公开列表/可被快速匹配：公开、未满、且等待中。
        /// </summary>
        private static bool IsListable(Room room)
        {
            return !room.IsPrivate && !room.IsFull && room.State == RoomState.Waiting;
        }

        /// <summary>
        /// 生成下一个稳定且唯一的房间 Id。
        /// </summary>
        private string AllocateRoomId()
        {
            string id = "room-" + m_NextRoomId.ToString();
            m_NextRoomId++;
            return id;
        }
    }
}
