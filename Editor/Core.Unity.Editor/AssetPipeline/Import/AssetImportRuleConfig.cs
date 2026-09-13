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
    /// 资源导入规则配置（ScriptableObject）。规则按 Priority 降序匹配，资产命中首条。
    ///
    /// 由 <see cref="AssetImportProcessor"/>（首次导入自动套用）与
    /// <see cref="AssetImportRulesApplier"/>（菜单批量重置）共同消费。
    /// 规范实例落 <see cref="DefaultAssetPath"/>，亦可右键 Create 多份。
    /// </summary>
    [CreateAssetMenu(fileName = "EjoyAssetImportRules", menuName = "EjoyFramework/Core/Asset Import Rules")]
    public sealed class AssetImportRuleConfig : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Editor/EjoyAssetImportRules.asset";

        [Tooltip("是否在资产首次导入（无 .meta）时自动套用规则。关闭后仅菜单手动批量生效。")]
        public bool EnableAutoOnImport = true;

        [Tooltip("跳过的路径模式（glob）。这些资产不参与自动/批量导入规则。")]
        public List<string> ExcludePatterns = new List<string>
        {
            "**/Editor/**",
            "**/Tests/**",
            "Assets/Screenshots/**",
        };

        [Tooltip("导入规则（按 Priority 降序匹配，资产命中首条同类规则）")]
        public List<AssetImportRule> Rules = new List<AssetImportRule>();

        private static AssetImportRuleConfig s_Instance;

        /// <summary>规范路径 → 工程内任意实例 → 自动创建，三步定位（缓存）。</summary>
        public static AssetImportRuleConfig GetOrCreate()
        {
            if (s_Instance != null) return s_Instance;

            s_Instance = AssetDatabase.LoadAssetAtPath<AssetImportRuleConfig>(DefaultAssetPath);
            if (s_Instance != null) return s_Instance;

            var guids = AssetDatabase.FindAssets("t:AssetImportRuleConfig");
            if (guids != null && guids.Length > 0)
            {
                s_Instance = AssetDatabase.LoadAssetAtPath<AssetImportRuleConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (s_Instance != null) return s_Instance;
            }

            string dir = Path.GetDirectoryName(DefaultAssetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            s_Instance = CreateInstance<AssetImportRuleConfig>();
            AssetDatabase.CreateAsset(s_Instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[EjoyAssetImportRules] Created default import-rule config at " + DefaultAssetPath);
            return s_Instance;
        }

        /// <summary>不触发创建的只读查找（postprocessor 热路径用，避免导入期写资产）。</summary>
        public static AssetImportRuleConfig FindExisting()
        {
            if (s_Instance != null) return s_Instance;
            s_Instance = AssetDatabase.LoadAssetAtPath<AssetImportRuleConfig>(DefaultAssetPath);
            if (s_Instance != null) return s_Instance;

            var guids = AssetDatabase.FindAssets("t:AssetImportRuleConfig");
            if (guids != null && guids.Length > 0)
                s_Instance = AssetDatabase.LoadAssetAtPath<AssetImportRuleConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return s_Instance;
        }
    }
}
