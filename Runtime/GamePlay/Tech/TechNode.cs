//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Tech
{
    /// <summary>
    /// 科技 / 升级树中的单个节点定义。属于静态结构数据，定义后不应再修改。
    /// 节点自身不持有运行期等级；等级与状态由 <see cref="TechTree"/> 统一维护。
    /// </summary>
    public sealed class TechNode
    {
        private readonly string m_Id;
        private readonly int m_MaxLevel;
        private readonly IReadOnlyList<string> m_Prerequisites;
        private readonly object m_Payload;

        private static readonly string[] s_EmptyPrerequisites = new string[0];

        /// <summary>
        /// 构造一个科技节点。
        /// </summary>
        /// <param name="id">节点唯一标识，不可为空。</param>
        /// <param name="maxLevel">最大等级，必须大于等于 1；小于 1 时归一化为 1。</param>
        /// <param name="prerequisites">前置节点 Id 列表；当每个前置节点均已解锁（等级 &gt;= 1）时视为满足。为空表示无前置（根节点）。</param>
        /// <param name="payload">可选的游戏侧不透明数据（如花费、效果等）。</param>
        /// <exception cref="ArgumentException">当 id 为空时抛出。</exception>
        public TechNode(string id, int maxLevel = 1, IReadOnlyList<string> prerequisites = null, object payload = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("科技节点的 id 不能为空。", nameof(id));
            }

            m_Id = id;
            // 归一化：最大等级至少为 1，避免出现无法解锁的非法节点。
            m_MaxLevel = maxLevel < 1 ? 1 : maxLevel;
            m_Prerequisites = prerequisites ?? s_EmptyPrerequisites;
            m_Payload = payload;
        }

        /// <summary>
        /// 节点唯一标识。
        /// </summary>
        public string Id
        {
            get { return m_Id; }
        }

        /// <summary>
        /// 节点最大等级，已归一化为至少 1。
        /// </summary>
        public int MaxLevel
        {
            get { return m_MaxLevel; }
        }

        /// <summary>
        /// 前置节点 Id 列表。当每个前置节点均已解锁（等级 &gt;= 1）时，本节点的前置条件满足。
        /// 列表中引用的缺失节点 Id 视为永远无法满足。
        /// </summary>
        public IReadOnlyList<string> Prerequisites
        {
            get { return m_Prerequisites; }
        }

        /// <summary>
        /// 可选的游戏侧不透明数据（如花费、效果等）。
        /// </summary>
        public object Payload
        {
            get { return m_Payload; }
        }
    }
}
