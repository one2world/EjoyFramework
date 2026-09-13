//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Mail
{
    /// <summary>
    /// 邮件附件：一条“物品 Id + 数量”记录。
    /// 不可变值对象，由 <see cref="MailMessage"/> 持有，领取时由 <see cref="Mailbox"/> 发放。
    /// 纯逻辑、与引擎无关。
    /// </summary>
    public sealed class MailAttachment
    {
        private readonly string m_ItemId;
        private readonly int m_Count;

        /// <summary>
        /// 构造附件。
        /// </summary>
        /// <param name="itemId">物品 Id，不可为空。</param>
        /// <param name="count">数量，必须为正。</param>
        /// <exception cref="ArgumentException">itemId 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">count 不为正。</exception>
        public MailAttachment(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("附件物品 Id 不能为空。", nameof(itemId));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "附件数量必须为正。");
            }

            m_ItemId = itemId;
            m_Count = count;
        }

        /// <summary>
        /// 物品 Id。
        /// </summary>
        public string ItemId
        {
            get { return m_ItemId; }
        }

        /// <summary>
        /// 数量。
        /// </summary>
        public int Count
        {
            get { return m_Count; }
        }
    }
}
