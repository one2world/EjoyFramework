//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Resource
{
    /// <summary>
    /// 资源清单。AssetBundle 模式下由打包工具产出，运行时从 ReadOnlyPath/ReadWritePath 读取。
    /// EditorSimulation 模式下从 AssetDatabase 反推构造（开发期间也可走 manifest 验证打包配置正确）。
    /// 设计原则：纯数据、Engine-agnostic、可被任意序列化器（Utility.Json / 自研 binary）序列化。
    ///
    /// 字段（非属性）：Unity JsonUtility 只能序列化字段，不能序列化属性。
    /// 历史上这里曾用 { get; set; } 属性，导致 manifest.json 永远只输出 {}，runtime 拿到空清单。
    /// 切勿把这些 public 字段改回 auto-property，除非整套切换到非 Unity 的 JSON 序列化器。
    /// </summary>
    [Serializable]
    public sealed class AssetManifest
    {
        /// <summary>
        /// Manifest 版本号；不兼容变更时递增。
        /// </summary>
        public int Version;

        /// <summary>
        /// 应用语义版本（与构建对齐），用于热更比对。
        /// </summary>
        public string AppVersion;

        /// <summary>
        /// 打包目标平台（Android/iOS/StandaloneWindows64 等），运行时校验。
        /// </summary>
        public string Platform;

        /// <summary>
        /// 所有 bundle 信息，按 BundleName 索引。
        /// </summary>
        public List<BundleInfo> Bundles = new List<BundleInfo>();

        /// <summary>
        /// 所有 asset 信息，按 AssetName 索引。
        /// </summary>
        public List<AssetInfo> Assets = new List<AssetInfo>();
    }

    /// <summary>
    /// AssetBundle 元数据。
    /// </summary>
    [Serializable]
    public sealed class BundleInfo
    {
        /// <summary>
        /// Bundle 名称（含扩展名，与磁盘文件一致）。
        /// </summary>
        public string Name;

        /// <summary>
        /// 文件相对路径（相对 ReadOnlyPath/ReadWritePath）。
        /// </summary>
        public string RelativePath;

        /// <summary>
        /// 文件字节大小，用于显示进度与磁盘配额。
        /// </summary>
        public long Size;

        /// <summary>
        /// CRC32 校验码，用于热更包完整性。
        /// </summary>
        public uint Crc;

        /// <summary>
        /// Hash（Unity BuildPipeline 给出的 AssetBundleHash），版本对比用。
        /// </summary>
        public string Hash;

        /// <summary>
        /// 文件内容 MD5（十六进制小写），热更下载后完整性校验用。
        /// 注意：这是 .ab 文件字节的真实 MD5，区别于 <see cref="Hash"/>（Unity AssetBundleHash）。
        /// 安全要求：构建生成的 manifest 必须填充 Md5 与 Size。PatchManager 默认 m_RequireIntegrity=true，
        /// 会拒绝缺失完整性元数据的条目（防止经篡改清单将其置空以关闭校验）；仅受控测试可显式放行。
        /// </summary>
        public string Md5;

        /// <summary>
        /// 依赖 bundle 名列表（递归依赖由 Loader 解析）。
        /// </summary>
        public List<string> Dependencies = new List<string>();

        /// <summary>
        /// 该 bundle 中包含的资源逻辑名（用于反查；不含路径，等于 AssetInfo.Name）。
        /// </summary>
        public List<string> AssetNames = new List<string>();
    }

    /// <summary>
    /// 单个资源的元数据。
    /// </summary>
    [Serializable]
    public sealed class AssetInfo
    {
        /// <summary>
        /// 资源逻辑名（业务方传入的 assetName，例如 "Assets/UI/MainMenu.prefab"）。
        /// </summary>
        public string Name;

        /// <summary>
        /// 所在 bundle 名。
        /// </summary>
        public string BundleName;

        /// <summary>
        /// 是否为场景资产（影响 LoadScene 路径）。
        /// </summary>
        public bool IsScene;

        /// <summary>
        /// 主类型全名（可空，仅用于调试/校验）。
        /// </summary>
        public string TypeName;

        /// <summary>
        /// 变体名（若使用 AB 变体，例如 hd/sd）。
        /// </summary>
        public string Variant;
    }
}
