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
            if (loc == null) return baseKey;
            var form = PluralRules.Get(loc.Language, count);
            string keyed = PluralRules.MakeKey(baseKey, form);
            string template = loc.GetRawString(keyed);
            if (string.IsNullOrEmpty(template))
            {
                // 找不到该 form 的 key，回退到 .other 或 baseKey 本身
                template = loc.GetRawString(PluralRules.MakeKey(baseKey, PluralForm.Other));
                if (string.IsNullOrEmpty(template)) template = loc.GetRawString(baseKey);
                if (string.IsNullOrEmpty(template)) return baseKey;
            }
            return SafeFormat(template, new object[] { count });
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
