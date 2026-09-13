//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using EjoyFramework.Core.Unity.Editor.Resource;   // GlobMatcher

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 检查上下文：按配置筛出候选资产，并提供全规则共享的<b>反向引用集合</b>（一次构建、懒加载）。
    /// 避免每条规则各自重扫全工程（这是 ResourceDependencyAnalyzer 的痛点）。
    /// </summary>
    public sealed class AssetCheckContext
    {
        private readonly AssetCheckConfig m_Config;
        private List<string> m_Candidates;
        private HashSet<string> m_ReferencedPaths;   // 被任意资产引用到的路径集合（懒构建）

        public AssetCheckContext(AssetCheckConfig config)
        {
            m_Config = config;
        }

        /// <summary>筛选后的候选资产路径（文件，非文件夹；命中 Include、未命中 Exclude）。</summary>
        public IReadOnlyList<string> Candidates
        {
            get
            {
                if (m_Candidates == null) BuildCandidates();
                return m_Candidates;
            }
        }

        private void BuildCandidates()
        {
            m_Candidates = new List<string>();
            string[] searchFolders = (m_Config.IncludeDirectories != null && m_Config.IncludeDirectories.Count > 0)
                ? m_Config.IncludeDirectories.ToArray()
                : null;

            string[] guids = searchFolders != null
                ? AssetDatabase.FindAssets(string.Empty, searchFolders)
                : AssetDatabase.FindAssets(string.Empty);

            var seen = new HashSet<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (GlobMatcher.MatchAny(m_Config.ExcludePatterns, path)) continue;
                m_Candidates.Add(path);
            }
        }

        /// <summary>该资产是否被工程中任何其他资产引用（用于孤儿检测）。首次访问构建全工程反向引用集合。</summary>
        public bool IsReferencedByAnyone(string assetPath)
        {
            if (m_ReferencedPaths == null) BuildReferencedSet();
            return m_ReferencedPaths.Contains(assetPath);
        }

        private void BuildReferencedSet()
        {
            m_ReferencedPaths = new HashSet<string>();
            string[] all = AssetDatabase.FindAssets(string.Empty);
            try
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (i % 250 == 0)
                        EditorUtility.DisplayProgressBar("Asset Checker",
                            "Building reference index " + i + "/" + all.Length, (float)i / all.Length);

                    string path = AssetDatabase.GUIDToAssetPath(all[i]);
                    if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;

                    string[] deps = AssetDatabase.GetDependencies(path, recursive: false);
                    for (int d = 0; d < deps.Length; d++)
                    {
                        if (deps[d] != path) m_ReferencedPaths.Add(deps[d]);   // 不把自依赖算作被引用
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
