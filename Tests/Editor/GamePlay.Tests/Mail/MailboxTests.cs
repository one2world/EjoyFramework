//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EjoyFramework.GamePlay.Mail;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Mail
{
    /// <summary>
    /// 针对 <see cref="Mailbox"/> 的状态机、领取语义、过期裁剪、排序、计数与变更事件的单元测试。
    /// </summary>
    [TestFixture]
    public class MailboxTests
    {
        // 可变时钟，便于在测试中推进“当前时间”。
        private sealed class FakeClock
        {
            public long NowMs;

            public long Provide()
            {
                return NowMs;
            }
        }

        private static MailMessage Plain(string id, long sent = 0)
        {
            return new MailMessage(id, "t", "b", "s", sent, 0);
        }

        private static MailMessage WithGift(string id, long sent = 0, long expire = 0)
        {
            return new MailMessage(
                id, "t", "b", "s", sent, expire,
                new[] { new MailAttachment("gold", 10) });
        }

        // ---- Add / AddRange ----

        [Test]
        public void Add_IncreasesCount()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            Assert.AreEqual(1, box.Count);
        }

        [Test]
        public void Add_DuplicateId_Throws()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            Assert.Throws<System.ArgumentException>(() => box.Add(Plain("m1")));
        }

        [Test]
        public void Add_Null_Throws()
        {
            Mailbox box = new Mailbox();
            Assert.Throws<System.ArgumentNullException>(() => box.Add(null));
        }

        [Test]
        public void AddRange_AddsAll()
        {
            Mailbox box = new Mailbox();
            box.AddRange(new[] { Plain("m1"), Plain("m2"), Plain("m3") });

            Assert.AreEqual(3, box.Count);
        }

        // ---- MarkRead ----

        [Test]
        public void MarkRead_UnreadToRead_ReturnsTrue_AndDecrementsUnread()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            Assert.AreEqual(1, box.UnreadCount);
            Assert.IsTrue(box.MarkRead("m1"));
            Assert.AreEqual(0, box.UnreadCount);
        }

        [Test]
        public void MarkRead_AlreadyRead_ReturnsFalse()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));
            box.MarkRead("m1");

            Assert.IsFalse(box.MarkRead("m1"));
        }

        [Test]
        public void MarkRead_Missing_ReturnsFalse()
        {
            Mailbox box = new Mailbox();
            Assert.IsFalse(box.MarkRead("nope"));
        }

        // ---- TryClaim ----

        [Test]
        public void TryClaim_GrantsAttachmentsOnce_SetsClaimedAndRead()
        {
            Mailbox box = new Mailbox();
            box.Add(WithGift("m1"));

            Assert.IsTrue(box.TryClaim("m1", out IReadOnlyList<MailAttachment> granted));
            Assert.AreEqual(1, granted.Count);
            Assert.AreEqual("gold", granted[0].ItemId);

            MailMessage mail = box.Messages.First(m => m.Id == "m1");
            Assert.AreEqual(MailState.Claimed, mail.State);
            // 已领取必然非未读。
            Assert.AreEqual(0, box.UnreadCount);
        }

        [Test]
        public void TryClaim_SecondCall_ReturnsFalseAndEmpty()
        {
            Mailbox box = new Mailbox();
            box.Add(WithGift("m1"));
            box.TryClaim("m1", out _);

            Assert.IsFalse(box.TryClaim("m1", out IReadOnlyList<MailAttachment> granted));
            Assert.AreEqual(0, granted.Count);
        }

        [Test]
        public void TryClaim_NoAttachments_ReturnsFalse()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            Assert.IsFalse(box.TryClaim("m1", out IReadOnlyList<MailAttachment> granted));
            Assert.AreEqual(0, granted.Count);
        }

        [Test]
        public void TryClaim_Missing_ReturnsFalse()
        {
            Mailbox box = new Mailbox();
            Assert.IsFalse(box.TryClaim("nope", out _));
        }

        // ---- ClaimAll ----

        [Test]
        public void ClaimAll_ClaimsEveryClaimable_AppendsAttachments()
        {
            Mailbox box = new Mailbox();
            box.Add(WithGift("m1"));
            box.Add(WithGift("m2"));
            box.Add(Plain("m3")); // 无附件，不应被领取

            List<MailAttachment> granted = new List<MailAttachment>();
            int claimed = box.ClaimAll(granted);

            Assert.AreEqual(2, claimed);
            Assert.AreEqual(2, granted.Count);
            Assert.AreEqual(0, box.ClaimableCount);
        }

        [Test]
        public void ClaimAll_AppendsToExistingList()
        {
            Mailbox box = new Mailbox();
            box.Add(WithGift("m1"));

            List<MailAttachment> granted = new List<MailAttachment>
            {
                new MailAttachment("preexisting", 1),
            };
            int claimed = box.ClaimAll(granted);

            Assert.AreEqual(1, claimed);
            Assert.AreEqual(2, granted.Count);
            Assert.AreEqual("preexisting", granted[0].ItemId);
        }

        [Test]
        public void ClaimAll_NothingClaimable_ReturnsZero()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            List<MailAttachment> granted = new List<MailAttachment>();
            Assert.AreEqual(0, box.ClaimAll(granted));
            Assert.AreEqual(0, granted.Count);
        }

        [Test]
        public void ClaimAll_NullList_Throws()
        {
            Mailbox box = new Mailbox();
            Assert.Throws<System.ArgumentNullException>(() => box.ClaimAll(null));
        }

        // ---- Delete ----

        [Test]
        public void Delete_RemovesMail()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            Assert.IsTrue(box.Delete("m1"));
            Assert.AreEqual(0, box.Count);
        }

        [Test]
        public void Delete_Missing_ReturnsFalse()
        {
            Mailbox box = new Mailbox();
            Assert.IsFalse(box.Delete("nope"));
        }

        // ---- PruneExpired & expiry exclusion ----

        [Test]
        public void ExpiredMail_ExcludedFromMessagesAndCounts_BeforePrune()
        {
            FakeClock clock = new FakeClock { NowMs = 100 };
            Mailbox box = new Mailbox(clock.Provide);
            box.Add(WithGift("live", sent: 10, expire: 1000)); // 未过期
            box.Add(WithGift("dead", sent: 10, expire: 50));   // 已过期 (now=100 > 50)

            // 尚未 Prune，但过期邮件已被排除。
            Assert.AreEqual(1, box.Count);
            Assert.AreEqual(1, box.UnreadCount);
            Assert.AreEqual(1, box.ClaimableCount);
            Assert.IsFalse(box.Messages.Any(m => m.Id == "dead"));
            Assert.IsTrue(box.Messages.Any(m => m.Id == "live"));
        }

        [Test]
        public void PruneExpired_RemovesExpired_ReturnsCount()
        {
            FakeClock clock = new FakeClock { NowMs = 100 };
            Mailbox box = new Mailbox(clock.Provide);
            box.Add(Plain("live")); // never expires (expire=0)
            box.Add(WithGift("dead1", sent: 0, expire: 50));
            box.Add(WithGift("dead2", sent: 0, expire: 99));

            int pruned = box.PruneExpired();

            Assert.AreEqual(2, pruned);
            Assert.AreEqual(1, box.Count);
            Assert.IsTrue(box.Messages.Any(m => m.Id == "live"));
        }

        [Test]
        public void PruneExpired_NothingExpired_ReturnsZero()
        {
            FakeClock clock = new FakeClock { NowMs = 10 };
            Mailbox box = new Mailbox(clock.Provide);
            box.Add(WithGift("m1", sent: 0, expire: 1000));

            Assert.AreEqual(0, box.PruneExpired());
        }

        [Test]
        public void ExpireBoundary_EqualNow_NotYetExpired()
        {
            FakeClock clock = new FakeClock { NowMs = 100 };
            Mailbox box = new Mailbox(clock.Provide);
            box.Add(WithGift("m1", sent: 0, expire: 100)); // now == expire => not expired (strict >)

            Assert.AreEqual(1, box.Count);
            Assert.AreEqual(0, box.PruneExpired());
        }

        [Test]
        public void NoProvider_NeverExpires()
        {
            Mailbox box = new Mailbox(); // null provider => now treated as 0
            box.Add(WithGift("m1", sent: 0, expire: 1));

            Assert.AreEqual(1, box.Count);
            Assert.AreEqual(0, box.PruneExpired());
        }

        // ---- Messages ordering ----

        [Test]
        public void Messages_SortedBySentDesc()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("old", sent: 100));
            box.Add(Plain("new", sent: 300));
            box.Add(Plain("mid", sent: 200));

            List<MailMessage> ordered = box.Messages.ToList();

            Assert.AreEqual("new", ordered[0].Id);
            Assert.AreEqual("mid", ordered[1].Id);
            Assert.AreEqual("old", ordered[2].Id);
        }

        // ---- ClaimableCount ----

        [Test]
        public void ClaimableCount_OnlyCountsUnclaimedAttachmentMails()
        {
            Mailbox box = new Mailbox();
            box.Add(WithGift("g1"));   // claimable
            box.Add(WithGift("g2"));   // will be claimed below
            box.Add(Plain("p1"));      // no attachment => not claimable

            Assert.AreEqual(2, box.ClaimableCount);

            box.TryClaim("g2", out _);
            Assert.AreEqual(1, box.ClaimableCount);
        }

        // ---- OnChanged ----

        [Test]
        public void OnChanged_FiresOnRealChanges()
        {
            Mailbox box = new Mailbox();
            int fired = 0;
            box.OnChanged += _ => fired++;

            box.Add(WithGift("m1"));        // +1 添加
            box.MarkRead("m1");             // +1 未读 -> 已读
            box.TryClaim("m1", out _);      // +1 已读 -> 已领取（发放附件）
            box.Delete("m1");               // +1 删除

            Assert.AreEqual(4, fired);
        }

        [Test]
        public void OnChanged_DoesNotFireOnNoOps()
        {
            Mailbox box = new Mailbox();
            box.Add(Plain("m1"));

            int fired = 0;
            box.OnChanged += _ => fired++;

            Assert.IsFalse(box.MarkRead("nope"));   // missing
            Assert.IsFalse(box.Delete("nope"));     // missing
            Assert.IsFalse(box.TryClaim("m1", out _)); // no attachments
            box.MarkRead("m1");                     // real change (1)
            Assert.IsFalse(box.MarkRead("m1"));     // already read, no-op
            Assert.AreEqual(0, box.PruneExpired()); // nothing expired, no-op

            Assert.AreEqual(1, fired);
        }
    }
}
