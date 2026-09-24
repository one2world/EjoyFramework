//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Diagnostics;

namespace EjoyFramework.Core.Benchmarking
{
    /// <summary>
    /// 托管分配探针。Core 引擎无关，由 Unity 层用 Profiler 的 GC.Alloc 事件实现（Mono/Boehm 下
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> 是死计数器，恒为 0，不能用）。
    /// </summary>
    public interface IAllocationProbe
    {
        /// <summary>探针在当前运行时是否可用（不可用时结果标记为"未测"，不会报 0）。</summary>
        bool IsAvailable { get; }

        /// <summary>计量单位说明（如 "allocs" = 分配次数，"bytes" = 字节）。</summary>
        string Unit { get; }

        void Begin();

        /// <summary>结束并返回 Begin 以来的计量值。</summary>
        long End();
    }

    /// <summary>基准选项。</summary>
    public sealed class BenchmarkOptions
    {
        /// <summary>预热轮数（JIT / 泛型实例化 / 首次池分配摊掉）。默认 2。</summary>
        public int WarmupRuns = 2;

        /// <summary>单个样本的最短耗时（毫秒）：每样本操作数自动倍增到不低于该值，避开计时器分辨率。默认 10。</summary>
        public double MinSampleMillis = 10.0;

        /// <summary>样本数。默认 15。</summary>
        public int Samples = 15;

        /// <summary>单样本操作数上限。默认 2^24。</summary>
        public long MaxOpsPerSample = 1L << 24;

        public static BenchmarkOptions Quick()
        {
            return new BenchmarkOptions { WarmupRuns = 1, MinSampleMillis = 2.0, Samples = 7 };
        }
    }

    /// <summary>一个基准的结果（纳秒 / 次）。</summary>
    public sealed class BenchmarkResult
    {
        public string Name;
        public int Samples;
        public long OpsPerSample;
        public double MinNs;
        public double MedianNs;
        public double MeanNs;
        public double P95Ns;
        public double StdDevNs;

        /// <summary>每次操作的分配量；<see cref="AllocMeasured"/> 为 false 时无意义。</summary>
        public double AllocPerOp;

        public bool AllocMeasured;
        public string AllocUnit;

        /// <summary>变异系数（标准差 / 均值）：&gt; 0.1 说明环境噪声大，结论要打折。</summary>
        public double CoefficientOfVariation { get { return MeanNs > 0 ? StdDevNs / MeanNs : 0; } }
    }

    /// <summary>
    /// 微基准执行器：预热 → 自适应每样本操作数 → 多样本计时 → 统计（最小 / 中位 / 均值 / P95 / 标准差）→ 单独一轮测分配。
    /// 被测体签名 <c>Action&lt;long&gt;</c>：执行 n 次操作（循环写在被测体里，避免委托调用开销混进每次操作）。
    /// 计时用单调 <see cref="Stopwatch"/>；分配在计时之外单独测，探针开销不污染时间。
    /// </summary>
    public static class BenchmarkRunner
    {
        /// <summary>测试注入的时钟（ticks, 每秒 ticks）；null 用 Stopwatch。</summary>
        internal static Func<long> ClockOverride;
        internal static long ClockFrequencyOverride;

        public static BenchmarkResult Run(string name, Action<long> body, BenchmarkOptions options = null, IAllocationProbe probe = null)
        {
            if (string.IsNullOrEmpty(name)) throw new FrameworkException("BenchmarkRunner：name 不能为空。");
            if (body == null) throw new FrameworkException("BenchmarkRunner：body 为空。");
            BenchmarkOptions o = options ?? new BenchmarkOptions();
            if (o.Samples < 1) throw new FrameworkException("BenchmarkRunner：Samples 须 ≥ 1。");

            for (int i = 0; i < o.WarmupRuns; i++) body(1);

            long ops = Calibrate(body, o);
            if (o.WarmupRuns > 0) body(ops);   // 按最终规模再热一次（容量增长类的首次分配）

            double[] perOp = new double[o.Samples];
            for (int s = 0; s < o.Samples; s++)
            {
                long start = Now();
                body(ops);
                long elapsed = Now() - start;
                perOp[s] = TicksToNs(elapsed) / ops;
            }

            BenchmarkResult r = Summarize(name, perOp, ops);
            if (probe != null && probe.IsAvailable)
            {
                probe.Begin();
                body(ops);
                long allocated = probe.End();
                r.AllocMeasured = true;
                r.AllocUnit = probe.Unit;
                r.AllocPerOp = (double)allocated / ops;
            }

            return r;
        }

        internal static BenchmarkResult Summarize(string name, double[] perOpNs, long ops)
        {
            double[] sorted = (double[])perOpNs.Clone();
            Array.Sort(sorted);
            int n = sorted.Length;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += sorted[i];
            double mean = sum / n;
            double variance = 0;
            for (int i = 0; i < n; i++) variance += (sorted[i] - mean) * (sorted[i] - mean);

            BenchmarkResult r = new BenchmarkResult();
            r.Name = name;
            r.Samples = n;
            r.OpsPerSample = ops;
            r.MinNs = sorted[0];
            r.MedianNs = (n & 1) == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5;
            r.MeanNs = mean;
            r.P95Ns = sorted[Math.Min(n - 1, (int)Math.Ceiling(0.95 * n) - 1)];   // ceil-rank
            r.StdDevNs = n > 1 ? Math.Sqrt(variance / (n - 1)) : 0;
            return r;
        }

        private static long Calibrate(Action<long> body, BenchmarkOptions o)
        {
            long ops = 1;
            while (ops < o.MaxOpsPerSample)
            {
                long start = Now();
                body(ops);
                double ms = TicksToNs(Now() - start) / 1e6;
                if (ms >= o.MinSampleMillis) return ops;
                // 按实测外推一步到位（至少翻倍），避免在极快的操作上倍增几十轮
                long next = ms > 0 ? (long)(ops * (o.MinSampleMillis / ms) * 1.1) : ops * 16;
                ops = next > ops * 2 ? next : ops * 2;
            }

            return o.MaxOpsPerSample;
        }

        private static long Now()
        {
            Func<long> clock = ClockOverride;
            return clock != null ? clock() : Stopwatch.GetTimestamp();
        }

        private static double TicksToNs(long ticks)
        {
            long freq = ClockOverride != null ? ClockFrequencyOverride : Stopwatch.Frequency;
            return ticks * (1e9 / freq);
        }
    }
}
