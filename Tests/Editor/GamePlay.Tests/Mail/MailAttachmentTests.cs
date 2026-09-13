//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Mail;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Mail
{
    /// <summary>
    /// 针对 <see cref="MailAttachment"/> 的构造校验单元测试。
    /// </summary>
    [TestFixture]
    public class MailAttachmentTests
    {
        [Test]
        public void Ctor_StoresValues()
        {
            MailAttachment attachment = new MailAttachment("gold", 100);

            Assert.AreEqual("gold", attachment.ItemId);
            Assert.AreEqual(100, attachment.Count);
        }

        [Test]
        public void Ctor_EmptyItemId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new MailAttachment(string.Empty, 1));
            Assert.Throws<System.ArgumentException>(() => new MailAttachment(null, 1));
        }

        [Test]
        public void Ctor_NonPositiveCount_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MailAttachment("gold", 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MailAttachment("gold", -5));
        }
    }
}
