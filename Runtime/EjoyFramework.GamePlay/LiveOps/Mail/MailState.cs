//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.GamePlay.Mail
{
    /// <summary>
    /// 邮件状态，由 <see cref="Mailbox"/> 单向推进：Unread → Read → Claimed。
    /// 状态只进不退；领取附件（<see cref="Mailbox.TryClaim"/>）会同时标记为已读。
    /// </summary>
    public enum MailState
    {
        /// <summary>未读。新邮件的初始状态。</summary>
        Unread,

        /// <summary>已读，但附件（若有）尚未领取。</summary>
        Read,

        /// <summary>已领取。附件已发放，终态。</summary>
        Claimed,
    }
}
