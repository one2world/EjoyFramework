//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Resource
{
    /// <summary>
    /// 资产 → bundle 名解析器。按 config 规则降序匹配；累计 SizeLimit 时切割。
    /// 输出每个资产应归属的 bundle 名（已 Sanitize）。
    /// </summary>
    internal sealed class BundleNameResolver
    {
        private readonly AssetBundleBuildConfig m_Config;
        private readonly List<BundleBuildRule> m_SortedRules;
        // SizeLimit 状态：bundle 名前缀 → 当前累计 byte / 当前 chunk index
        private readonly Dictionary<string, SizeChunkState> m_SizeStates = new Dictionary<string, SizeChunkState>(StringComparer.Ordinal);

        public BundleNameResolver(AssetBundleBuildConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            m_Config = config;
            m_SortedRules = new List<BundleBuildRule>(config.BuildRules ?? new List<BundleBuildRule>());
            m_SortedRules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>
        /// 解析单个 asset 的 bundle 名。
        /// </summary>
        public string Resolve(string assetPath, long assetSize)
        {
            if (string.IsNullOrEmpty(assetPath)) return GlobMatcher.SanitizeBundleName("default");

            BundleBuildRule matched = MatchRule(assetPath);
            BundleSplitStrategy strategy = matched != null ? matched.Strategy : m_Config.DefaultStrategy;
            long sizeLimit = matched != null && matched.OverrideSizeLimitBytes > 0
                ? matched.OverrideSizeLimitBytes
                : m_Config.SizeLimitBytes;

            switch (strategy)
            {
                case BundleSplitStrategy.SingleBundle:
                    if (matched != null && !string.IsNullOrEmpty(matched.SingleBundleName))
                        return GlobMatcher.SanitizeBundleName(matched.SingleBundleName);
                    // 未配置 SingleBundleName 时降级为 PerDirectory
                    return ResolvePerDirectory(assetPath);

                case BundleSplitStrategy.PerFile:
                    return ResolvePerFile(assetPath);

                case BundleSplitStrategy.SizeLimit:
                    return ResolveSizeLimit(assetPath, assetSize, sizeLimit);

                case BundleSplitStrategy.PerDirectory:
                default:
                    return ResolvePerDirectory(assetPath);
            }
        }

        private BundleBuildRule MatchRule(string assetPath)
        {
            for (int i = 0; i < m_SortedRules.Count; i++)
            {
                var r = m_SortedRules[i];
                if (r == null || r.PathPatterns == null || r.PathPatterns.Count == 0) continue;
                if (GlobMatcher.MatchAny(r.PathPatterns, assetPath)) return r;
            }
            return null;
        }

        private static string ResolvePerDirectory(string assetPath)
        {
            // Assets/Foo/Bar/Baz.prefab → foo_bar
            string dir = Path.GetDirectoryName(assetPath) ?? string.Empty;
            dir = dir.Replace('\\', '/');
            if (dir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) dir = dir.Substring("Assets/".Length);
            else if (dir.Equals("Assets", StringComparison.OrdinalIgnoreCase)) dir = "root";
            return GlobMatcher.SanitizeBundleName(dir.Length == 0 ? "root" : dir);
        }

        private static string ResolvePerFile(string assetPath)
        {
            string nameNoExt = Path.GetFileNameWithoutExtension(assetPath);
            string dir = Path.GetDirectoryName(assetPath) ?? string.Empty;
            dir = dir.Replace('\\', '/');
            if (dir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) dir = dir.Substring("Assets/".Length);
            string raw = string.IsNullOrEmpty(dir) ? nameNoExt : (dir + "/" + nameNoExt);
            return GlobMatcher.SanitizeBundleName(raw);
        }

        private string ResolveSizeLimit(string assetPath, long assetSize, long limit)
        {
            string baseName = ResolvePerDirectory(assetPath);
            SizeChunkState st;
            if (!m_SizeStates.TryGetValue(baseName, out st))
            {
                st = new SizeChunkState { ChunkIndex = 0, AccumulatedBytes = 0 };
                m_SizeStates[baseName] = st;
            }
            // 若加入当前 asset 后超过阈值，切到下一 chunk（除非当前 chunk 还是空的，避免单 asset 大于 limit 永远切不出去）
            if (st.AccumulatedBytes > 0 && st.AccumulatedBytes + assetSize > limit)
            {
                st.ChunkIndex++;
                st.AccumulatedBytes = 0;
            }
            st.AccumulatedBytes += assetSize;
            return st.ChunkIndex == 0 ? baseName : (baseName + "_" + st.ChunkIndex);
        }

        private sealed class SizeChunkState
        {
            public int ChunkIndex;
            public long AccumulatedBytes;
        }
    }
}
