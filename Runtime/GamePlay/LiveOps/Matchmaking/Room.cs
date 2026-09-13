//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Matchmaking
{
    /// <summary>
    /// 房间（大厅）本地数据模型。纯逻辑、与引擎无关，可独立单元测试。
    /// 以固定数量的座位（<see cref="MaxPlayers"/>）描述房间占用情况，负责加入/离开/踢人/就绪等规则，
    /// 并在房主缺位时自动迁移房主（首个加入者自动成为房主）。
    /// 该类型只决定“谁在房间里、谁是房主”，不负责任何字节传输——netcode 层基于此结果建立连接。
    /// 任何造成实际变化的修改都会触发一次 <see cref="OnChanged"/>。
    /// </summary>
    public sealed class Room
    {
        private readonly string m_Id;
        private readonly string m_Name;
        private readonly int m_MaxPlayers;
        private readonly bool m_IsPrivate;

        // 座位快照数组；每次修改都替换其中的元素为新的 RoomSlot 实例（座位本身不可变）。
        private readonly RoomSlot[] m_Slots;

        // 以只读视图对外暴露的座位列表，避免外部改动内部数组。
        private readonly IReadOnlyList<RoomSlot> m_SlotsView;

        private RoomState m_State;
        private string m_HostId;

        /// <summary>
        /// 构造一个房间。座位全部初始化为空，状态为 <see cref="RoomState.Waiting"/>。
        /// 若提供了 <paramref name="hostId"/>，该玩家会被立即加入并成为房主。
        /// </summary>
        /// <param name="id">房间唯一标识。</param>
        /// <param name="name">房间显示名。</param>
        /// <param name="maxPlayers">最大玩家数；小于 1 时按 1 处理。</param>
        /// <param name="hostId">初始房主玩家标识；为 null 时表示创建空房间，由首个加入者成为房主。</param>
        /// <param name="isPrivate">是否为私有房间（私有房间不会出现在公开列表中）。</param>
        public Room(string id, string name, int maxPlayers, string hostId = null, bool isPrivate = false)
        {
            m_Id = id;
            m_Name = name;
            m_MaxPlayers = maxPlayers < 1 ? 1 : maxPlayers;
            m_IsPrivate = isPrivate;
            m_State = RoomState.Waiting;
            m_HostId = null;

            m_Slots = new RoomSlot[m_MaxPlayers];
            for (int i = 0; i < m_Slots.Length; i++)
            {
                m_Slots[i] = new RoomSlot(i, null, false);
            }

            m_SlotsView = Array.AsReadOnly(m_Slots);

            // 构造期间加入房主：不触发 OnChanged（此时尚无订阅者，且属于初始化语义）。
            if (!string.IsNullOrEmpty(hostId))
            {
                int index = FindFreeSlot();
                if (index >= 0)
                {
                    m_Slots[index] = new RoomSlot(index, hostId, false);
                    m_HostId = hostId;
                }
            }
        }

        /// <summary>
        /// 房间唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 房间显示名。
        /// </summary>
        public string Name
        {
            get { return m_Name; }
        }

        /// <summary>
        /// 最大玩家数（座位总数）。
        /// </summary>
        public int MaxPlayers
        {
            get { return m_MaxPlayers; }
        }

        /// <summary>
        /// 房间当前状态；通过 <see cref="SetState"/> 由上层（匹配逻辑）受控修改。
        /// </summary>
        public RoomState State
        {
            get { return m_State; }
        }

        /// <summary>
        /// 当前房主玩家标识；房间为空时为 null。
        /// </summary>
        public string HostId
        {
            get { return m_HostId; }
        }

        /// <summary>
        /// 是否为私有房间。私有房间不会出现在公开房间列表中。
        /// </summary>
        public bool IsPrivate
        {
            get { return m_IsPrivate; }
        }

        /// <summary>
        /// 座位快照（只读视图，按下标升序）。外部不可修改。
        /// </summary>
        public IReadOnlyList<RoomSlot> Slots
        {
            get { return m_SlotsView; }
        }

        /// <summary>
        /// 当前在房人数（已占用座位数）。读路径零分配。
        /// </summary>
        public int PlayerCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_Slots.Length; i++)
                {
                    if (!m_Slots[i].IsEmpty)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 房间是否已满（所有座位均被占用）。
        /// </summary>
        public bool IsFull
        {
            get { return PlayerCount >= m_MaxPlayers; }
        }

        /// <summary>
        /// 是否所有占位玩家都已就绪，且房间内至少有一名玩家。
        /// </summary>
        public bool AllReady
        {
            get
            {
                int occupied = 0;
                for (int i = 0; i < m_Slots.Length; i++)
                {
                    RoomSlot slot = m_Slots[i];
                    if (slot.IsEmpty)
                    {
                        continue;
                    }

                    occupied++;
                    if (!slot.IsReady)
                    {
                        return false;
                    }
                }

                return occupied > 0;
            }
        }

        /// <summary>
        /// 判断某玩家是否在房间内。读路径零分配。
        /// </summary>
        /// <param name="playerId">玩家唯一标识。</param>
        /// <returns>在房间内返回 true，否则 false。</returns>
        public bool Contains(string playerId)
        {
            return IndexOfPlayer(playerId) >= 0;
        }

        /// <summary>
        /// 玩家加入房间。占用第一个空位；若房间此前为空（无房主），该玩家自动成为房主。
        /// 当房间已满、玩家已在房内、或房间不可加入（状态非 <see cref="RoomState.Waiting"/>）时返回 false。
        /// 加入成功后触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="playerId">玩家唯一标识；为 null 或空串时直接返回 false。</param>
        /// <returns>加入成功返回 true，否则 false。</returns>
        public bool Join(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            if (m_State != RoomState.Waiting)
            {
                return false;
            }

            if (Contains(playerId))
            {
                return false;
            }

            int index = FindFreeSlot();
            if (index < 0)
            {
                return false;
            }

            m_Slots[index] = new RoomSlot(index, playerId, false);

            // 房间此前无房主 => 首个加入者成为房主。
            if (string.IsNullOrEmpty(m_HostId))
            {
                m_HostId = playerId;
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 玩家离开房间。若离开者是房主，则将房主迁移给下一个占位玩家；
        /// 若离开后房间为空，则状态置为 <see cref="RoomState.Closed"/> 且房主清空。
        /// 当玩家不在房内时返回 false。离开成功后触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="playerId">玩家唯一标识。</param>
        /// <returns>离开成功返回 true，否则 false。</returns>
        public bool Leave(string playerId)
        {
            int index = IndexOfPlayer(playerId);
            if (index < 0)
            {
                return false;
            }

            bool wasHost = string.Equals(m_HostId, playerId, StringComparison.Ordinal);

            m_Slots[index] = new RoomSlot(index, null, false);

            if (PlayerCount == 0)
            {
                // 房间已空：清空房主并关闭房间。
                m_HostId = null;
                m_State = RoomState.Closed;
            }
            else if (wasHost)
            {
                // 房主离开但房间非空：迁移房主给下一个占位玩家。
                m_HostId = FirstOccupantId();
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 房主踢人。仅房主可执行，且不能踢自己。目标玩家必须在房内。
        /// 被踢者按 <see cref="Leave"/> 的语义离场（包括可能触发的房主迁移/关闭）。
        /// 成功后触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="byHostId">发起踢人的玩家标识，必须等于当前房主。</param>
        /// <param name="targetId">被踢玩家标识。</param>
        /// <returns>踢人成功返回 true，否则 false。</returns>
        public bool Kick(string byHostId, string targetId)
        {
            if (string.IsNullOrEmpty(byHostId) || string.IsNullOrEmpty(targetId))
            {
                return false;
            }

            // 仅房主可踢人。
            if (!string.Equals(byHostId, m_HostId, StringComparison.Ordinal))
            {
                return false;
            }

            // 不能踢自己。
            if (string.Equals(byHostId, targetId, StringComparison.Ordinal))
            {
                return false;
            }

            // 目标必须在房内（Leave 内部已校验，并负责触发 OnChanged）。
            return Leave(targetId);
        }

        /// <summary>
        /// 设置某玩家的就绪状态。玩家必须在房内，且新状态与当前不同才视为有效修改。
        /// 仅在状态真正改变时触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="playerId">玩家唯一标识。</param>
        /// <param name="ready">目标就绪状态。</param>
        /// <returns>状态发生改变返回 true；玩家不在房内或状态未变化返回 false。</returns>
        public bool SetReady(string playerId, bool ready)
        {
            int index = IndexOfPlayer(playerId);
            if (index < 0)
            {
                return false;
            }

            if (m_Slots[index].IsReady == ready)
            {
                return false;
            }

            m_Slots[index] = new RoomSlot(index, m_Slots[index].PlayerId, ready);
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 强制将房主迁移给“下一个”占位玩家（按座位顺序选取首个非当前房主的占位者；
        /// 若房主当前缺位，则选取首个占位者）。无可迁移对象时返回 false。
        /// 成功后触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <returns>成功迁移返回 true；房间为空或无其他可选玩家时返回 false。</returns>
        public bool MigrateHost()
        {
            string next = NextHostCandidate();
            if (string.IsNullOrEmpty(next))
            {
                return false;
            }

            m_HostId = next;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 由上层（匹配逻辑）受控设置房间状态。仅在状态真正改变时触发 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="state">目标状态。</param>
        public void SetState(RoomState state)
        {
            if (m_State == state)
            {
                return;
            }

            m_State = state;
            RaiseChanged();
        }

        /// <summary>
        /// 返回某玩家所在座位下标；不在房内返回 -1。读路径零分配。
        /// </summary>
        private int IndexOfPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return -1;
            }

            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (string.Equals(m_Slots[i].PlayerId, playerId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 返回首个空位下标；房间已满返回 -1。
        /// </summary>
        private int FindFreeSlot()
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i].IsEmpty)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 返回按座位顺序的首个占位玩家标识；房间为空返回 null。
        /// </summary>
        private string FirstOccupantId()
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (!m_Slots[i].IsEmpty)
                {
                    return m_Slots[i].PlayerId;
                }
            }

            return null;
        }

        /// <summary>
        /// 选取房主迁移目标：优先按座位顺序选取首个“非当前房主”的占位玩家；
        /// 若不存在（如房主缺位），退化为首个占位玩家。无占位者返回 null。
        /// </summary>
        private string NextHostCandidate()
        {
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i].IsEmpty)
                {
                    continue;
                }

                string candidate = m_Slots[i].PlayerId;
                if (!string.Equals(candidate, m_HostId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            // 没有“其他”占位者；若房主当前缺位则退化为首个占位者。
            if (string.IsNullOrEmpty(m_HostId))
            {
                return FirstOccupantId();
            }

            return null;
        }

        /// <summary>
        /// 触发 <see cref="OnChanged"/>。先快照委托引用，避免“迭代中订阅者增删自身”的风险。
        /// </summary>
        private void RaiseChanged()
        {
            Action<Room> handler = OnChanged;
            handler?.Invoke(this);
        }

        /// <summary>
        /// 房间发生实际变化（加入/离开/踢人/就绪变更/房主迁移/状态变更）时触发。参数为本房间实例。
        /// </summary>
        public event Action<Room> OnChanged;
    }
}
