//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Unity.Editor.Resource;   // GlobMatcher

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 命名与目录约定检查：
    ///   • 文件名含空格/非 ASCII（影响打包名规范化、跨平台路径）→ Warning。
    ///   • Art 路径下贴图须有 cg_ 前缀（项目美术契约）→ Warning。
    ///   • Forms 路径下顶层 prefab 须以 Form 结尾（Items 子目录除外）→ Warning。
    /// </summary>
    public sealed class NamingConventionRule : IAssetCheckRule
    {
        public string Id => "naming";
        public string DisplayName => "命名与目录约定";
        public AssetCheckCategory Category => AssetCheckCategory.Naming;

        private static readonly string[] TextureExt = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr" };

        public IEnumerable<AssetIssue> Check(AssetCheckContext context, AssetCheckConfig config)
        {
            var issues = new List<AssetIssue>();

            foreach (string path in context.Candidates)
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();

                // 1) 空格 / 非 ASCII
                if (config.FlagNonAsciiOrSpace && HasSpaceOrNonAscii(fileName))
                {
                    issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path,
                        "文件名含空格或非 ASCII 字符：'" + fileName + "'，建议改为 [a-z0-9_-]。"));
                }

                // 2) Art 下贴图 cg_ 前缀
                if (!string.IsNullOrEmpty(config.CgPrefixPathPattern)
                    && IsTexture(ext)
                    && GlobMatcher.Match(config.CgPrefixPathPattern, path)
                    && !fileName.StartsWith("cg_"))
                {
                    issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path,
                        "Art 贴图缺少 'cg_' 前缀：'" + fileName + "'。"));
                }

                // 3) Forms 顶层 prefab 须 *Form（Items/ 子目录除外）
                if (!string.IsNullOrEmpty(config.FormSuffixPathPattern)
                    && ext == ".prefab"
                    && GlobMatcher.Match(config.FormSuffixPathPattern, path)
                    && !path.Replace('\\', '/').Contains("/Items/")
                    && !fileName.EndsWith("Form"))
                {
                    issues.Add(AssetIssue.Make(AssetCheckSeverity.Warning, Category, Id, path,
                        "顶层窗体 prefab 命名应以 'Form' 结尾：'" + fileName + "'。"));
                }
            }

            return issues;
        }

        // ---- 纯逻辑辅助（供测试直接调用）----

        internal static bool HasSpaceOrNonAscii(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c > 127) return true;
            }
            return false;
        }

        internal static bool IsTexture(string lowerExt)
        {
            for (int i = 0; i < TextureExt.Length; i++)
                if (TextureExt[i] == lowerExt) return true;
            return false;
        }
    }
}
