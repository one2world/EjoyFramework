//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EjoyFramework.Core.Benchmarking
{
    /// <summary>
    /// 一组基准结果 + 运行环境。落盘为带头注释的制表符分隔文本（人可读、git 可 diff、无需 JSON 库即可解析）：
    /// <code>
    /// # ejoy-benchmark 1
    /// # suite	EditMode
    /// # unity	6000.4.8f1
    /// name	samples	ops	min_ns	median_ns	mean_ns	p95_ns	stddev_ns	alloc_per_op	alloc_unit
    /// Pool.ListGetRelease	15	524288	21.3	21.9	22.0	22.6	0.4	0	allocs
    /// </code>
    /// 未测分配的行 alloc_per_op / alloc_unit 为 "-"。
    /// </summary>
    public sealed class BenchmarkReport
    {
        public const string Signature = "# ejoy-benchmark 1";
        private const string ColumnHeader = "name\tsamples\tops\tmin_ns\tmedian_ns\tmean_ns\tp95_ns\tstddev_ns\talloc_per_op\talloc_unit";

        private readonly List<KeyValuePair<string, string>> m_Environment = new List<KeyValuePair<string, string>>();
        private readonly List<BenchmarkResult> m_Results = new List<BenchmarkResult>();

        public IReadOnlyList<BenchmarkResult> Results { get { return m_Results; } }

        public IReadOnlyList<KeyValuePair<string, string>> Environment { get { return m_Environment; } }

        public void SetEnvironment(string key, string value)
        {
            CheckField(key, "环境键");
            string v = value ?? string.Empty;
            CheckField(v.Length == 0 ? "-" : v, "环境值");
            for (int i = 0; i < m_Environment.Count; i++)
            {
                if (m_Environment[i].Key == key)
                {
                    m_Environment[i] = new KeyValuePair<string, string>(key, v);
                    return;
                }
            }

            m_Environment.Add(new KeyValuePair<string, string>(key, v));
        }

        public string GetEnvironment(string key)
        {
            for (int i = 0; i < m_Environment.Count; i++)
            {
                if (m_Environment[i].Key == key) return m_Environment[i].Value;
            }

            return null;
        }

        /// <summary>添加结果（同名替换）。名字不能含制表符 / 换行。</summary>
        public void Add(BenchmarkResult result)
        {
            if (result == null) throw new FrameworkException("BenchmarkReport.Add：result 为空。");
            CheckField(result.Name, "基准名");
            for (int i = 0; i < m_Results.Count; i++)
            {
                if (m_Results[i].Name == result.Name)
                {
                    m_Results[i] = result;
                    return;
                }
            }

            m_Results.Add(result);
        }

        public BenchmarkResult Find(string name)
        {
            for (int i = 0; i < m_Results.Count; i++)
            {
                if (m_Results[i].Name == name) return m_Results[i];
            }

            return null;
        }

        // ================================================================
        //  序列化
        // ================================================================

        public void Write(TextWriter writer)
        {
            writer.Write(Signature);
            writer.Write('\n');
            for (int i = 0; i < m_Environment.Count; i++)
            {
                writer.Write("# ");
                writer.Write(m_Environment[i].Key);
                writer.Write('\t');
                writer.Write(m_Environment[i].Value);
                writer.Write('\n');
            }

            writer.Write(ColumnHeader);
            writer.Write('\n');
            for (int i = 0; i < m_Results.Count; i++)
            {
                BenchmarkResult r = m_Results[i];
                writer.Write(r.Name);
                writer.Write('\t'); writer.Write(r.Samples.ToString(CultureInfo.InvariantCulture));
                writer.Write('\t'); writer.Write(r.OpsPerSample.ToString(CultureInfo.InvariantCulture));
                writer.Write('\t'); writer.Write(Num(r.MinNs));
                writer.Write('\t'); writer.Write(Num(r.MedianNs));
                writer.Write('\t'); writer.Write(Num(r.MeanNs));
                writer.Write('\t'); writer.Write(Num(r.P95Ns));
                writer.Write('\t'); writer.Write(Num(r.StdDevNs));
                writer.Write('\t'); writer.Write(r.AllocMeasured ? Num(r.AllocPerOp) : "-");
                writer.Write('\t'); writer.Write(r.AllocMeasured ? r.AllocUnit ?? "?" : "-");
                writer.Write('\n');
            }
        }

        public override string ToString()
        {
            StringWriter sw = new StringWriter(CultureInfo.InvariantCulture);
            Write(sw);
            return sw.ToString();
        }

        /// <summary>写入文件（目录不存在则创建）。</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                Write(w);
            }
        }

        /// <summary>解析；格式错误抛出带行号的 <see cref="FrameworkException"/>。</summary>
        public static BenchmarkReport Parse(string text)
        {
            if (text == null) throw new FrameworkException("BenchmarkReport.Parse：text 为空。");
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0] != Signature) throw new FrameworkException("BenchmarkReport：缺少签名行 '" + Signature + "'。");

            BenchmarkReport report = new BenchmarkReport();
            bool header = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                int lineNo = i + 1;
                if (!header && line.StartsWith("# ", StringComparison.Ordinal))
                {
                    int tab = line.IndexOf('\t');
                    if (tab < 0) throw new FrameworkException("BenchmarkReport 第 " + lineNo + " 行：环境行缺少制表符。");
                    report.m_Environment.Add(new KeyValuePair<string, string>(line.Substring(2, tab - 2), line.Substring(tab + 1)));
                    continue;
                }

                if (!header)
                {
                    if (line != ColumnHeader) throw new FrameworkException("BenchmarkReport 第 " + lineNo + " 行：列头不匹配。");
                    header = true;
                    continue;
                }

                string[] f = line.Split('\t');
                if (f.Length != 10) throw new FrameworkException("BenchmarkReport 第 " + lineNo + " 行：应为 10 列，实际 " + f.Length + "。");
                BenchmarkResult r = new BenchmarkResult();
                r.Name = f[0];
                r.Samples = (int)ParseLong(f[1], lineNo);
                r.OpsPerSample = ParseLong(f[2], lineNo);
                r.MinNs = ParseDouble(f[3], lineNo);
                r.MedianNs = ParseDouble(f[4], lineNo);
                r.MeanNs = ParseDouble(f[5], lineNo);
                r.P95Ns = ParseDouble(f[6], lineNo);
                r.StdDevNs = ParseDouble(f[7], lineNo);
                r.AllocMeasured = f[8] != "-";
                if (r.AllocMeasured)
                {
                    r.AllocPerOp = ParseDouble(f[8], lineNo);
                    r.AllocUnit = f[9];
                }

                report.m_Results.Add(r);
            }

            if (!header) throw new FrameworkException("BenchmarkReport：缺少列头。");
            return report;
        }

        public static BenchmarkReport Load(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        private static string Num(double v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static long ParseLong(string s, int lineNo)
        {
            long v;
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) throw new FrameworkException("BenchmarkReport 第 " + lineNo + " 行：'" + s + "' 不是整数。");
            return v;
        }

        private static double ParseDouble(string s, int lineNo)
        {
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new FrameworkException("BenchmarkReport 第 " + lineNo + " 行：'" + s + "' 不是数字。");
            return v;
        }

        private static void CheckField(string s, string what)
        {
            if (string.IsNullOrEmpty(s)) throw new FrameworkException("BenchmarkReport：" + what + "不能为空。");
            if (s.IndexOf('\t') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            {
                throw new FrameworkException("BenchmarkReport：" + what + " '" + s + "' 不能含制表符或换行。");
            }
        }
    }

    /// <summary>对比结论。</summary>
    public enum BenchmarkVerdict
    {
        Same,
        Faster,
        Slower,

        /// <summary>分配变多（包括从 0 变为非 0）——零分配路径的回归，应视为失败。</summary>
        AllocRegression,

        AllocImproved,
        Added,
        Removed,
    }

    public struct BenchmarkDelta
    {
        public string Name;
        public BenchmarkVerdict Verdict;
        public double BaselineMedianNs;
        public double CurrentMedianNs;

        /// <summary>当前中位 / 基线中位。</summary>
        public double Ratio;

        public double BaselineAllocPerOp;
        public double CurrentAllocPerOp;
    }

    /// <summary>
    /// 基线对比。时间判定抗噪：变慢需同时满足"中位比 &gt; slowerRatio"与"当前最快样本仍慢于基线中位"；
    /// 变快对称。分配以绝对值判定（两边都测了才比）。同机同配置的基线才有意义——跨机器只看分配结论。
    /// </summary>
    public static class BenchmarkComparison
    {
        public static List<BenchmarkDelta> Compare(BenchmarkReport baseline, BenchmarkReport current, double slowerRatio = 1.2, double fasterRatio = 0.8)
        {
            if (baseline == null || current == null) throw new FrameworkException("BenchmarkComparison：报告为空。");
            List<BenchmarkDelta> deltas = new List<BenchmarkDelta>();
            for (int i = 0; i < current.Results.Count; i++)
            {
                BenchmarkResult c = current.Results[i];
                BenchmarkResult b = baseline.Find(c.Name);
                BenchmarkDelta d = default(BenchmarkDelta);
                d.Name = c.Name;
                d.CurrentMedianNs = c.MedianNs;
                d.CurrentAllocPerOp = c.AllocPerOp;
                if (b == null)
                {
                    d.Verdict = BenchmarkVerdict.Added;
                    deltas.Add(d);
                    continue;
                }

                d.BaselineMedianNs = b.MedianNs;
                d.BaselineAllocPerOp = b.AllocPerOp;
                d.Ratio = b.MedianNs > 0 ? c.MedianNs / b.MedianNs : 1;
                if (b.AllocMeasured && c.AllocMeasured && c.AllocPerOp > b.AllocPerOp + 1e-9) d.Verdict = BenchmarkVerdict.AllocRegression;
                else if (d.Ratio > slowerRatio && c.MinNs > b.MedianNs) d.Verdict = BenchmarkVerdict.Slower;
                else if (d.Ratio < fasterRatio && c.MedianNs < b.MinNs) d.Verdict = BenchmarkVerdict.Faster;
                else if (b.AllocMeasured && c.AllocMeasured && c.AllocPerOp < b.AllocPerOp - 1e-9) d.Verdict = BenchmarkVerdict.AllocImproved;
                else d.Verdict = BenchmarkVerdict.Same;
                deltas.Add(d);
            }

            for (int i = 0; i < baseline.Results.Count; i++)
            {
                BenchmarkResult b = baseline.Results[i];
                if (current.Find(b.Name) != null) continue;
                BenchmarkDelta d = default(BenchmarkDelta);
                d.Name = b.Name;
                d.Verdict = BenchmarkVerdict.Removed;
                d.BaselineMedianNs = b.MedianNs;
                d.BaselineAllocPerOp = b.AllocPerOp;
                deltas.Add(d);
            }

            return deltas;
        }
    }
}
