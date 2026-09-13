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
    /// 静态资源检查配置（ScriptableObject）。控制扫描范围、各规则开关与阈值、构建门禁。
    /// 规范实例落 <see cref="DefaultAssetPath"/>。
    /// </summary>
    [CreateAssetMenu(fileName = "EjoyAssetCheckConfig", menuName = "EjoyFramework/Core/Asset Check Config")]
    public sealed class AssetCheckConfig : ScriptableObject
    {
        public const string DefaultAssetPath = "Assets/Editor/EjoyAssetCheckConfig.asset";

        [Header("扫描范围")]
        [Tooltip("仅检查这些根目录（相对工程根，如 'Assets/GameMain'）。空 = 整个 Assets/。")]
        public List<string> IncludeDirectories = new List<string> { "Assets/GameMain" };

        [Tooltip("跳过的路径模式（glob）")]
        public List<string> ExcludePatterns = new List<string>
        {
            "Assets/Screenshots/**",
            "**/Editor/**",
            "**/Tests/**",
        };

        [Header("规则开关")]
        public bool CheckImportSettings = true;
        public bool CheckNaming = true;
        public bool CheckReferences = true;
        public bool CheckSizeRedundancy = true;

        [Header("体积阈值")]
        [Tooltip("贴图 maxTextureSize 超过此值告警")]
        public int MaxTextureSize = 2048;
        [Tooltip("prefab 文件字节超过此值告警（默认 1MB）")]
        public long MaxPrefabBytes = 1024 * 1024;
        [Tooltip("检测内容完全重复的资产（按文件字节 hash 分组）")]
        public bool DetectDuplicates = true;
        [Tooltip("检测无人引用的孤儿资产（Info 级，常为有意保留）")]
        public bool DetectOrphans = true;

        [Header("命名约定")]
        [Tooltip("文件名含空格/非 ASCII 字符时告警")]
        public bool FlagNonAsciiOrSpace = true;
        [Tooltip("该路径下贴图须有 cg_ 前缀（留空关闭）")]
        public string CgPrefixPathPattern = "Assets/GameMain/UI/Art/**";
        [Tooltip("该路径下顶层 prefab 须以 Form 结尾（Items 子目录除外；留空关闭）")]
        public string FormSuffixPathPattern = "Assets/GameMain/UI/Forms/**";

        [Header("构建门禁")]
        [Tooltip("构建前检查发现 Error 级问题时阻断构建")]
        public bool FailBuildOnError = true;

        private static AssetCheckConfig s_Instance;

        public static AssetCheckConfig GetOrCreate()
        {
            if (s_Instance != null) return s_Instance;

            s_Instance = AssetDatabase.LoadAssetAtPath<AssetCheckConfig>(DefaultAssetPath);
            if (s_Instance != null) return s_Instance;

            var guids = AssetDatabase.FindAssets("t:AssetCheckConfig");
            if (guids != null && guids.Length > 0)
            {
                s_Instance = AssetDatabase.LoadAssetAtPath<AssetCheckConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (s_Instance != null) return s_Instance;
            }

            string dir = Path.GetDirectoryName(DefaultAssetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            s_Instance = CreateInstance<AssetCheckConfig>();
            AssetDatabase.CreateAsset(s_Instance, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[EjoyAssetCheck] Created default check config at " + DefaultAssetPath);
            return s_Instance;
        }

        /// <summary>不触发创建的只读查找。</summary>
        public static AssetCheckConfig FindExisting()
        {
            if (s_Instance != null) return s_Instance;
            s_Instance = AssetDatabase.LoadAssetAtPath<AssetCheckConfig>(DefaultAssetPath);
            if (s_Instance != null) return s_Instance;
            var guids = AssetDatabase.FindAssets("t:AssetCheckConfig");
            if (guids != null && guids.Length > 0)
                s_Instance = AssetDatabase.LoadAssetAtPath<AssetCheckConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            return s_Instance;
        }
    }
}
