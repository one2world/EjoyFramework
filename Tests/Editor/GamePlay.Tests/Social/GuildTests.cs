//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Social;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Social
{
    /// <summary>
    /// 针对 <see cref="Guild"/> 的成员管理、职位层级、唯一会长不变量、贡献与事件语义的单元测试。
    /// </summary>
    [TestFixture]
    public class GuildTests
    {
        private static Guild MakeGuild(int capacity = 5)
        {
            return new Guild("g1", "TestGuild", capacity);
        }

        private static GuildMember Member(string id, GuildRole role = GuildRole.Member)
        {
            return new GuildMember(id, id.ToUpperInvariant(), role);
        }

        private static int CountLeaders(Guild guild)
        {
            int leaders = 0;
            foreach (GuildMember m in guild.Members)
            {
                if (m.Role == GuildRole.Leader)
                {
                    leaders++;
                }
            }

            return leaders;
        }

        [Test]
        public void AddMember_UpToCapacity_ThenFullAndRejected()
        {
            Guild guild = MakeGuild(2);

            Assert.IsTrue(guild.AddMember(Member("a")));
            Assert.IsFalse(guild.IsFull);
            Assert.IsTrue(guild.AddMember(Member("b")));
            Assert.IsTrue(guild.IsFull);
            Assert.AreEqual(2, guild.Count);

            // 已满，再加被拒。
            Assert.IsFalse(guild.AddMember(Member("c")));
            Assert.AreEqual(2, guild.Count);
        }

        [Test]
        public void AddMember_DuplicateId_Rejected()
        {
            Guild guild = MakeGuild();

            Assert.IsTrue(guild.AddMember(Member("a")));
            Assert.IsFalse(guild.AddMember(Member("a")));
            Assert.AreEqual(1, guild.Count);
        }

        [Test]
        public void AddMember_SecondLeader_DemotedToMember()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));

            // 已有会长时，新来的“会长”被降为普通成员，维持唯一会长。
            guild.AddMember(Member("imposter", GuildRole.Leader));

            Assert.AreEqual(GuildRole.Member, guild.GetMember("imposter").Role);
            Assert.AreEqual(1, CountLeaders(guild));
            Assert.AreEqual("leader", guild.Leader.PlayerId);
        }

        [Test]
        public void GetMember_ReturnsMemberOrNull()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));

            Assert.IsNotNull(guild.GetMember("a"));
            Assert.AreEqual("a", guild.GetMember("a").PlayerId);
            Assert.IsNull(guild.GetMember("missing"));
            Assert.IsNull(guild.GetMember(null));
        }

        [Test]
        public void Promote_MovesUpTier_StopsAtViceLeader()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));

            Assert.IsTrue(guild.Promote("a"));
            Assert.AreEqual(GuildRole.Elder, guild.GetMember("a").Role);

            Assert.IsTrue(guild.Promote("a"));
            Assert.AreEqual(GuildRole.ViceLeader, guild.GetMember("a").Role);

            // 上限为副会长，不会自动晋升为会长。
            Assert.IsFalse(guild.Promote("a"));
            Assert.AreEqual(GuildRole.ViceLeader, guild.GetMember("a").Role);
            Assert.AreEqual(0, CountLeaders(guild));
        }

        [Test]
        public void Demote_MovesDownTier_StopsAtMember()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a", GuildRole.ViceLeader));

            Assert.IsTrue(guild.Demote("a"));
            Assert.AreEqual(GuildRole.Elder, guild.GetMember("a").Role);

            Assert.IsTrue(guild.Demote("a"));
            Assert.AreEqual(GuildRole.Member, guild.GetMember("a").Role);

            // 已到最低，不再降。
            Assert.IsFalse(guild.Demote("a"));
            Assert.AreEqual(GuildRole.Member, guild.GetMember("a").Role);
        }

        [Test]
        public void Promote_Demote_MissingMember_ReturnsFalse()
        {
            Guild guild = MakeGuild();
            Assert.IsFalse(guild.Promote("ghost"));
            Assert.IsFalse(guild.Demote("ghost"));
        }

        [Test]
        public void Leader_CannotBeDemotedDirectly()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));

            Assert.IsFalse(guild.Demote("leader"));
            Assert.AreEqual(GuildRole.Leader, guild.GetMember("leader").Role);
        }

        [Test]
        public void SetRole_ChangesRole_ButRejectsLeaderTarget()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));

            Assert.IsTrue(guild.SetRole("a", GuildRole.ViceLeader));
            Assert.AreEqual(GuildRole.ViceLeader, guild.GetMember("a").Role);

            // 不允许用 SetRole 设为会长。
            Assert.IsFalse(guild.SetRole("a", GuildRole.Leader));
            Assert.AreEqual(GuildRole.ViceLeader, guild.GetMember("a").Role);
            Assert.AreEqual(0, CountLeaders(guild));
        }

        [Test]
        public void SetRole_OnExistingLeader_Rejected()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));

            // 现任会长不能被 SetRole 直接改职。
            Assert.IsFalse(guild.SetRole("leader", GuildRole.Member));
            Assert.AreEqual(GuildRole.Leader, guild.GetMember("leader").Role);
        }

        [Test]
        public void SetRole_NoChange_ReturnsFalse()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a", GuildRole.Elder));

            Assert.IsFalse(guild.SetRole("a", GuildRole.Elder));
        }

        [Test]
        public void TransferLeadership_MovesLeadership_OldLeaderDemoted_SingleLeader()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("old", GuildRole.Leader));
            guild.AddMember(Member("new", GuildRole.ViceLeader));

            Assert.IsTrue(guild.TransferLeadership("new"));

            Assert.AreEqual(GuildRole.Leader, guild.GetMember("new").Role);
            // 原会长降为普通成员。
            Assert.AreEqual(GuildRole.Member, guild.GetMember("old").Role);
            // 任一时刻仅一名会长。
            Assert.AreEqual(1, CountLeaders(guild));
            Assert.AreEqual("new", guild.Leader.PlayerId);
        }

        [Test]
        public void TransferLeadership_WhenNoLeader_PromotesTarget()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));

            Assert.IsNull(guild.Leader);
            Assert.IsTrue(guild.TransferLeadership("a"));
            Assert.AreEqual(GuildRole.Leader, guild.GetMember("a").Role);
            Assert.AreEqual(1, CountLeaders(guild));
        }

        [Test]
        public void TransferLeadership_ToCurrentLeaderOrMissing_ReturnsFalse()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));

            Assert.IsFalse(guild.TransferLeadership("leader"));
            Assert.IsFalse(guild.TransferLeadership("ghost"));
        }

        [Test]
        public void AddContribution_AdjustsValue_RejectsZeroAndMissing()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));

            Assert.IsTrue(guild.AddContribution("a", 50));
            Assert.AreEqual(50, guild.GetMember("a").Contribution);

            Assert.IsTrue(guild.AddContribution("a", -20));
            Assert.AreEqual(30, guild.GetMember("a").Contribution);

            // 增量 0 视为无操作。
            Assert.IsFalse(guild.AddContribution("a", 0));
            Assert.AreEqual(30, guild.GetMember("a").Contribution);

            // 成员不存在。
            Assert.IsFalse(guild.AddContribution("ghost", 10));
        }

        [Test]
        public void RemoveMember_NonLeader_Succeeds()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));
            guild.AddMember(Member("a"));

            Assert.IsTrue(guild.RemoveMember("a"));
            Assert.IsNull(guild.GetMember("a"));
            Assert.AreEqual(1, guild.Count);
        }

        [Test]
        public void RemoveMember_Leader_RejectedUnlessLastMember()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("leader", GuildRole.Leader));
            guild.AddMember(Member("a"));

            // 还有其他成员，会长受保护。
            Assert.IsFalse(guild.RemoveMember("leader"));
            Assert.AreEqual(2, guild.Count);

            // 移除其他成员后，会长成为最后一名，可被移除。
            guild.RemoveMember("a");
            Assert.IsTrue(guild.RemoveMember("leader"));
            Assert.AreEqual(0, guild.Count);
        }

        [Test]
        public void RemoveMember_Missing_ReturnsFalse()
        {
            Guild guild = MakeGuild();
            Assert.IsFalse(guild.RemoveMember("ghost"));
            Assert.IsFalse(guild.RemoveMember(null));
        }

        [Test]
        public void Members_EnumeratesInJoinOrder()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a"));
            guild.AddMember(Member("b"));
            guild.AddMember(Member("c"));

            List<string> ids = new List<string>();
            foreach (GuildMember m in guild.Members)
            {
                ids.Add(m.PlayerId);
            }

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ids);
        }

        [Test]
        public void OnChanged_FiresOnRealChange_NotOnNoOp()
        {
            Guild guild = MakeGuild();
            int changed = 0;
            guild.OnChanged += _ => changed++;

            guild.AddMember(Member("a"));
            Assert.AreEqual(1, changed);

            guild.Promote("a");
            Assert.AreEqual(2, changed);

            guild.AddContribution("a", 5);
            Assert.AreEqual(3, changed);

            guild.RemoveMember("a");
            Assert.AreEqual(4, changed);

            // 以下均为无操作，不应触发。
            guild.AddMember(null);
            guild.Promote("ghost");
            guild.Demote("ghost");
            guild.AddContribution("ghost", 1);
            guild.AddContribution("ghost", 0);
            guild.RemoveMember("ghost");
            guild.SetRole("ghost", GuildRole.Elder);
            Assert.AreEqual(4, changed);
        }

        [Test]
        public void OnChanged_NotFired_WhenSetRoleNoChange()
        {
            Guild guild = MakeGuild();
            guild.AddMember(Member("a", GuildRole.Elder));
            int changed = 0;
            guild.OnChanged += _ => changed++;

            Assert.IsFalse(guild.SetRole("a", GuildRole.Elder));
            Assert.AreEqual(0, changed);

            // 已封顶的 Promote 同样不触发。
            guild.SetRole("a", GuildRole.ViceLeader);
            changed = 0;
            Assert.IsFalse(guild.Promote("a"));
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void Capacity_NegativeTreatedAsZero()
        {
            Guild guild = new Guild("g", "n", -3);
            Assert.AreEqual(0, guild.Capacity);
            Assert.IsTrue(guild.IsFull);
            Assert.IsFalse(guild.AddMember(Member("a")));
        }
    }
}
