//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Mail
{
    /// <summary>
    /// 一封邮件：标题、正文、发件人、收发时间、过期时间与附件列表。
    /// 内容字段不可变；<see cref="State"/> 由所属 <see cref="Mailbox"/> 推进（内部 setter，外部只读）。
    /// 可通过构造函数或 <see cref="Builder"/> 创建。纯逻辑、与引擎无关。
    /// </summary>
    public sealed class MailMessage
    {
        private readonly string m_Id;
        private readonly string m_Title;
        private readonly string m_Body;
        private readonly string m_Sender;
        private readonly long m_SentEpochMs;
        private readonly long m_ExpireEpochMs;
        private readonly IReadOnlyList<MailAttachment> m_Attachments;

        private MailState m_State;

        /// <summary>
        /// 构造一封邮件（默认状态为 <see cref="MailState.Unread"/>）。
        /// </summary>
        /// <param name="id">邮件唯一 Id，不可为空。</param>
        /// <param name="title">标题。</param>
        /// <param name="body">正文。</param>
        /// <param name="sender">发件人。</param>
        /// <param name="sentEpochMs">发送时间（Unix 毫秒）。</param>
        /// <param name="expireEpochMs">过期时间（Unix 毫秒）；0 表示永不过期，必须为非负。</param>
        /// <param name="attachments">附件集合，可为 null（视为无附件）；其中元素不可为 null。</param>
        /// <exception cref="ArgumentException">id 为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">expireEpochMs 为负。</exception>
        public MailMessage(
            string id,
            string title,
            string body,
            string sender,
            long sentEpochMs,
            long expireEpochMs,
            IEnumerable<MailAttachment> attachments = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("邮件 Id 不能为空。", nameof(id));
            }

            if (expireEpochMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expireEpochMs), expireEpochMs, "过期时间不能为负。");
            }

            m_Id = id;
            m_Title = title ?? string.Empty;
            m_Body = body ?? string.Empty;
            m_Sender = sender ?? string.Empty;
            m_SentEpochMs = sentEpochMs;
            m_ExpireEpochMs = expireEpochMs;
            m_Attachments = BuildAttachmentList(attachments);
            m_State = MailState.Unread;
        }

        /// <summary>
        /// 邮件唯一 Id。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 标题。
        /// </summary>
        public string Title
        {
            get { return m_Title; }
        }

        /// <summary>
        /// 正文。
        /// </summary>
        public string Body
        {
            get { return m_Body; }
        }

        /// <summary>
        /// 发件人。
        /// </summary>
        public string Sender
        {
            get { return m_Sender; }
        }

        /// <summary>
        /// 发送时间（Unix 毫秒）。
        /// </summary>
        public long SentEpochMs
        {
            get { return m_SentEpochMs; }
        }

        /// <summary>
        /// 过期时间（Unix 毫秒）；0 表示永不过期。
        /// </summary>
        public long ExpireEpochMs
        {
            get { return m_ExpireEpochMs; }
        }

        /// <summary>
        /// 附件列表（只读，可能为空集合）。
        /// </summary>
        public IReadOnlyList<MailAttachment> Attachments
        {
            get { return m_Attachments; }
        }

        /// <summary>
        /// 当前状态。仅由所属 <see cref="Mailbox"/> 推进，外部只读。
        /// </summary>
        public MailState State
        {
            get { return m_State; }
            internal set { m_State = value; }
        }

        /// <summary>
        /// 是否含有附件。
        /// </summary>
        public bool HasAttachments
        {
            get { return m_Attachments.Count > 0; }
        }

        /// <summary>
        /// 判断在给定时刻是否已过期。<see cref="ExpireEpochMs"/> 为 0 时永不过期。
        /// </summary>
        /// <param name="nowEpochMs">当前时间（Unix 毫秒）。</param>
        /// <returns>已过期返回 true。</returns>
        internal bool IsExpired(long nowEpochMs)
        {
            return m_ExpireEpochMs > 0 && nowEpochMs > m_ExpireEpochMs;
        }

        // 将传入的附件序列拷贝为不可变只读列表，跳过 null 校验由调用方语义保证元素非空。
        private static IReadOnlyList<MailAttachment> BuildAttachmentList(IEnumerable<MailAttachment> attachments)
        {
            if (attachments == null)
            {
                return Array.Empty<MailAttachment>();
            }

            List<MailAttachment> list = new List<MailAttachment>();
            foreach (MailAttachment attachment in attachments)
            {
                if (attachment == null)
                {
                    throw new ArgumentException("附件集合中不能包含 null 元素。", nameof(attachments));
                }

                list.Add(attachment);
            }

            if (list.Count == 0)
            {
                return Array.Empty<MailAttachment>();
            }

            return list.AsReadOnly();
        }

        /// <summary>
        /// <see cref="MailMessage"/> 的链式构建器，便于逐步装配附件后生成不可变邮件。
        /// </summary>
        public sealed class Builder
        {
            private readonly string m_Id;
            private readonly List<MailAttachment> m_Attachments = new List<MailAttachment>();
            private string m_Title = string.Empty;
            private string m_Body = string.Empty;
            private string m_Sender = string.Empty;
            private long m_SentEpochMs;
            private long m_ExpireEpochMs;

            /// <summary>
            /// 以邮件 Id 创建构建器。
            /// </summary>
            /// <param name="id">邮件唯一 Id，不可为空。</param>
            /// <exception cref="ArgumentException">id 为空。</exception>
            public Builder(string id)
            {
                if (string.IsNullOrEmpty(id))
                {
                    throw new ArgumentException("邮件 Id 不能为空。", nameof(id));
                }

                m_Id = id;
            }

            /// <summary>
            /// 设置标题。
            /// </summary>
            public Builder WithTitle(string title)
            {
                m_Title = title ?? string.Empty;
                return this;
            }

            /// <summary>
            /// 设置正文。
            /// </summary>
            public Builder WithBody(string body)
            {
                m_Body = body ?? string.Empty;
                return this;
            }

            /// <summary>
            /// 设置发件人。
            /// </summary>
            public Builder WithSender(string sender)
            {
                m_Sender = sender ?? string.Empty;
                return this;
            }

            /// <summary>
            /// 设置发送时间（Unix 毫秒）。
            /// </summary>
            public Builder WithSentTime(long sentEpochMs)
            {
                m_SentEpochMs = sentEpochMs;
                return this;
            }

            /// <summary>
            /// 设置过期时间（Unix 毫秒）；0 表示永不过期。
            /// </summary>
            public Builder WithExpireTime(long expireEpochMs)
            {
                m_ExpireEpochMs = expireEpochMs;
                return this;
            }

            /// <summary>
            /// 追加一条附件。
            /// </summary>
            /// <param name="itemId">物品 Id。</param>
            /// <param name="count">数量，必须为正。</param>
            public Builder AddAttachment(string itemId, int count)
            {
                m_Attachments.Add(new MailAttachment(itemId, count));
                return this;
            }

            /// <summary>
            /// 追加一条附件实例。
            /// </summary>
            /// <param name="attachment">附件，不可为 null。</param>
            public Builder AddAttachment(MailAttachment attachment)
            {
                if (attachment == null)
                {
                    throw new ArgumentNullException(nameof(attachment));
                }

                m_Attachments.Add(attachment);
                return this;
            }

            /// <summary>
            /// 构建不可变邮件实例。
            /// </summary>
            public MailMessage Build()
            {
                return new MailMessage(
                    m_Id,
                    m_Title,
                    m_Body,
                    m_Sender,
                    m_SentEpochMs,
                    m_ExpireEpochMs,
                    m_Attachments);
            }
        }
    }
}
