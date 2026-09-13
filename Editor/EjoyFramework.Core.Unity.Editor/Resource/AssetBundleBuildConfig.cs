//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Resource
{
    /// <summary>
    /// AssetBundle 打包配置（ScriptableObject）。
    /// 通过 Tools → EjoyFramework.Core → Build AssetBundles 调用 AssetBundleBuilder。
    ///
    /// Bundle 划分策略：
    ///   - PerDirectory: 整个目录一个 bundle（默认/最常用）
    ///   - PerFile:      每个资产一个 bundle（场景、超大 asset 推荐）
    ///   - SizeLimit:    目录内累积超过阈值切割（适合贴图/音频集合）
    ///   - SingleBundle: 规则下所有资产并入指定 bundle 名（atlas / shared 资产）
    ///
    /// 规则可以叠加：BuildRules 按 Priority 降序匹配，每个资产命中首条规则。
    /// 未命中任何规则时使用 DefaultStrategy。
    /// </summary>
    [CreateAssetMenu(fileName = "AssetBundleBuildConfig", menuName = "EjoyFramework/Core/AssetBundle Build Config")]
    public sealed class AssetBundleBuildConfig : ScriptableObject
    {
        [Header("输出")]
        [Tooltip("Bundle 文件输出目录（相对 Project 根，会拼接 <platform>/ 子目录）")]
        public string OutputDirectory = "AssetBundles";

        [Tooltip("首包资源拷贝到 StreamingAssets/AssetBundles/<platform>/ 下；移动端首次启动会展开到 persistentDataPath，Standalone 构建后会复制到 dataPath")]
        public bool CopyToStreamingAssets = true;

        [Header("打包选项")]
        public AssetBundleCompressionMode Compression = AssetBundleCompressionMode.LZ4;

        [Tooltip("产物文件名是否使用 Hash（生产推荐 false，热更对比用 Hash 字段而非文件名）")]
        public bool UseHashAsFileName = false;

        [Tooltip("是否在构建前清空输出目录")]
        public bool CleanBeforeBuild = true;

        [Header("应用版本（写入 manifest，运行时校验）")]
        public string AppVersion = "1.0.0";

        [Header("默认策略（未命中任何规则时使用）")]
        public BundleSplitStrategy DefaultStrategy = BundleSplitStrategy.PerDirectory;

        [Tooltip("SizeLimit 策略下，单个 bundle 累积资产字节阈值（单位：字节，默认 4MB）")]
        public long SizeLimitBytes = 4 * 1024 * 1024;

        [Header("打包规则（按 Priority 降序匹配）")]
        public List<BundleBuildRule> BuildRules = new List<BundleBuildRule>();

        [Header("打包过滤")]
        [Tooltip("仅打包这些根目录下的资产（相对 Assets/，例如 'GameMain/UI'）")]
        public List<string> IncludeDirectories = new List<string>();

        [Tooltip("打包时跳过的资产路径模式（glob，例如 '**/Editor/**'）")]
        public List<string> ExcludePatterns = new List<string> { "**/Editor/**", "**/Tests/**" };

        [Tooltip("跳过这些扩展名（小写）")]
        public List<string> ExcludeExtensions = new List<string> { ".cs", ".meta", ".asmdef", ".asmref" };
    }

    /// <summary>
    /// Bundle 划分策略。
    /// </summary>
    public enum BundleSplitStrategy
    {
        /// <summary>整个目录合并为一个 bundle。</summary>
        PerDirectory = 0,
        /// <summary>每个资产单独一个 bundle。</summary>
        PerFile = 1,
        /// <summary>按大小阈值切割（同目录内累积超过 SizeLimitBytes 切割）。</summary>
        SizeLimit = 2,
        /// <summary>规则匹配的所有资产并入 SingleBundleName 指定的 bundle。</summary>
        SingleBundle = 3,
    }

    /// <summary>
    /// AssetBundle 压缩模式。
    /// </summary>
    public enum AssetBundleCompressionMode
    {
        /// <summary>无压缩，加载最快、包最大。</summary>
        Uncompressed = 0,
        /// <summary>LZ4，加载快、压缩比中等（推荐运行时）。</summary>
        LZ4 = 1,
        /// <summary>LZMA，压缩比最高、加载慢（推荐热更下载包，运行时再转 LZ4）。</summary>
        LZMA = 2,
    }

    /// <summary>
    /// 单条打包规则。
    /// </summary>
    [Serializable]
    public sealed class BundleBuildRule
    {
        [Tooltip("规则名（仅用于显示）")]
        public string Name = "Rule";

        [Tooltip("路径 glob 模式，相对 Assets/。例如 'GameMain/UI/**/*.prefab'。多模式 OR。")]
        public List<string> PathPatterns = new List<string>();

        [Tooltip("数值越大越优先匹配")]
        public int Priority = 0;

        [Tooltip("匹配后采用的划分策略")]
        public BundleSplitStrategy Strategy = BundleSplitStrategy.PerDirectory;

        [Tooltip("SingleBundle 策略下使用的 bundle 名")]
        public string SingleBundleName;

        [Tooltip("SizeLimit 策略下覆盖全局阈值（0 表示用全局）")]
        public long OverrideSizeLimitBytes;
    }
}
