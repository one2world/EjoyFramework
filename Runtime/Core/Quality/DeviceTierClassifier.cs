//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;

namespace EjoyFramework.Core.Quality
{
    /// <summary>
    /// 设备分级器：覆盖规则 → 硬件评分 → 上限规则。
    ///
    /// 硬件规格对 GPU 实际性能只是粗略代理，所以分级只决定"起点档位"，运行期由 <see cref="AutoQualityController"/> 按真实帧时间纠偏；
    /// 线上遥测（<c>TelemetryKind.Tier</c> 带评分）回收后，把表现偏离的机型写成覆盖规则下发即可校准。
    ///
    /// 评分（0..1）= Σ 权重 × clamp01(实际 / 参考)：CPU 核数 0.15、主频 0.15、系统内存 0.30、显存 0.25、着色器级别 0.15
    /// （着色器级别 35→0、50→1）。移动与桌面使用不同的内存参考值与档位阈值。
    ///
    /// 规则文本（逐行，# 注释，大小写不敏感，出错抛带行号的异常）：
    /// <code>
    /// override gpu "Adreno (TM) 740" = Ultra      # 字段 model / gpu / cpu / os，子串匹配，先写先得
    /// override model "iPhone10," = Low
    /// cap memory &lt; 3072 = Low                     # 指标 memory / vram / cores / freq / shader，只约束评分结果
    /// thresholds mobile 0.40 0.55 0.70 0.85       # Low / Medium / High / Ultra 的最低分
    /// thresholds desktop 0.35 0.50 0.65 0.85
    /// </code>
    /// </summary>
    public sealed class DeviceTierClassifier
    {
        public enum Field { Model, Gpu, Cpu, Os }

        public enum Metric { Memory, Vram, Cores, Freq, Shader }

        private struct OverrideRule
        {
            public Field Field;
            public string Pattern;
            public DeviceTier Tier;
            public int Line;
        }

        private struct CapRule
        {
            public Metric Metric;
            public int LessThan;
            public DeviceTier MaxTier;
            public int Line;
        }

        private readonly List<OverrideRule> m_Overrides = new List<OverrideRule>();
        private readonly List<CapRule> m_Caps = new List<CapRule>();
        private readonly float[] m_MobileThresholds = { 0.40f, 0.55f, 0.70f, 0.85f };
        private readonly float[] m_DesktopThresholds = { 0.35f, 0.50f, 0.65f, 0.85f };

        public int OverrideCount { get { return m_Overrides.Count; } }
        public int CapCount { get { return m_Caps.Count; } }

        public void AddOverride(Field field, string pattern, DeviceTier tier, int line = 0)
        {
            if (string.IsNullOrEmpty(pattern)) throw new FrameworkException("DeviceTierClassifier：覆盖规则的匹配串不能为空。");
            OverrideRule r;
            r.Field = field;
            r.Pattern = pattern;
            r.Tier = tier;
            r.Line = line;
            m_Overrides.Add(r);
        }

        public void AddCap(Metric metric, int lessThan, DeviceTier maxTier, int line = 0)
        {
            CapRule r;
            r.Metric = metric;
            r.LessThan = lessThan;
            r.MaxTier = maxTier;
            r.Line = line;
            m_Caps.Add(r);
        }

        /// <summary>设置档位阈值（Low / Medium / High / Ultra 的最低分，严格递增，位于 (0,1]）。</summary>
        public void SetThresholds(bool mobile, float low, float medium, float high, float ultra)
        {
            if (!(low > 0f && low < medium && medium < high && high < ultra && ultra <= 1f))
            {
                throw new FrameworkException("DeviceTierClassifier：阈值须满足 0 < low < medium < high < ultra <= 1。");
            }

            float[] t = mobile ? m_MobileThresholds : m_DesktopThresholds;
            t[0] = low;
            t[1] = medium;
            t[2] = high;
            t[3] = ultra;
        }

        public void Clear()
        {
            m_Overrides.Clear();
            m_Caps.Clear();
        }

        /// <summary>分级。</summary>
        public DeviceTierResult Classify(in DeviceProfile profile)
        {
            DeviceTierResult result;
            result.Score = Score(in profile);
            result.RuleLine = 0;

            for (int i = 0; i < m_Overrides.Count; i++)
            {
                OverrideRule r = m_Overrides[i];
                string value = FieldValue(in profile, r.Field);
                if (value != null && value.IndexOf(r.Pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Tier = r.Tier;
                    result.Reason = DeviceTierReason.Override;
                    result.RuleLine = r.Line;
                    return result;
                }
            }

            float[] thresholds = profile.IsMobile ? m_MobileThresholds : m_DesktopThresholds;
            DeviceTier tier = DeviceTier.Minimum;
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (result.Score >= thresholds[i]) tier = (DeviceTier)(i + 1);
            }

            result.Tier = tier;
            result.Reason = DeviceTierReason.Score;
            for (int i = 0; i < m_Caps.Count; i++)
            {
                CapRule c = m_Caps[i];
                if (MetricValue(in profile, c.Metric) < c.LessThan && result.Tier > c.MaxTier)
                {
                    result.Tier = c.MaxTier;
                    result.Reason = DeviceTierReason.Capped;
                    result.RuleLine = c.Line;
                }
            }

            return result;
        }

        /// <summary>硬件评分 0..1。</summary>
        public static float Score(in DeviceProfile p)
        {
            float memoryRef = p.IsMobile ? 8192f : 16384f;
            float vramRef = p.IsMobile ? 4096f : 8192f;
            float score = 0.15f * Clamp01(p.CpuCores / 8f)
                          + 0.15f * Clamp01(p.CpuFrequencyMHz / 2800f)
                          + 0.30f * Clamp01(p.SystemMemoryMB / memoryRef)
                          + 0.25f * Clamp01(p.GraphicsMemoryMB / vramRef)
                          + 0.15f * Clamp01((p.GraphicsShaderLevel - 35) / 15f);
            return score;
        }

