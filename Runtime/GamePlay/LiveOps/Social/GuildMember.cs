//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Social
{
    /// <summary>
    /// 公会成员档案。<see cref="Role"/> 与 <see cref="Contribution"/> 由所属 <see cref="Guild"/> 统一管理，
    /// 外部不可直接改写（职位由 <see cref="Guild"/> 的内部 setter 维护，贡献由 <see cref="Guild.AddContribution"/> 维护），
    /// 以保证“全公会唯一会长”等不变量始终成立。其余字段构造后只读。
    /// 单线程使用，非线程安全。
    /// </summary>
    public sealed class GuildMember
    {
        private readonly string m_PlayerId;
        private readonly string m_Name;
        private readonly long m_JoinedEpochMs;

        private GuildRole m_Role;
        private int m_Contribution;

        /// <summary>
        /// 构造公会成员档案。
        /// </summary>
        /// <param name="playerId">玩家唯一标识，公会内主键。</param>
        /// <param name="name">显示名称。</param>
        /// <param name="role">初始职位，默认 <see cref="GuildRole.Member"/>。</param>
        /// <param name="joinedEpochMs">加入时间（Unix 毫秒时间戳），默认 0。</param>
        /// <param name="contribution">初始贡献值，默认 0。</param>
        public GuildMember(string playerId, string name, GuildRole role = GuildRole.Member, long joinedEpochMs = 0, int contribution = 0)
        {
            m_PlayerId = playerId;
            m_Name = name;
            m_Role = role;
            m_JoinedEpochMs = joinedEpochMs;
            m_Contribution = contribution;
        }

        /// <summary>
        /// 玩家唯一标识，构造后只读。
        /// </summary>
        public string PlayerId
        {
            get { return m_PlayerId; }
        }

        /// <summary>
        /// 显示名称，构造后只读。
        /// </summary>
        public string Name
        {
            get { return m_Name; }
        }

        /// <summary>
        /// 当前职位。仅由所属 <see cref="Guild"/> 通过内部 setter 调整，外部只读。
        /// </summary>
        public GuildRole Role
        {
            get { return m_Role; }
            internal set { m_Role = value; }
        }

        /// <summary>
        /// 加入时间（Unix 毫秒时间戳），构造后只读。
        /// </summary>
        public long JoinedEpochMs
        {
            get { return m_JoinedEpochMs; }
        }

        /// <summary>
        /// 贡献值。仅由 <see cref="Guild.AddContribution"/> 通过内部 setter 调整，外部只读。
        /// </summary>
        public int Contribution
        {
            get { return m_Contribution; }
            internal set { m_Contribution = value; }
        }
    }
}
