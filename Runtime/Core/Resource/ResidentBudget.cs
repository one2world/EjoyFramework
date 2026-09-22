//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 常驻集内存预算策略（纯 C#，loader 无关）。
    ///
    /// 模型：loader 把每个已加载单元（bundle）登记进来，报告引用计数在 0 与非 0 之间的迁移；
    /// 预算启用时，引用归零的单元**不立即卸载**而是进入"温缓存"（最近最少使用队列），
    /// 只有常驻总字节超过预算才按 LRU 淘汰——开放世界里玩家来回走动时，刚离开的区块资源大概率马上又要用，
    /// 温缓存把"卸载再重载"变成"直接命中"，而预算保证它不会无限增长。
    ///
    /// 预算为 0 = 未启用：策略只做统计，<see cref="SelectEvictions"/> 永远返回空，loader 保持原有的延迟卸载行为。
    ///
    /// 全部操作零分配（内部集合复用；淘汰候选排序走 <see cref="NoAllocSort"/>）。线程契约：仅主线程。
    /// </summary>
    public sealed class ResidentBudget
    {
        private sealed class Entry
        {
            public string Key;
            public long Bytes;
            public bool Referenced;
            public long LastUseTick;
        }

        private static readonly IComparer<Entry> s_ByLastUse = new LastUseComparer();

        private readonly Dictionary<string, Entry> m_Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<Entry> m_Candidates = new List<Entry>();
        private readonly Stack<Entry> m_EntryPool = new Stack<Entry>();
        private long m_BudgetBytes;
        private long m_ResidentBytes;
        private long m_WarmBytes;
        private long m_Tick;
        private long m_EvictionCount;
        private long m_WarmHitCount;
        private long m_PressureWarningCount;
        private bool m_PressureWarned;

        /// <summary>预算字节数；0 = 未启用。</summary>
        public long BudgetBytes
        {
            get { return m_BudgetBytes; }
            set { m_BudgetBytes = value < 0 ? 0 : value; m_PressureWarned = false; }
        }

        /// <summary>是否启用预算。</summary>
        public bool IsEnabled
        {
            get { return m_BudgetBytes > 0; }
        }

        /// <summary>常驻总字节（引用中 + 温缓存）。</summary>
        public long ResidentBytes
        {
            get { return m_ResidentBytes; }
        }

        /// <summary>温缓存字节（引用计数为 0、尚未淘汰）。</summary>
        public long WarmBytes
        {
            get { return m_WarmBytes; }
        }

        /// <summary>登记的单元数。</summary>
        public int Count
        {
            get { return m_Entries.Count; }
        }

        /// <summary>累计淘汰次数。</summary>
        public long EvictionCount
        {
            get { return m_EvictionCount; }
        }

        /// <summary>累计温缓存命中（引用归零后又被重新引用）次数。</summary>
        public long WarmHitCount
        {
            get { return m_WarmHitCount; }
        }

        /// <summary>累计"超预算且无可淘汰"告警次数（每次进入压力状态计一次）。</summary>
        public long PressureWarningCount
        {
            get { return m_PressureWarningCount; }
        }

        /// <summary>预算超限但无可淘汰单元（全部在引用中）——只告警一次，预算调整后重新武装。</summary>
        public bool IsUnderPressure
        {
            get { return IsEnabled && m_ResidentBytes > m_BudgetBytes && m_WarmBytes == 0; }
        }

        // ================================================================
        //  loader 上报
        // ================================================================

        /// <summary>单元加载完成。<paramref name="referenced"/> 通常为 true（加载即被引用）。重复登记同键覆盖大小。</summary>
        public void OnLoaded(string key, long bytes, bool referenced)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new FrameworkException("ResidentBudget.OnLoaded：key 不能为空。");
            }

            if (bytes < 0) bytes = 0;
            Entry entry;
            if (m_Entries.TryGetValue(key, out entry))
            {
                m_ResidentBytes -= entry.Bytes;
                if (!entry.Referenced) m_WarmBytes -= entry.Bytes;
                entry.Bytes = bytes;
            }
            else
            {
                entry = AcquireEntry();
                entry.Key = key;
                entry.Bytes = bytes;
                m_Entries.Add(key, entry);
            }

            entry.Referenced = referenced;
            entry.LastUseTick = ++m_Tick;
            m_ResidentBytes += bytes;
            if (!referenced) m_WarmBytes += bytes;
        }

        /// <summary>单元引用计数从 0 变为非 0（温缓存命中）。</summary>
        public void OnReferenced(string key)
        {
            Entry entry;
            if (!m_Entries.TryGetValue(key, out entry) || entry.Referenced) return;
            entry.Referenced = true;
            entry.LastUseTick = ++m_Tick;
            m_WarmBytes -= entry.Bytes;
            m_WarmHitCount++;
        }

        /// <summary>单元引用计数归零：进入温缓存。</summary>
        public void OnUnreferenced(string key)
        {
            Entry entry;
            if (!m_Entries.TryGetValue(key, out entry) || !entry.Referenced) return;
            entry.Referenced = false;
            entry.LastUseTick = ++m_Tick;
            m_WarmBytes += entry.Bytes;
        }

        /// <summary>单元已卸载（无论由谁触发）。</summary>
        public void OnUnloaded(string key)
        {
            Entry entry;
            if (!m_Entries.TryGetValue(key, out entry)) return;
            m_Entries.Remove(key);
            m_ResidentBytes -= entry.Bytes;
            if (!entry.Referenced) m_WarmBytes -= entry.Bytes;
            ReleaseEntry(entry);
        }

        /// <summary>清空全部登记（loader 关闭）。</summary>
        public void Clear()
        {
            foreach (KeyValuePair<string, Entry> kv in m_Entries) ReleaseEntry(kv.Value);
            m_Entries.Clear();
            m_ResidentBytes = 0;
            m_WarmBytes = 0;
            m_PressureWarned = false;
        }

        // ================================================================
        //  决策
        // ================================================================

        /// <summary>
        /// 选出应淘汰的单元键（最久未用优先），直到常驻字节不超过预算或温缓存耗尽。
        /// 预算未启用返回 0。<paramref name="results"/> 会先被清空；调用方逐个卸载后需回报 <see cref="OnUnloaded"/>。
        /// </summary>
        public int SelectEvictions(List<string> results)
        {
            return SelectEvictions(results, m_BudgetBytes);
        }

        /// <summary>按指定目标字节选淘汰（主动收缩：关卡切换、内存告警时把目标设得更低）。</summary>
        public int SelectEvictions(List<string> results, long targetBytes)
        {
            if (results == null)
            {
                throw new FrameworkException("ResidentBudget.SelectEvictions：results 不能为 null。");
            }

            results.Clear();
            if (!IsEnabled || m_ResidentBytes <= targetBytes)
            {
                return 0;
            }

            m_Candidates.Clear();
            foreach (KeyValuePair<string, Entry> kv in m_Entries)
            {
                if (!kv.Value.Referenced) m_Candidates.Add(kv.Value);
            }

            if (m_Candidates.Count == 0)
            {
                if (!m_PressureWarned)
                {
                    m_PressureWarned = true;
                    m_PressureWarningCount++;
                    FrameworkLog.Warning("ResidentBudget：常驻 {0} 字节超过预算 {1} 字节，但全部单元都在引用中，无可淘汰。请检查是否有资源泄漏或预算设得过低。", m_ResidentBytes, m_BudgetBytes);
                }

                return 0;
            }

            NoAllocSort.Sort(m_Candidates, s_ByLastUse);
            long projected = m_ResidentBytes;
            int selected = 0;
            for (int i = 0; i < m_Candidates.Count && projected > targetBytes; i++)
            {
                results.Add(m_Candidates[i].Key);
                projected -= m_Candidates[i].Bytes;
                selected++;
            }

            m_Candidates.Clear();
            m_EvictionCount += selected;
            m_PressureWarned = false;
            return selected;
        }

        private Entry AcquireEntry()
        {
            return m_EntryPool.Count > 0 ? m_EntryPool.Pop() : new Entry();
        }

        private void ReleaseEntry(Entry entry)
        {
            entry.Key = null;
            entry.Bytes = 0;
            entry.Referenced = false;
            entry.LastUseTick = 0;
            m_EntryPool.Push(entry);
        }

        private sealed class LastUseComparer : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b)
            {
                return a.LastUseTick.CompareTo(b.LastUseTick);
            }
        }
    }
}
