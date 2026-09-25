//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// 放在编辑器层根命名空间：不新建 "...Editor.Text" 命名空间，以免它在兄弟命名空间里遮蔽 UnityEngine.UI.Text。
namespace EjoyFramework.Core.Unity.Editor
{
    /// <summary>
    /// StringHash 编辑期碰撞体检：扫描源码里的 <c>StringHash.Of("字面量")</c>（默认 Assets/ 与框架包 Runtime/），
    /// 连同运行期登记表已记录的碰撞一起报告。
    ///
    /// 为什么需要：32 位哈希在键多时会碰撞（1 万个键约 1.2%）。运行期登记表只在代码实际执行到 StringHash.Of 时才发现碰撞，
    /// 冷门分支里的键可能上线后才撞上；本工具在提交前一次性检查全部字面量键。
    /// 只识别字面量参数（普通与逐字字符串）；拼接、变量、插值字符串无法静态提取，它们仍由运行期登记表兜底。
    /// 框架包的 Tests/ 不在扫描范围内（其中有故意构造的碰撞对）。
    /// </summary>
    public static class StringHashValidator
    {
        private static readonly Regex s_RegularLiteral = new Regex(
            @"StringHash\s*\.\s*Of\s*\(\s*""((?:[^""\\\r\n]|\\.)*)""\s*\)", RegexOptions.Compiled);

        private static readonly Regex s_VerbatimLiteral = new Regex(
            @"StringHash\s*\.\s*Of\s*\(\s*@""((?:[^""]|"""")*)""\s*\)", RegexOptions.Compiled);

        [MenuItem("EjoyFramework/Core/Text/Validate StringHash Collisions")]
        private static void ValidateMenu()
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            int files = ScanDirectory("Assets", names);
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(StringHash).Assembly);
            if (package != null)
            {
                files += ScanDirectory(Path.Combine(package.resolvedPath, "Runtime"), names);
            }

            var collisions = new List<StringHashCollision>();
            StringHashRegistry.FindCollisions(names.Keys, collisions);
            int fromSource = collisions.Count;
            StringHashRegistry.GetCollisions(collisions);

            if (collisions.Count == 0)
            {
                Debug.Log("[StringHash] " + files + " files, " + names.Count + " literal keys: no collisions.");
                return;
            }

            for (int i = 0; i < collisions.Count; i++)
            {
                StringHashCollision c = collisions[i];
                Debug.LogError("[StringHash] " + c + (i < fromSource ? "  (" + Where(names, c.First) + " / " + Where(names, c.Second) + ")" : "  (recorded at runtime)"));
            }

            Debug.LogError("[StringHash] " + collisions.Count + " collision(s) found in " + files + " files / " + names.Count + " literal keys.");
        }

        /// <summary>
        /// 扫描目录下所有 .cs，把 StringHash.Of 的字面量参数加入 names（键 = 解码后的字符串，值 = 首次出现的 "路径:行号"）。
        /// </summary>
        /// <param name="root">目录，不存在时返回 0。</param>
        /// <param name="names">结果。</param>
        /// <returns>扫描的文件数。</returns>
        public static int ScanDirectory(string root, IDictionary<string, string> names)
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            string[] paths = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            for (int i = 0; i < paths.Length; i++)
            {
                ExtractLiterals(File.ReadAllText(paths[i], Encoding.UTF8), paths[i].Replace('\\', '/'), names);
            }

            return paths.Length;
        }

        /// <summary>
        /// 从一段源码里提取 StringHash.Of 的字面量参数（解码 C# 转义与逐字字符串的 ""）。
        /// </summary>
        /// <param name="source">源码文本。</param>
        /// <param name="location">位置前缀（通常是文件路径），用于报告。</param>
        /// <param name="names">结果：解码后的键 → 首次出现的 "位置:行号"。</param>
        public static void ExtractLiterals(string source, string location, IDictionary<string, string> names)
        {
            foreach (Match m in s_RegularLiteral.Matches(source))
            {
                Add(names, Unescape(m.Groups[1].Value), location, source, m.Index);
            }

            foreach (Match m in s_VerbatimLiteral.Matches(source))
            {
                Add(names, m.Groups[1].Value.Replace("\"\"", "\""), location, source, m.Index);
            }
        }

        private static void Add(IDictionary<string, string> names, string key, string location, string source, int index)
        {
            if (key == null || names.ContainsKey(key))
            {
                return;
            }

            int line = 1;
            for (int i = 0; i < index; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                }
            }

            names.Add(key, location + ":" + line);
        }

        private static string Where(Dictionary<string, string> names, string key)
        {
            string location;
            return names.TryGetValue(key, out location) ? location : "?";
        }

        /// <summary>解码普通字符串字面量的转义；无法识别的转义返回 null（该字面量不参与体检）。</summary>
        internal static string Unescape(string body)
        {
            if (body.IndexOf('\\') < 0)
            {
                return body;
            }

            var sb = new StringBuilder(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (++i >= body.Length)
                {
                    return null;
                }

                switch (body[i])
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    case '0': sb.Append('\0'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'u':
                        if (!AppendHex(body, ref i, 4, 4, sb)) return null;
                        break;
                    case 'U':
                        if (!AppendHex(body, ref i, 8, 8, sb)) return null;
                        break;
                    case 'x':
                        if (!AppendHex(body, ref i, 1, 4, sb)) return null;
                        break;
                    default:
                        return null;
                }
            }

            return sb.ToString();
        }

        private static bool AppendHex(string body, ref int i, int minDigits, int maxDigits, StringBuilder sb)
        {
            int start = i + 1;
            int count = 0;
            while (count < maxDigits && start + count < body.Length && Uri.IsHexDigit(body[start + count]))
            {
                count++;
            }

            if (count < minDigits)
            {
                return false;
            }

            int codePoint = int.Parse(body.Substring(start, count), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (codePoint <= 0xFFFF)
            {
                sb.Append((char)codePoint);   // 含孤立代理项：C# 允许 \uD800 这类转义
            }
            else if (codePoint <= 0x10FFFF)
            {
                sb.Append(char.ConvertFromUtf32(codePoint));
            }
            else
            {
                return false;
            }

            i = start + count - 1;
            return true;
        }
    }
}
