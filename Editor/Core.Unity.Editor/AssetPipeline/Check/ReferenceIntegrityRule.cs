//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 引用完整性检查：
    ///   • prefab 含 Missing MonoBehaviour（脚本丢失/未编译）→ Error。
    ///   • 依赖路径在磁盘不存在（断链）→ Error。
    /// </summary>
    public sealed class ReferenceIntegrityRule : IAssetCheckRule
    {
        public string Id => "reference";
        public string DisplayName => "引用完整性";
        public AssetCheckCategory Category => AssetCheckCategory.Reference;

        public IEnumerable<AssetIssue> Check(AssetCheckContext context, AssetCheckConfig config)
        {
            var issues = new List<AssetIssue>();

            foreach (string path in context.Candidates)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();

                // 1) prefab 缺失脚本
                if (ext == ".prefab")
                {
                    GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null)
                    {
                        int missing = CountMissingScripts(go);
                        if (missing > 0)
                        {
                            issues.Add(AssetIssue.Make(AssetCheckSeverity.Error, Category, Id, path,
                                "prefab 含 " + missing + " 个 Missing 脚本（MonoBehaviour 丢失）。"));
                        }
                    }
                }

                // 2) 依赖断链（依赖路径在磁盘不存在）
                string[] deps = AssetDatabase.GetDependencies(path, recursive: false);
                for (int d = 0; d < deps.Length; d++)
                {
                    string dep = deps[d];
                    if (dep == path) continue;
                    if (!File.Exists(dep))
                    {
                        issues.Add(AssetIssue.Make(AssetCheckSeverity.Error, Category, Id, path,
                            "依赖断链：引用的资产不存在 '" + dep + "'。"));
                    }
                }
            }

            return issues;
        }

        private static int CountMissingScripts(GameObject root)
        {
            int total = 0;
            var stack = new Stack<Transform>();
            stack.Push(root.transform);
            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }
            return total;
        }
    }
}
