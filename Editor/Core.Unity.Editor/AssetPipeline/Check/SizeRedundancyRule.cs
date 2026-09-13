//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 体积与冗余检查：
    ///   • 贴图 maxTextureSize 超阈值 → Warning。
    ///   • prefab 文件字节超阈值 → Warning。
    ///   • 内容完全重复（文件字节 hash 相同）→ Warning（仅在贴图/音频等二进制资产间比对，限定开销）。
    ///   • 孤儿资产（无人引用，且非场景/根类型）→ Info。
    /// </summary>
    public sealed class SizeRedundancyRule : IAssetCheckRule
    {
        public string Id => "size";
        public string DisplayName => "体积与冗余";
        public AssetCheckCategory Category => AssetCheckCategory.Size;

        private static readonly string[] HashableExt = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".wav", ".mp3", ".ogg" };
        private static readonly string[] RootExt = { ".unity", ".asset", ".spriteatlas" };  // 视为引用根，不报孤儿

        public IEnumerable<AssetIssue> Check(AssetCheckContext context, AssetCheckConfig config)
        {
            var issues = new List<AssetIssue>();
            var hashGroups = config.DetectDuplicates ? new Dictionary<string, List<string>>() : null;

            foreach (string path in context.Candidates)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();

                // 贴图 maxSize
                if (IsTextureExt(ext))
                {
                    if (AssetImporter.GetAtPath(path) is TextureImporter tex && tex.maxTextureSize > config.MaxTextureSize)
                    {
                        issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path,
                            "贴图 maxTextureSize=" + tex.maxTextureSize + " 超过阈值 " + config.MaxTextureSize + "。"));
                    }
                }

                // prefab 文件体积
                if (ext == ".prefab")
                {
                    long bytes = GetFileBytes(path);
                    if (bytes > config.MaxPrefabBytes)
                    {
                        issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path,
                            "prefab 体积 " + (bytes / 1024) + "KB 超过阈值 " + (config.MaxPrefabBytes / 1024) + "KB。"));
                    }
                }

                // 重复内容分组
                if (hashGroups != null && IsHashable(ext))
                {
                    string hash = ComputeFileHash(path);
                    if (hash != null)
                    {
                        if (!hashGroups.TryGetValue(hash, out var list)) { list = new List<string>(); hashGroups[hash] = list; }
                        list.Add(path);
                    }
                }

                // 孤儿检测
                if (config.DetectOrphans && !IsRootExt(ext) && !context.IsReferencedByAnyone(path))
                {
                    issues.Add(AssetIssue.Make(AssetCheckSeverity.Info, Category, Id, path,
                        "孤儿资产：未被任何资产引用（若为有意保留可忽略）。"));
                }
            }

            // 汇总重复组
            if (hashGroups != null)
            {
                foreach (var kv in hashGroups)
                {
                    if (kv.Value.Count <= 1) continue;
                    string others = string.Join(", ", kv.Value.GetRange(1, kv.Value.Count - 1).ToArray());
                    issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, kv.Value[0],
                        "内容重复（" + kv.Value.Count + " 份相同字节）：与 " + others + " 完全一致。"));
                }
            }

            return issues;
        }

        private static long GetFileBytes(string assetPath)
        {
            try { return new FileInfo(assetPath).Length; }
            catch { return 0L; }
        }

        private static string ComputeFileHash(string assetPath)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(assetPath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return System.BitConverter.ToString(hash);
                }
            }
            catch { return null; }
        }

        private static bool IsTextureExt(string ext)
        {
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".exr";
        }

        private static bool IsHashable(string ext)
        {
            for (int i = 0; i < HashableExt.Length; i++) if (HashableExt[i] == ext) return true;
            return false;
        }

        private static bool IsRootExt(string ext)
        {
            for (int i = 0; i < RootExt.Length; i++) if (RootExt[i] == ext) return true;
            return false;
        }
    }
}
