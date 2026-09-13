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
    /// 针对 <see cref="Party"/> 的加入/离开/踢人/准备/队长交接/槽位视图与事件语义的单元测试。
    /// </summary>
    [TestFixture]
    public class PartyTests
    {
        private static Party MakeParty(int maxSize = 4, string leaderId = null)
        {
            return new Party("p1", maxSize, leaderId);
        }

        [Test]
        public void Join_FirstJoiner_BecomesLeader()
        {
            Party party = MakeParty();

            Assert.IsNull(party.LeaderId);
            Assert.IsTrue(party.Join("a"));

            Assert.AreEqual("a", party.LeaderId);
            Assert.AreEqual(1, party.Count);
            Assert.IsTrue(party.Contains("a"));
        }

        [Test]
        public void Join_SubsequentJoiners_DoNotChangeLeader()
        {
            Party party = MakeParty();
            party.Join("a");
            party.Join("b");

            Assert.AreEqual("a", party.LeaderId);
            Assert.AreEqual(2, party.Count);
            Assert.IsTrue(party.Contains("b"));
        }

        [Test]
        public void Join_Full_Rejected()
        {
            Party party = MakeParty(2);
            Assert.IsTrue(party.Join("a"));
            Assert.IsTrue(party.Join("b"));
            Assert.IsTrue(party.IsFull);

            Assert.IsFalse(party.Join("c"));
            Assert.AreEqual(2, party.Count);
        }

        [Test]
        public void Join_Duplicate_Rejected()
        {
            Party party = MakeParty();
            Assert.IsTrue(party.Join("a"));
            Assert.IsFalse(party.Join("a"));
            Assert.AreEqual(1, party.Count);
        }

        [Test]
        public void Constructor_WithLeaderId_SeatsAndLeads()
        {
            Party party = MakeParty(4, "boss");

            Assert.AreEqual("boss", party.LeaderId);
            Assert.IsTrue(party.Contains("boss"));
            Assert.AreEqual(1, party.Count);
            Assert.AreEqual("boss", party.Slots[0].PlayerId);
        }

        [Test]
        public void ContainsAndCount_ReflectState()
        {
            Party party = MakeParty();
            Assert.IsFalse(party.Contains("a"));
            Assert.AreEqual(0, party.Count);

            party.Join("a");
            party.Join("b");
            Assert.IsTrue(party.Contains("a"));
            Assert.IsTrue(party.Contains("b"));
            Assert.IsFalse(party.Contains("c"));
            Assert.AreEqual(2, party.Count);
        }

        [Test]
        public void SetReady_And_AllReady()
        {
            Party party = MakeParty();
            party.Join("a");
            party.Join("b");

            Assert.IsFalse(party.AllReady);

            Assert.IsTrue(party.SetReady("a", true));
            Assert.IsFalse(party.AllReady);

            Assert.IsTrue(party.SetReady("b", true));
            Assert.IsTrue(party.AllReady);

            // 取消其一，整体不再就绪。
            Assert.IsTrue(party.SetReady("b", false));
            Assert.IsFalse(party.AllReady);
        }

        [Test]
        public void AllReady_EmptyParty_IsFalse()
        {
            Party party = MakeParty();
            Assert.IsFalse(party.AllReady);
        }

        [Test]
        public void SetReady_NoChangeOrMissing_ReturnsFalse()
        {
            Party party = MakeParty();
            party.Join("a");

            // 默认未准备，再设为 false 即无变化。
            Assert.IsFalse(party.SetReady("a", false));
            // 不在队中。
            Assert.IsFalse(party.SetReady("ghost", true));
        }

        [Test]
        public void Leave_NonLeader_KeepsLeader()
        {
            Party party = MakeParty();
            party.Join("a");
            party.Join("b");

            Assert.IsTrue(party.Leave("b"));
            Assert.AreEqual("a", party.LeaderId);
            Assert.AreEqual(1, party.Count);
            Assert.IsFalse(party.Contains("b"));
        }

        [Test]
        public void Leave_Leader_HandsOffToNextOccupant()
        {
            Party party = MakeParty();
            party.Join("a"); // leader at slot 0
            party.Join("b"); // slot 1
            party.Join("c"); // slot 2

            Assert.IsTrue(party.Leave("a"));
            // 按槽位顺序交给下一名在队成员 b。
            Assert.AreEqual("b", party.LeaderId);
            Assert.AreEqual(2, party.Count);
            Assert.IsFalse(party.Contains("a"));
        }

        [Test]
        public void Leave_LastMember_Disbands_LeaderNull()
        {
            Party party = MakeParty();
            party.Join("a");

            Assert.IsTrue(party.Leave("a"));
            Assert.IsNull(party.LeaderId);
            Assert.AreEqual(0, party.Count);
        }

        [Test]
        public void Leave_NotInParty_ReturnsFalse()
        {
            Party party = MakeParty();
            party.Join("a");
            Assert.IsFalse(party.Leave("ghost"));
        }

        [Test]
        public void Kick_OnlyByLeader()
        {
            Party party = MakeParty();
            party.Join("leader");
            party.Join("b");
            party.Join("c");

            // 非队长踢人被拒。
            Assert.IsFalse(party.Kick("b", "c"));
            Assert.IsTrue(party.Contains("c"));

            // 队长踢人成功。
            Assert.IsTrue(party.Kick("leader", "c"));
            Assert.IsFalse(party.Contains("c"));
        }

        [Test]
        public void Kick_CannotKickSelf()
        {
            Party party = MakeParty();
            party.Join("leader");
            party.Join("b");

            Assert.IsFalse(party.Kick("leader", "leader"));
            Assert.IsTrue(party.Contains("leader"));
            Assert.AreEqual("leader", party.LeaderId);
        }

        [Test]
        public void Kick_TargetMissing_ReturnsFalse()
        {
            Party party = MakeParty();
            party.Join("leader");

            Assert.IsFalse(party.Kick("leader", "ghost"));
        }

        [Test]
        public void TransferLeader_MovesLeadership()
        {
            Party party = MakeParty();
            party.Join("a");
            party.Join("b");

            Assert.IsTrue(party.TransferLeader("b"));
            Assert.AreEqual("b", party.LeaderId);

            // 目标已是队长或不在队中。
            Assert.IsFalse(party.TransferLeader("b"));
            Assert.IsFalse(party.TransferLeader("ghost"));
        }

        [Test]
        public void Slots_ReflectState()
        {
            Party party = MakeParty(3);
            party.Join("a");
            party.Join("b");
            party.SetReady("a", true);

            IReadOnlyList<PartySlot> slots = party.Slots;
            Assert.AreEqual(3, slots.Count);

            // 槽 0：a，已准备。
            Assert.AreEqual(0, slots[0].Index);
            Assert.AreEqual("a", slots[0].PlayerId);
            Assert.IsTrue(slots[0].IsReady);
            Assert.IsFalse(slots[0].IsEmpty);

            // 槽 1：b，未准备。
            Assert.AreEqual("b", slots[1].PlayerId);
            Assert.IsFalse(slots[1].IsReady);
            Assert.IsFalse(slots[1].IsEmpty);

            // 槽 2：空。
            Assert.IsNull(slots[2].PlayerId);
            Assert.IsTrue(slots[2].IsEmpty);
            Assert.IsFalse(slots[2].IsReady);
        }

        [Test]
        public void Slots_AfterLeave_SlotBecomesEmpty()
        {
            Party party = MakeParty(3);
            party.Join("a");
            party.Join("b");
            party.Leave("a");

            IReadOnlyList<PartySlot> slots = party.Slots;
            // 槽 0 原 a 现为空。
            Assert.IsTrue(slots[0].IsEmpty);
            // b 仍在原槽 1。
            Assert.AreEqual("b", slots[1].PlayerId);
        }

        [Test]
        public void OnChanged_FiresOnRealChange_NotOnNoOp()
        {
            Party party = MakeParty();
            int changed = 0;
            party.OnChanged += _ => changed++;

            party.Join("a");
            Assert.AreEqual(1, changed);

            party.Join("b");
            Assert.AreEqual(2, changed);

            party.SetReady("a", true);
            Assert.AreEqual(3, changed);

            party.TransferLeader("b");
            Assert.AreEqual(4, changed);

            party.Kick("b", "a");
            Assert.AreEqual(5, changed);

            party.Leave("b");
            Assert.AreEqual(6, changed);

            // 参数非法的加入是无操作，不应触发。
            Assert.IsFalse(party.Join(null));
            Assert.AreEqual(6, changed);
        }

        [Test]
        public void OnChanged_NotFired_OnRejectedOperations()
        {
            Party party = MakeParty(2);
            party.Join("a");
            party.Join("b");
            int changed = 0;
            party.OnChanged += _ => changed++;

            // 满员加入被拒。
            Assert.IsFalse(party.Join("c"));
            // 重复加入被拒。
            Assert.IsFalse(party.Join("a"));
            // 准备状态无变化。
            Assert.IsFalse(party.SetReady("a", false));
            // 非队长踢人。
            Assert.IsFalse(party.Kick("b", "a"));
            // 踢自己。
            Assert.IsFalse(party.Kick("a", "a"));
            // 转让给现任队长。
            Assert.IsFalse(party.TransferLeader("a"));
            // 离开不在队的人。
            Assert.IsFalse(party.Leave("ghost"));

            Assert.AreEqual(0, changed);
        }

        [Test]
        public void MaxSize_NegativeTreatedAsZero()
        {
            Party party = new Party("p", -2);
            Assert.AreEqual(0, party.MaxSize);
            Assert.IsTrue(party.IsFull);
            Assert.IsFalse(party.Join("a"));
            Assert.AreEqual(0, party.Slots.Count);
        }
    }
}
