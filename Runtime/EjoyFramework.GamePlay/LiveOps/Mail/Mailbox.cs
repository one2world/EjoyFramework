//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Mail
{
    /// <summary>
    /// 本地邮箱模型：维护一组 <see cref="MailMessage"/> 并管理其状态机（未读→已读→已领取）。
    /// 仅负责本地数据与状态推进，服务端同步由游戏层经 HTTP 完成。
    ///
    /// 过期判定依赖构造时注入的“当前时间提供器”：
    /// <list type="bullet">
    /// <item>已过期邮件即便尚未 <see cref="PruneExpired"/>，也不计入 <see cref="Messages"/> 与各计数。</item>
    /// <item>提供器为 null 时不做过期裁剪（当前时间视为 0，等价于一切均未过期）。</item>
    /// </list>
    /// 领取语义：仅对“含附件”的邮件有意义——领取发放一次附件并置为 <see cref="MailState.Claimed"/>（同时置为已读），
    /// 二次领取无效（返回 false 且附件为空）。无附件邮件不可领取，只能标记已读或删除。
    ///
    /// 仅在状态真正发生改变时触发 <see cref="OnChanged"/>。纯逻辑、与引擎无关，单线程使用，非线程安全。
    /// </summary>
    public sealed class Mailbox
    {
        private static readonly IReadOnlyList<MailAttachment> s_EmptyAttachments = Array.Empty<MailAttachment>();

        private readonly Dictionary<string, MailMessage> m_Messages =
            new Dictionary<string, MailMessage>(StringComparer.Ordinal);

        private readonly Func<long> m_NowProvider;

        /// <summary>
        /// 构造邮箱。
        /// </summary>
        /// <param name="nowEpochMsProvider">
        /// 当前时间（Unix 毫秒）提供器；为 null 时不做过期裁剪（当前时间视为 0）。
        /// </param>
        public Mailbox(Func<long> nowEpochMsProvider = null)
        {
            m_NowProvider = nowEpochMsProvider;
        }

        /// <summary>
        /// 邮箱内容（增删、状态变更）发生改变后触发，回调参数为邮箱自身。
        /// 仅在真实改变时触发，无操作不会触发。
        /// </summary>
        public event Action<Mailbox> OnChanged;

        /// <summary>
        /// 当前未过期邮件数量。
        /// </summary>
        public int Count
        {
            get
            {
                long now = Now();
                int count = 0;
                foreach (MailMessage message in m_Messages.Values)
                {
                    if (!message.IsExpired(now))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 未读且未过期的邮件数量。
        /// </summary>
        public int UnreadCount
        {
            get
            {
                long now = Now();
                int count = 0;
                foreach (MailMessage message in m_Messages.Values)
                {
                    if (message.State == MailState.Unread && !message.IsExpired(now))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 可领取邮件数量：含附件、尚未领取（非 <see cref="MailState.Claimed"/>）且未过期。
        /// </summary>
        public int ClaimableCount
        {
            get
            {
                long now = Now();
                int count = 0;
                foreach (MailMessage message in m_Messages.Values)
                {
                    if (IsClaimable(message, now))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 所有未过期邮件，按 <see cref="MailMessage.SentEpochMs"/> 降序（最新在前）。
        /// 每次枚举返回新的快照，遍历期间对邮箱的修改不影响已取得的序列。
        /// </summary>
        public IEnumerable<MailMessage> Messages
        {
            get
            {
                long now = Now();
                List<MailMessage> list = new List<MailMessage>(m_Messages.Count);
                foreach (MailMessage message in m_Messages.Values)
                {
                    if (!message.IsExpired(now))
                    {
                        list.Add(message);
                    }
                }

                list.Sort(CompareBySentDesc);
                return list;
            }
        }

        /// <summary>
        /// 添加一封邮件。
        /// </summary>
        /// <param name="message">邮件，不可为 null。</param>
        /// <exception cref="ArgumentNullException">message 为 null。</exception>
        /// <exception cref="ArgumentException">邮件 Id 已存在。</exception>
        public void Add(MailMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (m_Messages.ContainsKey(message.Id))
            {
                throw new ArgumentException($"邮件 Id 已存在：{message.Id}", nameof(message));
            }

            m_Messages.Add(message.Id, message);
            RaiseChanged();
        }

        /// <summary>
        /// 批量添加邮件。任一邮件为 null 或 Id 重复都会抛出异常；
        /// 异常前已加入的邮件保留，但仅在确有邮件加入时触发一次 <see cref="OnChanged"/>。
        /// </summary>
        /// <param name="messages">邮件序列，不可为 null。</param>
        /// <exception cref="ArgumentNullException">messages 为 null，或其中含 null 元素。</exception>
        /// <exception cref="ArgumentException">某封邮件 Id 已存在。</exception>
        public void AddRange(IEnumerable<MailMessage> messages)
        {
            if (messages == null)
            {
                throw new ArgumentNullException(nameof(messages));
            }

            int added = 0;
            try
            {
                foreach (MailMessage message in messages)
                {
                    if (message == null)
                    {
                        throw new ArgumentNullException(nameof(messages), "邮件序列中不能包含 null 元素。");
                    }

                    if (m_Messages.ContainsKey(message.Id))
                    {
                        throw new ArgumentException($"邮件 Id 已存在：{message.Id}", nameof(messages));
                    }

                    m_Messages.Add(message.Id, message);
                    added++;
                }
            }
            finally
            {
                if (added > 0)
                {
                    RaiseChanged();
                }
            }
        }

        /// <summary>
        /// 将未读邮件标记为已读。
        /// </summary>
        /// <param name="id">邮件 Id。</param>
        /// <returns>状态由 <see cref="MailState.Unread"/> 变为 <see cref="MailState.Read"/> 返回 true；
        /// 邮件不存在、已过期或已读/已领取返回 false。</returns>
        public bool MarkRead(string id)
        {
            if (!TryGetActive(id, out MailMessage message))
            {
                return false;
            }

            if (message.State != MailState.Unread)
            {
                return false;
            }

            message.State = MailState.Read;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 领取一封邮件的附件：发放一次后置为 <see cref="MailState.Claimed"/>（同时视为已读）。幂等。
        /// </summary>
        /// <param name="id">邮件 Id。</param>
        /// <param name="attachments">输出：本次发放的附件；未发放时为空列表。</param>
        /// <returns>本次确有发放（首次领取且含附件、未过期）返回 true；否则返回 false。</returns>
        public bool TryClaim(string id, out IReadOnlyList<MailAttachment> attachments)
        {
            attachments = s_EmptyAttachments;

            if (!TryGetActive(id, out MailMessage message))
            {
                return false;
            }

            if (!IsClaimable(message, Now()))
            {
                return false;
            }

            attachments = message.Attachments;
            message.State = MailState.Claimed;
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 领取全部可领取邮件，将其附件追加进给定列表，并把这些邮件置为 <see cref="MailState.Claimed"/>。
        /// </summary>
        /// <param name="grantedInto">用于收集所有发放附件的列表，不可为 null。</param>
        /// <returns>本次实际领取的邮件数量。</returns>
        /// <exception cref="ArgumentNullException">grantedInto 为 null。</exception>
        public int ClaimAll(List<MailAttachment> grantedInto)
        {
            if (grantedInto == null)
            {
                throw new ArgumentNullException(nameof(grantedInto));
            }

            long now = Now();
            int claimed = 0;

            // 先快照可领取邮件，避免在遍历 Dictionary 的同时修改其元素状态。
            foreach (MailMessage message in m_Messages.Values)
            {
                if (IsClaimable(message, now))
                {
                    IReadOnlyList<MailAttachment> list = message.Attachments;
                    for (int i = 0; i < list.Count; i++)
                    {
                        grantedInto.Add(list[i]);
                    }

                    message.State = MailState.Claimed;
                    claimed++;
                }
            }

            if (claimed > 0)
            {
                RaiseChanged();
            }

            return claimed;
        }

        /// <summary>
        /// 删除一封邮件（无论其状态或是否过期）。
        /// </summary>
        /// <param name="id">邮件 Id。</param>
        /// <returns>确有删除返回 true；不存在返回 false。</returns>
        public bool Delete(string id)
        {
            if (id == null || !m_Messages.Remove(id))
            {
                return false;
            }

            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 清理所有已过期邮件（<see cref="MailMessage.ExpireEpochMs"/> 大于 0 且当前时间已超过）。
        /// </summary>
        /// <returns>被移除的邮件数量。</returns>
        public int PruneExpired()
        {
            long now = Now();

            // 先收集待删 Id，避免遍历过程中修改 Dictionary。
            List<string> expiredIds = null;
            foreach (KeyValuePair<string, MailMessage> pair in m_Messages)
            {
                if (pair.Value.IsExpired(now))
                {
                    if (expiredIds == null)
                    {
                        expiredIds = new List<string>();
                    }

                    expiredIds.Add(pair.Key);
                }
            }

            if (expiredIds == null)
            {
                return 0;
            }

            for (int i = 0; i < expiredIds.Count; i++)
            {
                m_Messages.Remove(expiredIds[i]);
            }

            RaiseChanged();
            return expiredIds.Count;
        }

        // 当前时间：无提供器则视为 0（不触发任何过期）。
        private long Now()
        {
            return m_NowProvider != null ? m_NowProvider() : 0L;
        }

        // 取出存在且未过期的邮件。
        private bool TryGetActive(string id, out MailMessage message)
        {
            if (id != null && m_Messages.TryGetValue(id, out message) && !message.IsExpired(Now()))
            {
                return true;
            }

            message = null;
            return false;
        }

        // 可领取判定：含附件、未领取、未过期。
        private static bool IsClaimable(MailMessage message, long now)
        {
            return message.HasAttachments
                && message.State != MailState.Claimed
                && !message.IsExpired(now);
        }

        // 按发送时间降序排序（最新在前）；时间相同则按 Id 升序保证稳定。
        private static int CompareBySentDesc(MailMessage a, MailMessage b)
        {
            int byTime = b.SentEpochMs.CompareTo(a.SentEpochMs);
            if (byTime != 0)
            {
                return byTime;
            }

            return string.CompareOrdinal(a.Id, b.Id);
        }

        private void RaiseChanged()
        {
            OnChanged?.Invoke(this);
        }
    }
}
