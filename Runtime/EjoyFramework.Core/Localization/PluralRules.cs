//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Localization
{
    /// <summary>
    /// 简化版 CLDR Plural Rules（不依赖 ICU）。当前 Language enum 已实现的规则：
    ///   en: one / other
    ///   zh / ja / ko: other (无单复数)
    ///   ru: one / few / many（整数遵循 CLDR 俄语规则；非整数 fallback 至 other）
    ///
    /// PluralForm 预留了 zero / two，供后续扩展 Language enum（如阿拉伯等多形式语言）时复用；
    /// 新增语言时在 <see cref="Get"/> 的 switch 中补对应 case 即可。few / many 已由
    /// <see cref="FormSuffix"/>（以及 <see cref="MakeKey"/>）映射对应后缀。
    ///
    /// 业务侧 key 命名约定：基础 key + "." + form（如 "apple.one" / "apple.few" / "apple.many"）。
    /// </summary>
    public enum PluralForm
    {
        Other,
        One,
        Few,
        Many,
        Zero,
        Two,
    }

    public static class PluralRules
    {
        public static PluralForm Get(Language lang, int count)
        {
            switch (lang)
            {
                case Language.English:
                    return count == 1 ? PluralForm.One : PluralForm.Other;

                // 俄语：CLDR 整数复数规则（n 取计数的绝对值，整数恒映射 one/few/many）
                case Language.Russian:
                {
                    int n = count < 0 ? -count : count;
                    int mod10 = n % 10;
                    int mod100 = n % 100;
                    if (mod10 == 1 && mod100 != 11)
                    {
                        return PluralForm.One;
                    }
                    if (mod10 >= 2 && mod10 <= 4 && !(mod100 >= 12 && mod100 <= 14))
                    {
                        return PluralForm.Few;
                    }
                    return PluralForm.Many;
                }

                // CJK / 韩语：单一形式（业务方如需增加阿拉伯等复杂语言，扩展 Language enum + 加 case）
                case Language.ChineseSimplified:
                case Language.ChineseTraditional:
                case Language.Japanese:
                case Language.Korean:
                default:
                    return PluralForm.Other;
            }
        }

        /// <summary>把基础 key 与 form 组合成查表 key（基础.form）。</summary>
        public static string MakeKey(string baseKey, PluralForm form)
        {
            return baseKey + "." + FormSuffix(form);
        }

        private static string FormSuffix(PluralForm f)
        {
            switch (f)
            {
                case PluralForm.One: return "one";
                case PluralForm.Few: return "few";
                case PluralForm.Many: return "many";
                case PluralForm.Zero: return "zero";
                case PluralForm.Two: return "two";
                case PluralForm.Other:
                default: return "other";
            }
        }
    }
}
