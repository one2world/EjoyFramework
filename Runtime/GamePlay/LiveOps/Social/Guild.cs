//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Social
{
    /// <summary>
    /// 公会本地花名册模型：维护成员集合、职位层级与“全公会唯一会长”的不变量。
    /// 纯逻辑、与引擎无关，服务器同步由游戏层负责，本类只提供可复用的本地规则。
    /// 单线程使用，非线程安全。
    ///
    /// 边界规则约定（已在各方法 XML 中分别说明）：
    /// - 唯一会长不变量：任一时刻至多一名 <see cref="GuildRole.Leader"/>。
    /// - <see cref="SetRole"/> 不允许把任何人设为会长（会长更替必须走 <see cref="TransferLeadership"/>）。
    /// - <see cref="Promote"/> 至多升到 <see cref="GuildRole.ViceLeader"/>，不会自动产生第二个会长。
    /// - <see cref="Demote"/> 不允许直接降级会长。
    /// - <see cref="RemoveMember"/> 不允许移除会长，除非其为公会最后一名成员。
    /// </summary>
    public sealed class Guild
    {
        private readonly string m_Id;
        private readonly string m_Name;
        private readonly int m_Capacity;

        // 以 PlayerId 为主键的成员表，保持插入顺序以便稳定枚举。
        private readonly Dictionary<string, GuildMember> m_Members =
            new Dictionary<string, GuildMember>(StringComparer.Ordinal);
        private readonly List<GuildMember> m_Order = new List<GuildMember>();

        /// <summary>
        /// 花名册发生实际变化时触发一次（每个产生变更的调用至多触发一次）。
        /// </summary>
        public event Action<Guild> OnChanged;

        /// <summary>
        /// 构造公会。
        /// </summary>
        /// <param name="id">公会唯一标识。</param>
        /// <param name="name">公会名称。</param>
        /// <param name="capacity">成员容量上限；小于等于 0 视为 0（无成员可加入）。</param>
        public Guild(string id, string name, int capacity)
        {
            m_Id = id;
            m_Name = name;
            m_Capacity = capacity < 0 ? 0 : capacity;
        }

        /// <summary>公会唯一标识。</summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>公会名称。</summary>
        public string Name
        {
            get { return m_Name; }
        }

        /// <summary>成员容量上限。</summary>
        public int Capacity
        {
            get { return m_Capacity; }
        }

        /// <summary>当前成员数量。</summary>
        public int Count
        {
            get { return m_Order.Count; }
        }

        /// <summary>成员是否已达容量上限。</summary>
        public bool IsFull
        {
            get { return m_Order.Count >= m_Capacity; }
        }

        /// <summary>
        /// 当前会长；没有会长时为 null。
        /// </summary>
        public GuildMember Leader
        {
            get
            {
                for (int i = 0; i < m_Order.Count; i++)
                {
                    if (m_Order[i].Role == GuildRole.Leader)
                    {
                        return m_Order[i];
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// 成员只读枚举视图（按加入顺序）。
        /// </summary>
        public IEnumerable<GuildMember> Members
        {
            get { return m_Order; }
        }

        /// <summary>
        /// 加入一名成员。当公会已满或已存在同 <see cref="GuildMember.PlayerId"/> 时返回 false。
        /// 若新成员声明为 <see cref="GuildRole.Leader"/> 但公会已有会长，则将其降为 <see cref="GuildRole.Member"/> 以维持唯一会长不变量。
        /// </summary>
        /// <param name="member">待加入的成员档案。</param>
        /// <returns>成功加入返回 true；满员、重复或参数非法返回 false。</returns>
        public bool AddMember(GuildMember member)
        {
            if (member == null || string.IsNullOrEmpty(member.PlayerId))
            {
                return false;
            }

            if (IsFull || m_Members.ContainsKey(member.PlayerId))
            {
                return false;
            }

            // 维持唯一会长：已有会长时，新来的“会长”降为普通成员。
            if (member.Role == GuildRole.Leader && Leader != null)
            {
                member.Role = GuildRole.Member;
            }

            m_Members.Add(member.PlayerId, member);
            m_Order.Add(member);
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 移除指定成员。会长受保护：仅当其为公会最后一名成员时才可被移除，否则返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成功移除返回 true；成员不存在或为受保护会长返回 false。</returns>
        public bool RemoveMember(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return false;
            }

            GuildMember member;
            if (!m_Members.TryGetValue(playerId, out member))
            {
                return false;
            }

            // 会长仅在“最后一名成员”时可被移除。
            if (member.Role == GuildRole.Leader && m_Order.Count > 1)
            {
                return false;
            }

            m_Members.Remove(playerId);
            m_Order.Remove(member);
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 按标识获取成员；不存在时返回 null。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成员档案或 null。</returns>
        public GuildMember GetMember(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            GuildMember member;
            return m_Members.TryGetValue(playerId, out member) ? member : null;
        }

        /// <summary>
        /// 直接设置成员职位。为维持唯一会长不变量，本方法<b>不允许</b>把任何人设为 <see cref="GuildRole.Leader"/>
        /// （请改用 <see cref="TransferLeadership"/>），也不允许把现任会长改成其它职位（应通过 <see cref="TransferLeadership"/> 转让后自动降级）。
        /// 职位未发生变化时不触发 <see cref="OnChanged"/> 并返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="role">目标职位（不可为 <see cref="GuildRole.Leader"/>）。</param>
        /// <returns>成功改变职位返回 true；否则返回 false。</returns>
        public bool SetRole(string playerId, GuildRole role)
        {
            if (role == GuildRole.Leader)
            {
                return false;
            }

            GuildMember member = GetMember(playerId);
            if (member == null)
            {
                return false;
            }

            // 现任会长不能被直接降级（会长更替必须经 TransferLeadership）。
            if (member.Role == GuildRole.Leader)
            {
                return false;
            }

            if (member.Role == role)
            {
                return false;
            }

            member.Role = role;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 将成员职位向上提升一级：Member → Elder → ViceLeader。
        /// 不会自动晋升为 <see cref="GuildRole.Leader"/>（避免产生第二个会长，会长更替须用 <see cref="TransferLeadership"/>）。
        /// 已处于 <see cref="GuildRole.ViceLeader"/> 或更高时不再提升并返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成功提升返回 true；已封顶、为会长或不存在返回 false。</returns>
        public bool Promote(string playerId)
        {
            GuildMember member = GetMember(playerId);
            if (member == null)
            {
                return false;
            }

            // 上限为副会长：再往上的“会长”须走转让流程。
            if (member.Role >= GuildRole.ViceLeader)
            {
                return false;
            }

            member.Role = member.Role + 1;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 将成员职位向下降低一级：ViceLeader → Elder → Member。
        /// 不允许直接降级会长（会长须先经 <see cref="TransferLeadership"/> 转让）。
        /// 已处于 <see cref="GuildRole.Member"/> 时不再降低并返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成功降级返回 true；已到最低、为会长或不存在返回 false。</returns>
        public bool Demote(string playerId)
        {
            GuildMember member = GetMember(playerId);
            if (member == null)
            {
                return false;
            }

            // 会长不可直接降级。
            if (member.Role == GuildRole.Leader)
            {
                return false;
            }

            if (member.Role <= GuildRole.Member)
            {
                return false;
            }

            member.Role = member.Role - 1;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 转让会长。目标成员升为 <see cref="GuildRole.Leader"/>，原会长（若存在）降为 <see cref="GuildRole.Member"/>，
        /// 全程维持唯一会长不变量。目标已是会长时视为无变化并返回 false。
        /// 若公会当前尚无会长，则等价于直接将目标设为会长。
        /// </summary>
        /// <param name="toPlayerId">新会长的玩家标识。</param>
        /// <returns>成功转让返回 true；目标不存在或已是会长返回 false。</returns>
        public bool TransferLeadership(string toPlayerId)
        {
            GuildMember next = GetMember(toPlayerId);
            if (next == null)
            {
                return false;
            }

            if (next.Role == GuildRole.Leader)
            {
                return false;
            }

            GuildMember current = Leader;
            if (current != null)
            {
                // 原会长降为普通成员（约定为 Member）。
                current.Role = GuildRole.Member;
            }

            next.Role = GuildRole.Leader;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 为指定成员增加贡献值（amount 可为负以扣减）。amount 为 0 视为无操作并返回 false。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="amount">增量，可正可负。</param>
        /// <returns>成功调整返回 true；成员不存在或增量为 0 返回 false。</returns>
        public bool AddContribution(string playerId, int amount)
        {
            if (amount == 0)
            {
                return false;
            }

            GuildMember member = GetMember(playerId);
            if (member == null)
            {
                return false;
            }

            member.Contribution = member.Contribution + amount;
            RaiseChanged();
            return true;
        }

        private void RaiseChanged()
        {
            Action<Guild> handler = OnChanged;
            if (handler != null)
            {
                handler(this);
            }
        }
    }
}
