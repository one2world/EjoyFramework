//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Abilities
{
    /// <summary>
    /// 层级化字符串标签集合，例如 "state.stunned"、"ability.fire"。
    /// 支持父前缀匹配：查询 "state" 时，若持有 "state.stunned" 也视为命中。
    /// </summary>
    public sealed class GameplayTagSet
    {
        private readonly HashSet<string> m_Tags = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 当前持有的全部标签（只读视图）。
        /// </summary>
        public IReadOnlyCollection<string> Tags
        {
            get { return m_Tags; }
        }

        /// <summary>
        /// 添加一个标签。空白标签会被忽略。
        /// </summary>
        /// <param name="tag">要添加的标签。</param>
        public void Add(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            m_Tags.Add(tag);
        }

        /// <summary>
        /// 移除一个标签。
        /// </summary>
        /// <param name="tag">要移除的标签。</param>
        /// <returns>若标签存在并被移除则返回 true。</returns>
        public bool Remove(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            return m_Tags.Remove(tag);
        }

        /// <summary>
        /// 查询是否命中某标签。规则：任一持有标签与查询完全相等，
        /// 或持有标签是查询标签的子级（以 "查询+'.'" 为前缀），均视为命中。
        /// 例如查询 "state" 时，持有 "state.stunned" 命中。
        /// </summary>
        /// <param name="tag">查询标签。</param>
        /// <returns>命中返回 true。</returns>
        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            // 精确命中快速路径。
            if (m_Tags.Contains(tag))
            {
                return true;
            }

            // 父前缀匹配：持有标签是查询标签的子级。
            foreach (string held in m_Tags)
            {
                if (held.Length > tag.Length
                    && held[tag.Length] == '.'
                    && held.StartsWith(tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 是否命中给定集合中的任意一个标签。
        /// </summary>
        /// <param name="tags">查询标签集合，可为空。</param>
        /// <returns>命中任意一个返回 true；集合为空返回 false。</returns>
        public bool HasAny(IEnumerable<string> tags)
        {
            if (tags == null)
            {
                return false;
            }

            foreach (string tag in tags)
            {
                if (HasTag(tag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 是否命中给定集合中的全部标签。
        /// </summary>
        /// <param name="tags">查询标签集合，可为空。</param>
        /// <returns>全部命中返回 true；集合为空视为全部满足返回 true。</returns>
        public bool HasAll(IEnumerable<string> tags)
        {
            if (tags == null)
            {
                return true;
            }

            foreach (string tag in tags)
            {
                if (!HasTag(tag))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
