//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace EjoyFramework.Core.Unity.Editor.Resource
{
    /// <summary>
    /// 路径 glob 模式匹配工具（极小实现）。
    /// 支持：
    ///   *   单段通配（不跨 /）
    ///   **  跨段通配
    ///   ?   单字符
    ///   其它字符按字面匹配
    /// 不区分大小写（资产路径在 Unity 中可能因 OS 不同表现，统一小写比对）。
    /// </summary>
    internal static class GlobMatcher
    {
        public static bool Match(string pattern, string path)
        {
            if (string.IsNullOrEmpty(pattern) || path == null) return false;
            return MatchImpl(pattern.ToLowerInvariant(), path.Replace('\\', '/').ToLowerInvariant(), 0, 0);
        }

        public static bool MatchAny(IList<string> patterns, string path)
        {
            if (patterns == null) return false;
            for (int i = 0; i < patterns.Count; i++)
            {
                if (Match(patterns[i], path)) return true;
            }
            return false;
        }

        private static bool MatchImpl(string p, string s, int pi, int si)
        {
            while (pi < p.Length)
            {
                char pc = p[pi];
                if (pc == '*')
                {
                    bool doubleStar = pi + 1 < p.Length && p[pi + 1] == '*';
                    if (doubleStar)
                    {
                        // ** 匹配任意（含 /）
                        int nextPi = pi + 2;
                        // 跳过紧跟的 /
                        if (nextPi < p.Length && p[nextPi] == '/') nextPi++;
                        if (nextPi >= p.Length) return true;
                        for (int k = si; k <= s.Length; k++)
                            if (MatchImpl(p, s, nextPi, k)) return true;
                        return false;
                    }
                    // * 匹配除 / 外任意字符
                    int nextPi2 = pi + 1;
                    if (nextPi2 >= p.Length)
                    {
                        for (int k = si; k < s.Length; k++) if (s[k] == '/') return false;
                        return true;
                    }
                    for (int k = si; k <= s.Length; k++)
                    {
                        if (k > si && s[k - 1] == '/') break;
                        if (MatchImpl(p, s, nextPi2, k)) return true;
                    }
                    return false;
                }
                if (si >= s.Length) return false;
                if (pc == '?' || pc == s[si]) { pi++; si++; continue; }
                return false;
            }
            return si == s.Length;
        }

        /// <summary>
        /// 将任意字符串规范化为 bundle 名（小写、目录分隔符 → _、保留扩展名时去掉点）。
        /// </summary>
        public static string SanitizeBundleName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "default";
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '/' || c == '\\' || c == ' ' || c == '.') sb.Append('_');
                else if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(char.ToLowerInvariant(c));
                else sb.Append('_');
            }
            // 折叠连续下划线
            string s = sb.ToString();
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.Trim('_');
        }
    }
}
