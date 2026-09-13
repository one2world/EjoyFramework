//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Social
{
    /// <summary>
    /// 队伍中单个固定槽位的快照：槽位下标、占用者与准备状态。
    /// 不可变引用类型，作为 <see cref="Party.Slots"/> 的只读视图元素对外暴露，避免外部直接改写队伍内部状态。
    /// </summary>
    public sealed class PartySlot
    {
        private readonly int m_Index;
        private readonly string m_PlayerId;
        private readonly bool m_IsReady;

        /// <summary>
        /// 构造一个槽位快照。
        /// </summary>
        /// <param name="index">槽位下标（0 起）。</param>
        /// <param name="playerId">占用者玩家标识；为 null 表示空槽。</param>
        /// <param name="isReady">占用者是否已准备；空槽恒为 false。</param>
        public PartySlot(int index, string playerId, bool isReady)
        {
            m_Index = index;
            m_PlayerId = playerId;
            m_IsReady = isReady;
        }

        /// <summary>槽位下标（0 起）。</summary>
        public int Index
        {
            get { return m_Index; }
        }

        /// <summary>占用者玩家标识；为 null 表示空槽。</summary>
        public string PlayerId
        {
            get { return m_PlayerId; }
        }

        /// <summary>占用者是否已准备；空槽恒为 false。</summary>
        public bool IsReady
        {
            get { return m_IsReady; }
        }

        /// <summary>该槽位是否为空。</summary>
        public bool IsEmpty
        {
            get { return m_PlayerId == null; }
        }
    }
}
