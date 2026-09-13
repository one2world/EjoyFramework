//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 导入规则的<b>批量手动</b>执行器：对选中/全部资产强制套用规则预设并重导入。
    /// 与首次导入 postprocessor 不同——本路径会覆盖已有导入设置，用于"统一存量资产"。
    /// </summary>
    public static class AssetImportRulesApplier
    {
        [MenuItem("EjoyFramework/Core/Resource/Import Rules/Apply to Selection", priority = 60)]
        public static void ApplyToSelectionMenu()
        {
            var paths = new List<string>();
            foreach (Object obj in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(p)) continue;
                if (AssetDatabase.IsValidFolder(p))
                    CollectFolder(p, paths);
                else
                    paths.Add(p);
            }
            RunAndReport(paths, "Selection");
        }

        [MenuItem("EjoyFramework/Core/Resource/Import Rules/Apply to All", priority = 61)]
        public static void ApplyToAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Apply Import Rules",
                    "对全工程资产强制套用导入规则（会覆盖手动导入设置并重导入）。继续？", "Apply", "Cancel"))
                return;

            var paths = new List<string>();
            string[] guids = AssetDatabase.FindAssets(string.Empty);
            for (int i = 0; i < guids.Length; i++)
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            RunAndReport(paths, "All");
        }

        private static void CollectFolder(string folder, List<string> into)
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!AssetDatabase.IsValidFolder(p)) into.Add(p);
            }
        }

        private static void RunAndReport(List<string> paths, string scope)
        {
            AssetImportRuleConfig config = AssetImportRuleConfig.GetOrCreate();
            int applied = ApplyToPaths(paths, config);
            string msg = string.Format("Import Rules ({0}): {1} 个资产已套用并重导入。", scope, applied);
            Debug.Log("[EjoyAssetImportRules] " + msg);
            EditorUtility.DisplayDialog("Apply Import Rules", msg, "OK");
        }

        /// <summary>对一组资产路径套用规则预设并重导入。返回实际被套用（命中规则）的资产数。</summary>
        public static int ApplyToPaths(IEnumerable<string> paths, AssetImportRuleConfig config)
        {
            if (paths == null || config == null) return 0;
            var resolver = new AssetImportRuleResolver(config);

            int applied = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in paths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    AssetImporter importer = AssetImporter.GetAtPath(path);
                    if (importer == null) continue;

                    if (importer is TextureImporter tex)
                    {
                        AssetImportRule r = resolver.Resolve(path, AssetImportKind.Texture);
                        if (r != null) { AssetImportApplyOps.ApplyTexture(tex, r.Texture); tex.SaveAndReimport(); applied++; }
                    }
                    else if (importer is AudioImporter audio)
                    {
                        AssetImportRule r = resolver.Resolve(path, AssetImportKind.Audio);
                        if (r != null) { AssetImportApplyOps.ApplyAudio(audio, r.Audio); audio.SaveAndReimport(); applied++; }
                    }
                    else if (importer is ModelImporter model)
                    {
                        AssetImportRule r = resolver.Resolve(path, AssetImportKind.Model);
                        if (r != null) { AssetImportApplyOps.ApplyModel(model, r.Model); model.SaveAndReimport(); applied++; }
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
            return applied;
        }
    }
}
