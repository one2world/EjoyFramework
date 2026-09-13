//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Unity.Editor.Resource;   // GlobMatcher（同 asmdef，internal 可见）

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 导入规则解析器：构造时按 Priority 降序快照启用的规则，
    /// 之后 <see cref="Resolve"/> 对单路径首命中（GlobMatcher.MatchAny + Kind 匹配 + 未被 Exclude）。
    /// 模式对齐既有 <c>BundleNameResolver</c>。
    /// </summary>
    internal sealed class AssetImportRuleResolver
    {
        private readonly List<AssetImportRule> m_Rules;
        private readonly List<string> m_ExcludePatterns;

        public AssetImportRuleResolver(AssetImportRuleConfig config)
        {
            m_Rules = new List<AssetImportRule>();
            m_ExcludePatterns = config != null ? config.ExcludePatterns : null;

            if (config != null && config.Rules != null)
            {
                for (int i = 0; i < config.Rules.Count; i++)
                {
                    AssetImportRule r = config.Rules[i];
                    if (r != null && r.Enabled) m_Rules.Add(r);
                }
                // Priority 降序；同优先级保持声明序（稳定）。
                m_Rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
        }

        /// <summary>该路径是否被排除（命中任一 ExcludePattern）。</summary>
        public bool IsExcluded(string assetPath)
        {
            return GlobMatcher.MatchAny(m_ExcludePatterns, assetPath);
        }

        /// <summary>解析路径+类别对应的首条规则；无命中或被排除返回 null。</summary>
        public AssetImportRule Resolve(string assetPath, AssetImportKind kind)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (IsExcluded(assetPath)) return null;

            for (int i = 0; i < m_Rules.Count; i++)
            {
                AssetImportRule r = m_Rules[i];
                if (r.Kind != kind) continue;
                if (GlobMatcher.MatchAny(r.PathPatterns, assetPath)) return r;
            }
            return null;
        }
    }
}
