//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Social
{
    /// <summary>
    /// 队伍（组队）本地花名册模型：维护固定数量槽位、成员准备状态与队长归属。
    /// 纯逻辑、与引擎无关，服务器同步由游戏层负责，本类只提供可复用的本地规则。
    /// 单线程使用，非线程安全。
    ///
    /// 边界规则约定（已在各方法 XML 中分别说明）：
    /// - 队长自动指派：当队伍原本无队长时，首位加入者自动成为队长。
    /// - 队长离队交接：队长 <see cref="Leave"/> 时，按槽位顺序把队长交给下一名在队成员；若无其他成员则解散，<see cref="LeaderId"/> 置空。
    /// - 仅队长可 <see cref="Kick"/>，且不能借 <see cref="Kick"/> 踢自己（退出请用 <see cref="Leave"/>）。
    /// - 任何改变都仅在产生实际变化时触发一次 <see cref="OnChanged"/>。
    /// </summary>
    public sealed class Party
    {
        private readonly string m_Id;
        private readonly int m_MaxSize;

        // 槽位的内部可变表示：playerId 为 null 表示空槽。
        private readonly string[] m_SlotPlayers;
        private readonly bool[] m_SlotReady;

        private string m_LeaderId;

        /// <summary>
        /// 队伍发生实际变化时触发一次（每个产生变更的调用至多触发一次）。
        /// </summary>
        public event Action<Party> OnChanged;

        /// <summary>
        /// 构造队伍。若给定的 <paramref name="leaderId"/> 非空，则将其填入首个槽位并设为队长。
        /// </summary>
        /// <param name="id">队伍唯一标识。</param>
        /// <param name="maxSize">最大人数（槽位数）；小于等于 0 视为 0。</param>
        /// <param name="leaderId">可选的初始队长玩家标识。</param>
        public Party(string id, int maxSize, string leaderId = null)
        {
            m_Id = id;
            m_MaxSize = maxSize < 0 ? 0 : maxSize;
            m_SlotPlayers = new string[m_MaxSize];
            m_SlotReady = new bool[m_MaxSize];

            if (!string.IsNullOrEmpty(leaderId) && m_MaxSize > 0)
            {
                m_SlotPlayers[0] = leaderId;
                m_LeaderId = leaderId;
            }
        }

        /// <summary>队伍唯一标识。</summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>最大人数（槽位数）。</summary>
        public int MaxSize
        {
            get { return m_MaxSize; }
        }

        /// <summary>当前队长玩家标识；队伍为空时为 null。</summary>
        public string LeaderId
        {
            get { return m_LeaderId; }
        }

        /// <summary>当前在队人数。</summary>
        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < m_SlotPlayers.Length; i++)
                {
                    if (m_SlotPlayers[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>队伍是否已满。</summary>
        public bool IsFull
        {
            get { return Count >= m_MaxSize; }
        }

        /// <summary>
        /// 是否所有已占用槽位都已准备；当队伍为空（无任何占用槽位）时为 false。
        /// </summary>
        public bool AllReady
        {
            get
            {
                bool any = false;
                for (int i = 0; i < m_SlotPlayers.Length; i++)
                {
                    if (m_SlotPlayers[i] == null)
                    {
                        continue;
                    }

                    any = true;
                    if (!m_SlotReady[i])
                    {
                        return false;
                    }
                }

                return any;
            }
        }

        /// <summary>
        /// 槽位的只读快照视图（长度恒为 <see cref="MaxSize"/>，含空槽）。每次访问构造一组不可变 <see cref="PartySlot"/>。
        /// </summary>
        public IReadOnlyList<PartySlot> Slots
        {
            get
            {
                PartySlot[] view = new PartySlot[m_SlotPlayers.Length];
                for (int i = 0; i < m_SlotPlayers.Length; i++)
                {
                    string player = m_SlotPlayers[i];
                    view[i] = new PartySlot(i, player, player != null && m_SlotReady[i]);
                }

                return view;
            }
        }

        /// <summary>
        /// 加入队伍。队伍已满或玩家已在队中时返回 false。
        /// 若加入前队伍尚无队长，则该加入者自动成为队长。新加入者初始为未准备。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成功加入返回 true；满员、重复或参数非法返回 false。</returns>
        public bool Join(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            if (Contains(playerId))
            {
                return false;
            }

            int slot = FindFirstEmptySlot();
            if (slot < 0)
            {
                return false;
            }

            m_SlotPlayers[slot] = playerId;
            m_SlotReady[slot] = false;

            // 无队长时，首位加入者自动成为队长。
            if (m_LeaderId == null)
            {
                m_LeaderId = playerId;
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 离开队伍。若离开者是队长，则按槽位顺序把队长交给下一名在队成员；
        /// 若无其他成员，队伍解散，<see cref="LeaderId"/> 置为 null。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成功离开返回 true；玩家不在队中或参数非法返回 false。</returns>
        public bool Leave(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            int slot = IndexOf(playerId);
            if (slot < 0)
            {
                return false;
            }

            bool wasLeader = string.Equals(m_LeaderId, playerId, StringComparison.Ordinal);

            m_SlotPlayers[slot] = null;
            m_SlotReady[slot] = false;

            if (wasLeader)
            {
                // 队长交接：按槽位顺序找下一名在队成员；找不到则解散。
                m_LeaderId = FindFirstOccupant();
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 由队长将目标踢出队伍。仅当前队长可执行；不能借本方法踢自己（退出请用 <see cref="Leave"/>）。
        /// 目标不在队中、操作者非队长或操作者踢自己时返回 false。
        /// </summary>
        /// <param name="byPlayerId">执行踢人的玩家标识，须为当前队长。</param>
        /// <param name="targetId">被踢出的玩家标识。</param>
        /// <returns>成功踢出返回 true；否则返回 false。</returns>
        public bool Kick(string byPlayerId, string targetId)
        {
            if (string.IsNullOrEmpty(byPlayerId) || string.IsNullOrEmpty(targetId))
            {
                return false;
            }

            // 仅队长可踢人。
            if (!string.Equals(m_LeaderId, byPlayerId, StringComparison.Ordinal))
            {
                return false;
            }

            // 不允许借 Kick 踢自己。
            if (string.Equals(byPlayerId, targetId, StringComparison.Ordinal))
            {
                return false;
            }

            int slot = IndexOf(targetId);
            if (slot < 0)
            {
                return false;
            }

            m_SlotPlayers[slot] = null;
            m_SlotReady[slot] = false;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 设置某成员的准备状态。状态未发生变化时不触发 <see cref="OnChanged"/> 并返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="ready">目标准备状态。</param>
        /// <returns>成功改变状态返回 true；玩家不在队中或状态未变返回 false。</returns>
        public bool SetReady(string playerId, bool ready)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            int slot = IndexOf(playerId);
            if (slot < 0)
            {
                return false;
            }

            if (m_SlotReady[slot] == ready)
            {
                return false;
            }

            m_SlotReady[slot] = ready;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 转让队长。目标须在队中；目标已是队长时视为无变化并返回 false。
        /// </summary>
        /// <param name="toPlayerId">新队长玩家标识。</param>
        /// <returns>成功转让返回 true；目标不在队中或已是队长返回 false。</returns>
        public bool TransferLeader(string toPlayerId)
        {
            if (string.IsNullOrEmpty(toPlayerId))
            {
                return false;
            }

            if (!Contains(toPlayerId))
            {
                return false;
            }

            if (string.Equals(m_LeaderId, toPlayerId, StringComparison.Ordinal))
            {
                return false;
            }

            m_LeaderId = toPlayerId;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 玩家是否在队中。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>在队返回 true。</returns>
        public bool Contains(string playerId)
        {
            return IndexOf(playerId) >= 0;
        }

        private int IndexOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return -1;
            }

            for (int i = 0; i < m_SlotPlayers.Length; i++)
            {
                if (string.Equals(m_SlotPlayers[i], playerId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindFirstEmptySlot()
        {
            for (int i = 0; i < m_SlotPlayers.Length; i++)
            {
                if (m_SlotPlayers[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private string FindFirstOccupant()
        {
            for (int i = 0; i < m_SlotPlayers.Length; i++)
            {
                if (m_SlotPlayers[i] != null)
                {
                    return m_SlotPlayers[i];
                }
            }

            return null;
        }

        private void RaiseChanged()
        {
            Action<Party> handler = OnChanged;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
