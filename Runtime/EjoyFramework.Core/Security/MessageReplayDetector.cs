//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.Core.Security
{
    /// <summary>
    /// 网络消息重放攻击检测：基于 sequence number + sliding window。
    ///
    /// 算法：维护 [base, base+windowSize) 滑动窗口 + bitmap，标记已收过的 sequence。
    /// 收到 seq < base → 太旧 拒绝；
    /// seq >= base+windowSize → 推进窗口；
    /// seq 在窗口内且已收过 → 重放 拒绝。
    /// </summary>
    public sealed class MessageReplayDetector
    {
        private readonly int m_WindowSize;
        private long m_Base;
        private readonly HashSet<long> m_Seen = new HashSet<long>();
        private int m_RejectedReplays;
        private int m_RejectedTooOld;

        public MessageReplayDetector(int windowSize = 512)
        {
            if (windowSize < 8) throw new FrameworkException("windowSize must be >= 8.");
            m_WindowSize = windowSize;
        }

        public int RejectedReplayCount => m_RejectedReplays;
        public int RejectedTooOldCount => m_RejectedTooOld;

        /// <summary>检查 sequence；返回 true 表示新消息接受、false 表示拒绝（重放或太旧）。</summary>
        public bool Accept(long sequence)
        {
            if (sequence < m_Base) { m_RejectedTooOld++; return false; }
            // 推进窗口
            if (sequence >= m_Base + m_WindowSize)
            {
                long newBase = sequence - m_WindowSize + 1;
                // 清理窗口外的旧 seq
                m_Seen.RemoveWhere(s => s < newBase);
                m_Base = newBase;
            }
            if (!m_Seen.Add(sequence)) { m_RejectedReplays++; return false; }
            return true;
        }

        public void Reset()
        {
            m_Base = 0;
            m_Seen.Clear();
            m_RejectedReplays = 0;
            m_RejectedTooOld = 0;
        }
    }
}