        // ================================================================
        //  规则文本
        // ================================================================

        /// <summary>解析规则文本并追加（不清空已有规则）。语法见类注释；任何错误抛出带行号的 <see cref="FrameworkException"/>。</summary>
        public void LoadRules(string text)
        {
            if (text == null) throw new FrameworkException("DeviceTierClassifier.LoadRules：text 为空。");
            List<string> tokens = new List<string>();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string line = lines[i];
                Tokenize(line, lineNo, tokens);
                if (tokens.Count == 0) continue;
                string head = tokens[0].ToLowerInvariant();
                switch (head)
                {
                    case "override":
                        // override <field> "<pattern>" = <tier>
                        Expect(tokens.Count == 5 && tokens[3] == "=", lineNo, "override <model|gpu|cpu|os> \"子串\" = <档位>");
                        AddOverride(ParseField(tokens[1], lineNo), tokens[2], ParseTier(tokens[4], lineNo), lineNo);
                        break;
                    case "cap":
                        // cap <metric> < <int> = <tier>
                        Expect(tokens.Count == 6 && tokens[2] == "<" && tokens[4] == "=", lineNo, "cap <memory|vram|cores|freq|shader> < <整数> = <档位>");
                        AddCap(ParseMetric(tokens[1], lineNo), ParseInt(tokens[3], lineNo), ParseTier(tokens[5], lineNo), lineNo);
                        break;
                    case "thresholds":
                        Expect(tokens.Count == 6, lineNo, "thresholds <mobile|desktop> <low> <medium> <high> <ultra>");
                        string kind = tokens[1].ToLowerInvariant();
                        Expect(kind == "mobile" || kind == "desktop", lineNo, "thresholds 的平台只能是 mobile / desktop");
                        try
                        {
                            SetThresholds(kind == "mobile", ParseFloat(tokens[2], lineNo), ParseFloat(tokens[3], lineNo),
                                ParseFloat(tokens[4], lineNo), ParseFloat(tokens[5], lineNo));
                        }
                        catch (FrameworkException ex)
                        {
                            throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：" + ex.Message);
                        }

                        break;
                    default:
                        throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：未知指令 '" + tokens[0] + "'。");
                }
            }
        }

        private static void Tokenize(string line, int lineNo, List<string> tokens)
        {
            tokens.Clear();
            int i = 0;
            int n = line.Length;
            while (i < n)
            {
                char c = line[i];
                if (c == '#') break;
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"')
                {
                    int end = line.IndexOf('"', i + 1);
                    if (end < 0) throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：引号未闭合。");
                    tokens.Add(line.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }

                if (c == '=' || c == '<')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                int start = i;
                while (i < n && !char.IsWhiteSpace(line[i]) && line[i] != '=' && line[i] != '<' && line[i] != '#' && line[i] != '"') i++;
                tokens.Add(line.Substring(start, i - start));
            }
        }

        private static void Expect(bool condition, int lineNo, string usage)
        {
            if (!condition) throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：格式应为 " + usage + "。");
        }

        private static Field ParseField(string s, int lineNo)
        {
            switch (s.ToLowerInvariant())
            {
                case "model": return Field.Model;
                case "gpu": return Field.Gpu;
                case "cpu": return Field.Cpu;
                case "os": return Field.Os;
                default: throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：未知字段 '" + s + "'（model / gpu / cpu / os）。");
            }
        }

        private static Metric ParseMetric(string s, int lineNo)
        {
            switch (s.ToLowerInvariant())
            {
                case "memory": return Metric.Memory;
                case "vram": return Metric.Vram;
                case "cores": return Metric.Cores;
                case "freq": return Metric.Freq;
                case "shader": return Metric.Shader;
                default: throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：未知指标 '" + s + "'（memory / vram / cores / freq / shader）。");
            }
        }

        private static DeviceTier ParseTier(string s, int lineNo)
        {
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= 4) return (DeviceTier)n;
            switch (s.ToLowerInvariant())
            {
                case "minimum": return DeviceTier.Minimum;
                case "low": return DeviceTier.Low;
                case "medium": return DeviceTier.Medium;
                case "high": return DeviceTier.High;
                case "ultra": return DeviceTier.Ultra;
                default: throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：未知档位 '" + s + "'（Minimum/Low/Medium/High/Ultra 或 0..4）。");
            }
        }

        private static int ParseInt(string s, int lineNo)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：'" + s + "' 不是整数。");
            return v;
        }

        private static float ParseFloat(string s, int lineNo)
        {
            float v;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new FrameworkException("DeviceTier 规则第 " + lineNo + " 行：'" + s + "' 不是数字。");
            return v;
        }

        private static string FieldValue(in DeviceProfile p, Field field)
        {
            switch (field)
            {
                case Field.Model: return p.DeviceModel;
                case Field.Gpu: return p.GpuName;
                case Field.Cpu: return p.CpuName;
                default: return p.OperatingSystem;
            }
        }

        private static int MetricValue(in DeviceProfile p, Metric metric)
        {
            switch (metric)
            {
                case Metric.Memory: return p.SystemMemoryMB;
                case Metric.Vram: return p.GraphicsMemoryMB;
                case Metric.Cores: return p.CpuCores;
                case Metric.Freq: return p.CpuFrequencyMHz;
                default: return p.GraphicsShaderLevel;
            }
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }
    }
}
