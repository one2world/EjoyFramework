//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Mail;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Mail
{
    /// <summary>
    /// 针对 <see cref="MailMessage"/> 构造、默认状态、附件标志与 <see cref="MailMessage.Builder"/> 的单元测试。
    /// </summary>
    [TestFixture]
    public class MailMessageTests
    {
        [Test]
        public void Ctor_DefaultsToUnread_NoAttachments()
        {
            MailMessage mail = new MailMessage("m1", "t", "b", "s", 100, 0);

            Assert.AreEqual("m1", mail.Id);
            Assert.AreEqual(MailState.Unread, mail.State);
            Assert.IsFalse(mail.HasAttachments);
            Assert.AreEqual(0, mail.Attachments.Count);
        }

        [Test]
        public void Ctor_NullStrings_BecomeEmpty()
        {
            MailMessage mail = new MailMessage("m1", null, null, null, 0, 0);

            Assert.AreEqual(string.Empty, mail.Title);
            Assert.AreEqual(string.Empty, mail.Body);
            Assert.AreEqual(string.Empty, mail.Sender);
        }

        [Test]
        public void Ctor_EmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new MailMessage("", "t", "b", "s", 0, 0));
        }

        [Test]
        public void Ctor_NegativeExpire_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new MailMessage("m1", "t", "b", "s", 0, -1));
        }

        [Test]
        public void Ctor_WithAttachments_SetsHasAttachments()
        {
            MailMessage mail = new MailMessage(
                "m1", "t", "b", "s", 0, 0,
                new[] { new MailAttachment("gold", 10), new MailAttachment("gem", 2) });

            Assert.IsTrue(mail.HasAttachments);
            Assert.AreEqual(2, mail.Attachments.Count);
            Assert.AreEqual("gold", mail.Attachments[0].ItemId);
        }

        [Test]
        public void Ctor_NullAttachmentElement_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => new MailMessage("m1", "t", "b", "s", 0, 0, new MailAttachment[] { null }));
        }

        [Test]
        public void Builder_BuildsEquivalentMessage()
        {
            MailMessage mail = new MailMessage.Builder("m1")
                .WithTitle("hello")
                .WithBody("world")
                .WithSender("system")
                .WithSentTime(1234)
                .WithExpireTime(5678)
                .AddAttachment("gold", 50)
                .AddAttachment(new MailAttachment("gem", 3))
                .Build();

            Assert.AreEqual("m1", mail.Id);
            Assert.AreEqual("hello", mail.Title);
            Assert.AreEqual("world", mail.Body);
            Assert.AreEqual("system", mail.Sender);
            Assert.AreEqual(1234, mail.SentEpochMs);
            Assert.AreEqual(5678, mail.ExpireEpochMs);
            Assert.AreEqual(2, mail.Attachments.Count);
            Assert.AreEqual(MailState.Unread, mail.State);
        }

        [Test]
        public void Builder_EmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new MailMessage.Builder(""));
        }
    }
}
