//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using SysPath = System.IO.Path;

namespace EjoyFramework.Core
{
    public static partial class Utility
    {
        /// <summary>
        /// 路径安全工具。用于在“清单（manifest）声明的相对路径”落地为文件系统路径 / URL 之前做白名单校验，
        /// 防止远程清单通过 <c>../../</c>、绝对路径、盘符或保留字符逃逸出预期的根目录（路径遍历 / Zip-Slip 类攻击）。
        ///
        /// 设计原则：
        ///   - 纯 C#、Engine-agnostic，放在 Core 程序集，<see cref="EjoyFramework"/>.Core.Unity 等上层可直接复用。
        ///   - 只做“拒绝可疑输入”的廉价语法校验（<see cref="IsSafeRelativePath"/>），
        ///     再配合 <see cref="IsContainedIn"/> 做一次基于 <c>Path.GetFullPath</c> 的“规范化后仍在根目录内”兜底，
        ///     双保险（belt-and-braces）：语法层挡掉绝大多数，规范化层兜住平台差异 / 符号链接等遗漏。
        /// </summary>
        public static class Path
        {
            /// <summary>
            /// 判断一个清单提供的相对路径是否“安全”（不会逃逸根目录）。
            /// 拒绝以下输入：
            ///   - null 或空字符串；
            ///   - 含有 NUL('\0') 或冒号(':')（盘符 / NTFS 备用数据流）；
            ///   - 以 '/' 或 '\\' 开头（绝对/根路径）；
            ///   - 归一化 '\\'→'/' 后，任一路径段为 ""（连续分隔符）、"."（当前目录）或 ".."（上级目录）。
            /// </summary>
            /// <param name="rel">清单声明的相对路径（如 BundleInfo.RelativePath）。</param>
            /// <returns>安全返回 true；任一可疑条件命中返回 false。</returns>
            public static bool IsSafeRelativePath(string rel)
            {
                if (string.IsNullOrEmpty(rel)) return false;

                // NUL 截断 / 冒号（盘符 C:、NTFS ADS file:stream）一律拒绝。
                if (rel.IndexOf('\0') >= 0) return false;
                if (rel.IndexOf(':') >= 0) return false;

                // 绝对路径 / 根路径（/foo、\foo、//server）一律拒绝。
                if (rel[0] == '/' || rel[0] == '\\') return false;

                // 归一化分隔符后逐段检查，挡住 ""（连续分隔符）/ "."（当前目录）/ ".."（上级目录）。
                string normalized = rel.Replace('\\', '/');
                string[] segments = normalized.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    string seg = segments[i];
                    if (seg.Length == 0) return false;   // "a//b" 或结尾 "/"
                    if (seg == ".") return false;
                    if (seg == "..") return false;
                }
                return true;
            }

            /// <summary>
            /// 校验 <paramref name="combined"/> 规范化后是否仍位于 <paramref name="root"/> 之内（含等于 root 本身）。
            /// 在 <see cref="IsSafeRelativePath"/> 之后做的兜底校验：即便语法校验有遗漏，
            /// 只要规范化路径越界即拒绝。比较使用序数（Ordinal），并确保 root 以目录分隔符结尾以避免
            /// 前缀误判（例如 <c>/data/app</c> 不应被视为包含 <c>/data/app-evil</c>）。
            /// </summary>
            /// <param name="root">允许的根目录。</param>
            /// <param name="combined">由 root 与相对路径组合得到的目标路径。</param>
            /// <returns>combined 在 root 之内返回 true；越界或参数非法返回 false。</returns>
            public static bool IsContainedIn(string root, string combined)
            {
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(combined)) return false;

                try
                {
                    string fullRoot = SysPath.GetFullPath(root);
                    string fullCombined = SysPath.GetFullPath(combined);

                    // 确保 root 以分隔符结尾，避免 "/data/app" 误判包含 "/data/app-evil"。
                    char sep = SysPath.DirectorySeparatorChar;
                    if (fullRoot.Length == 0) return false;
                    if (fullRoot[fullRoot.Length - 1] != sep && fullRoot[fullRoot.Length - 1] != SysPath.AltDirectorySeparatorChar)
                        fullRoot += sep;

                    // 等于 root 本身（去掉尾分隔符后相等）也视为包含。
                    string fullRootNoSep = fullRoot.Substring(0, fullRoot.Length - 1);
                    if (string.Equals(fullCombined, fullRootNoSep, StringComparison.Ordinal)) return true;

                    return fullCombined.StartsWith(fullRoot, StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    // GetFullPath 在非法字符 / 过长路径时抛异常 —— 视为不安全（拒绝）。
                    FrameworkLog.Warning("Utility.Path.IsContainedIn threw for root='{0}', combined='{1}': {2}", root, combined, ex.Message);
                    return false;
                }
            }
        }
    }
}
