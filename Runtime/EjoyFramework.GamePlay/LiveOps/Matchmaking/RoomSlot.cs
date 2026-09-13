//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 房间内的单个座位。纯逻辑、与引擎无关。
    /// 一个座位要么为空（<see cref="PlayerId"/> 为 null），要么被某个玩家占用并带有就绪标记。
    /// 该类型由 <see cref="Room"/> 内部维护与替换：任何对座位的修改都会生成新的快照实例，
    /// 对外只读，避免外部直接改动房间内部状态。
    /// </summary>
    public sealed class RoomSlot
    {
        private readonly int m_Index;
        private readonly string m_PlayerId;
        private readonly bool m_IsReady;

        /// <summary>
        /// 构造一个座位快照。
        /// </summary>
        /// <param name="index">座位下标（0 起，对应 <see cref="Room.MaxPlayers"/> 范围内）。</param>
        /// <param name="playerId">占位玩家的唯一标识；null 表示空位。</param>
        /// <param name="isReady">占位玩家是否已就绪；空位恒为 false。</param>
        public RoomSlot(int index, string playerId, bool isReady)
        {
            m_Index = index;
            m_PlayerId = playerId;
            m_IsReady = isReady;
        }

        /// <summary>
        /// 座位下标（0 起）。
        /// </summary>
        public int Index
        {
            get { return m_Index; }
        }

        /// <summary>
        /// 占位玩家的唯一标识；null 表示该座位为空。
        /// </summary>
        public string PlayerId
        {
            get { return m_PlayerId; }
        }

        /// <summary>
        /// 占位玩家是否已就绪；空位恒为 false。
        /// </summary>
        public bool IsReady
        {
            get { return m_IsReady; }
        }

        /// <summary>
        /// 该座位是否为空（即 <see cref="PlayerId"/> 为 null 或空串）。
        /// </summary>
        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(m_PlayerId); }
        }
    }
}
