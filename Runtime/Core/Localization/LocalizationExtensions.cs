//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Text;

namespace EjoyFramework.Core.Localization
{
    /// <summary>
    /// LocalizationManager 占位符 + 复数变化扩展（不修改原接口）。
    ///
    /// 占位符语法：
    ///   - {0}, {1}, ...   按 args 顺序索引
    ///   - {name}, {hp}    按命名 KeyValuePair 替换（需调 GetStringNamed）
    ///
    /// 复数语法：业务存表 key 时按 "key.one" / "key.other" 形式存。GetPlural 根据当前 Language + count 选择正确 form。
    /// </summary>
    public static class LocalizationExtensions
    {
        /// <summary>
        /// 按位置占位符替换：text.Format("{0} kills {1}", "Alice", "Bob") → "Alice kills Bob".
        /// </summary>
        public static string Format(this ILocalizationManager loc, string key, params object[] args)
        {
            if (loc == null) return key;
            string template = loc.GetRawString(key);
            if (string.IsNullOrEmpty(template)) return key;
            if (args == null || args.Length == 0) return template;
            return SafeFormat(template, args);
        }

        /// <summary>
        /// 按命名占位符替换：text.FormatNamed("hello", ("name","Alice")) → "Hello, Alice".
        /// </summary>
        public static string FormatNamed(this ILocalizationManager loc, string key, params (string Name, object Value)[] pairs)
        {
            if (loc == null) return key;
            string template = loc.GetRawString(key);
            if (string.IsNullOrEmpty(template)) return key;
            if (pairs == null || pairs.Length == 0) return template;
            var sb = new StringBuilder(template);
            foreach (var (name, value) in pairs)
            {
                sb.Replace("{" + name + "}", value != null ? value.ToString() : "");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 复数：基于 Language + count 自动选择 key.one / key.few / key.many / key.other。
        /// 占位符 {0} 自动 = count（业务也可在自己模板里写 {0}）。
        /// </summary>
        public static string GetPlural(this ILocalizationManager loc, string baseKey, int count)
        {
            if (loc == null || baseKey == null) return baseKey;
            string template = ResolvePluralTemplate(loc, baseKey, count);
            if (template == null) return baseKey;
            return SafeFormat(template, new object[] { count });
        }

        // ================= 零分配格式化（UI 热路径） =================
        // 写进调用方的 TempText，值类型参数不装箱。模板非法（翻译稿常见）时原样追加模板、返回 false，不抛异常；
        // key 缺失时追加 GetString 的 "<NoKey>key" 回退文本（并按 key 去重告警一次）、返回 false。

        /// <summary>追加本地化文本。</summary>
        public static bool AppendString(this ILocalizationManager loc, TempText dest, string key)
        {
            string template;
            if (!TryGetTemplate(loc, dest, key, out template)) return false;
            dest.Append(template);
            return true;
        }

        /// <summary>按位置占位符格式化后追加（1 个参数）。</summary>
        public static bool AppendFormat<T0>(this ILocalizationManager loc, TempText dest, string key, T0 arg0)
        {
            string template;
            if (!TryGetTemplate(loc, dest, key, out template)) return false;
            return dest.TryAppendFormat(template, arg0) || AppendRaw(dest, template);
        }

        /// <summary>按位置占位符格式化后追加（2 个参数）。</summary>
        public static bool AppendFormat<T0, T1>(this ILocalizationManager loc, TempText dest, string key, T0 arg0, T1 arg1)
        {
            string template;
            if (!TryGetTemplate(loc, dest, key, out template)) return false;
            return dest.TryAppendFormat(template, arg0, arg1) || AppendRaw(dest, template);
        }

        /// <summary>按位置占位符格式化后追加（3 个参数）。</summary>
        public static bool AppendFormat<T0, T1, T2>(this ILocalizationManager loc, TempText dest, string key, T0 arg0, T1 arg1, T2 arg2)
        {
            string template;
            if (!TryGetTemplate(loc, dest, key, out template)) return false;
            return dest.TryAppendFormat(template, arg0, arg1, arg2) || AppendRaw(dest, template);
        }

        /// <summary>按位置占位符格式化后追加（4 个参数）。</summary>
        public static bool AppendFormat<T0, T1, T2, T3>(this ILocalizationManager loc, TempText dest, string key, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            string template;
            if (!TryGetTemplate(loc, dest, key, out template)) return false;
            return dest.TryAppendFormat(template, arg0, arg1, arg2, arg3) || AppendRaw(dest, template);
        }

        /// <summary>
        /// 复数形式追加（规则同 <see cref="GetPlural"/>，{0} = count），复数键在栈缓冲里拼出后按切片查表，零分配。
        /// 所有候选键都缺失时追加 baseKey 并返回 false。
        /// </summary>
        public static bool AppendPlural(this ILocalizationManager loc, TempText dest, string baseKey, int count)
        {
            if (baseKey == null) return false;
            string template = loc != null ? ResolvePluralTemplate(loc, baseKey, count) : null;
            if (template == null)
            {
                dest.Append(baseKey);
                return false;
            }
            return dest.TryAppendFormat(template, count) || AppendRaw(dest, template);
        }

        private static bool TryGetTemplate(ILocalizationManager loc, TempText dest, string key, out string template)
        {
            template = loc != null && key != null ? loc.GetRawString(key) : null;
            if (template != null) return true;
            dest.Append(loc != null ? loc.GetString(key) : key);   // 缺失：沿用 GetString 的回退文本与一次性告警
            return false;
        }

        private static bool AppendRaw(TempText dest, string template)
        {
            dest.Append(template);
            return false;
        }

        /// <summary>依次尝试 baseKey.form、baseKey.other、baseKey（空模板视为缺失），都没有返回 null。</summary>
        private static string ResolvePluralTemplate(ILocalizationManager loc, string baseKey, int count)
        {
            PluralForm form = PluralRules.Get(loc.Language, count);
            string template = FindSuffixed(loc, baseKey, PluralRules.FormSuffix(form));
            if (string.IsNullOrEmpty(template) && form != PluralForm.Other)
            {
                template = FindSuffixed(loc, baseKey, PluralRules.FormSuffix(PluralForm.Other));
            }
            if (string.IsNullOrEmpty(template)) template = loc.GetRawString(baseKey);
            return string.IsNullOrEmpty(template) ? null : template;
        }

        private static string FindSuffixed(ILocalizationManager loc, string baseKey, string suffix)
        {
            int length = baseKey.Length + 1 + suffix.Length;
            char[] rented = null;
            Span<char> key = length <= 256 ? stackalloc char[256] : (Span<char>)(rented = CharBufferPool.Rent(length));
            try
            {
                baseKey.AsSpan().CopyTo(key);
                key[baseKey.Length] = '.';
                suffix.AsSpan().CopyTo(key.Slice(baseKey.Length + 1));
                string value;
                return loc.TryGetRawString(key.Slice(0, length), out value) ? value : null;
            }
            finally
            {
                if (rented != null) CharBufferPool.Return(rented);
            }
        }

        /// <summary>检查 key 是否存在。优先 raw key，其次复数 .other 形式。</summary>
        public static bool HasLocalized(this ILocalizationManager loc, string key)
        {
            return loc != null && (loc.HasRawString(key) || loc.HasRawString(key + ".other"));
        }

        private static string SafeFormat(string template, object[] args)
        {
            try { return string.Format(template, args); }
            catch (FormatException) { return template; }   // 格式错误回退原 template 不崩
        }
    }
}
