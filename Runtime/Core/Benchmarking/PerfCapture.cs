//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EjoyFramework.Core.Benchmarking
{
    /// <summary>一次性能采集的摘要。</summary>
    public struct PerfCaptureSummary
    {
        public int Frames;
        public double DurationSeconds;
        public float AvgFrameMs;
        public float P50FrameMs;
        public float P95FrameMs;
        public float P99FrameMs;
        public float MaxFrameMs;
        public float AvgWorkMs;
        public float P95WorkMs;

        /// <summary>超过 50ms / 100ms 的帧数（肉眼可见的卡顿）。</summary>
        public int Hitches50;
        public int Hitches100;

        public int PeakManagedMB;
        public int PeakTotalMB;
        public int PeakGraphicsMB;
    }

    /// <summary>
    /// 真机性能采集（跑图 / 基准场景 / 自动化回归用）：逐帧喂帧耗时与工作耗时，按 0.1ms 桶累计直方图求分位（零分配、
    /// 与帧数无关的固定内存），统计卡顿次数与内存峰值，可打标记（"进入城镇"、"Boss 出场"）。
    /// 结束后生成摘要并可写成与 <see cref="BenchmarkReport"/> 同风格的制表符文本。
    /// </summary>
    public sealed class PerfCapture
    {
        private const float BucketsPerMs = 10f;  // 0.1ms 一桶；用乘法定位桶（整数毫秒值精确落桶，不受除法舍入影响）
        private const int BucketCount = 2500;    // 0..250ms，超出计入溢出桶

        private readonly int[] m_FrameHist = new int[BucketCount + 1];
        private readonly int[] m_WorkHist = new int[BucketCount + 1];
        private readonly List<KeyValuePair<double, string>> m_Markers = new List<KeyValuePair<double, string>>();
        private string m_Label;
        private bool m_Running;
        private int m_Frames;
        private int m_WorkFrames;
        private double m_Duration;
        private double m_FrameSum;
        private double m_WorkSum;
        private float m_MaxFrame;
        private int m_Hitch50;
        private int m_Hitch100;
        private int m_PeakManaged;
        private int m_PeakTotal;
        private int m_PeakGraphics;

        public bool IsRunning { get { return m_Running; } }

        public string Label { get { return m_Label; } }

        public int FrameCount { get { return m_Frames; } }

        public double ElapsedSeconds { get { return m_Duration; } }

        public IReadOnlyList<KeyValuePair<double, string>> Markers { get { return m_Markers; } }

        public void Begin(string label)
        {
            if (string.IsNullOrEmpty(label)) throw new FrameworkException("PerfCapture：label 不能为空。");
            if (label.IndexOf('\t') >= 0 || label.IndexOf('\n') >= 0) throw new FrameworkException("PerfCapture：label 不能含制表符或换行。");
            Array.Clear(m_FrameHist, 0, m_FrameHist.Length);
            Array.Clear(m_WorkHist, 0, m_WorkHist.Length);
            m_Markers.Clear();
            m_Label = label;
            m_Frames = 0;
            m_WorkFrames = 0;
            m_Duration = 0;
            m_FrameSum = 0;
            m_WorkSum = 0;
            m_MaxFrame = 0;
            m_Hitch50 = 0;
            m_Hitch100 = 0;
            m_PeakManaged = 0;
            m_PeakTotal = 0;
            m_PeakGraphics = 0;
            m_Running = true;
        }

        /// <summary>喂一帧。<paramref name="workMs"/> ≤ 0 表示本帧无工作耗时数据。</summary>
        public void AddFrame(float frameMs, float workMs)
        {
            if (!m_Running || !(frameMs > 0f)) return;
            m_Frames++;
            m_Duration += frameMs / 1000.0;
            m_FrameSum += frameMs;
            if (frameMs > m_MaxFrame) m_MaxFrame = frameMs;
            if (frameMs > 50f) m_Hitch50++;
            if (frameMs > 100f) m_Hitch100++;
            m_FrameHist[Bucket(frameMs)]++;
            if (workMs > 0f)
            {
                m_WorkFrames++;
                m_WorkSum += workMs;
                m_WorkHist[Bucket(workMs)]++;
            }
        }

        public void AddMemorySample(int managedMB, int totalMB, int graphicsMB)
        {
            if (!m_Running) return;
            if (managedMB > m_PeakManaged) m_PeakManaged = managedMB;
            if (totalMB > m_PeakTotal) m_PeakTotal = totalMB;
            if (graphicsMB > m_PeakGraphics) m_PeakGraphics = graphicsMB;
        }

        /// <summary>在当前采集时间点打标记（会分配一次列表项，非每帧调用）。</summary>
        public void Mark(string name)
        {
            if (!m_Running) return;
            if (string.IsNullOrEmpty(name) || name.IndexOf('\t') >= 0 || name.IndexOf('\n') >= 0)
            {
                throw new FrameworkException("PerfCapture.Mark：name 不能为空、不能含制表符或换行。");
            }

            m_Markers.Add(new KeyValuePair<double, string>(m_Duration, name));
        }

        public PerfCaptureSummary End()
        {
            PerfCaptureSummary s = Summary;
            m_Running = false;
            return s;
        }

        /// <summary>当前摘要（采集中也可读）。</summary>
        public PerfCaptureSummary Summary
        {
            get
            {
                PerfCaptureSummary s = default(PerfCaptureSummary);
                s.Frames = m_Frames;
                s.DurationSeconds = m_Duration;
                s.AvgFrameMs = m_Frames > 0 ? (float)(m_FrameSum / m_Frames) : 0f;
                s.P50FrameMs = Percentile(m_FrameHist, m_Frames, 0.50);
                s.P95FrameMs = Percentile(m_FrameHist, m_Frames, 0.95);
                s.P99FrameMs = Percentile(m_FrameHist, m_Frames, 0.99);
                s.MaxFrameMs = m_MaxFrame;
                s.AvgWorkMs = m_WorkFrames > 0 ? (float)(m_WorkSum / m_WorkFrames) : 0f;
                s.P95WorkMs = Percentile(m_WorkHist, m_WorkFrames, 0.95);
                s.Hitches50 = m_Hitch50;
                s.Hitches100 = m_Hitch100;
                s.PeakManagedMB = m_PeakManaged;
                s.PeakTotalMB = m_PeakTotal;
                s.PeakGraphicsMB = m_PeakGraphics;
                return s;
            }
        }

        /// <summary>写成制表符文本：签名、环境、摘要键值、标记。</summary>
        public void Write(TextWriter w, IReadOnlyList<KeyValuePair<string, string>> environment)
        {
            PerfCaptureSummary s = Summary;
            w.Write("# ejoy-perfcapture 1\n");
            w.Write("# label\t"); w.Write(m_Label ?? string.Empty); w.Write('\n');
            if (environment != null)
            {
                for (int i = 0; i < environment.Count; i++)
                {
                    w.Write("# "); w.Write(environment[i].Key); w.Write('\t'); w.Write(environment[i].Value); w.Write('\n');
                }
            }

            Kv(w, "frames", s.Frames.ToString(CultureInfo.InvariantCulture));
            Kv(w, "duration_s", s.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            Kv(w, "avg_ms", F(s.AvgFrameMs));
            Kv(w, "p50_ms", F(s.P50FrameMs));
            Kv(w, "p95_ms", F(s.P95FrameMs));
            Kv(w, "p99_ms", F(s.P99FrameMs));
            Kv(w, "max_ms", F(s.MaxFrameMs));
            Kv(w, "avg_work_ms", F(s.AvgWorkMs));
            Kv(w, "p95_work_ms", F(s.P95WorkMs));
            Kv(w, "hitches_50ms", s.Hitches50.ToString(CultureInfo.InvariantCulture));
            Kv(w, "hitches_100ms", s.Hitches100.ToString(CultureInfo.InvariantCulture));
            Kv(w, "peak_managed_mb", s.PeakManagedMB.ToString(CultureInfo.InvariantCulture));
            Kv(w, "peak_total_mb", s.PeakTotalMB.ToString(CultureInfo.InvariantCulture));
            Kv(w, "peak_gfx_mb", s.PeakGraphicsMB.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < m_Markers.Count; i++)
            {
                w.Write("marker\t");
                w.Write(m_Markers[i].Key.ToString("0.###", CultureInfo.InvariantCulture));
                w.Write('\t');
                w.Write(m_Markers[i].Value);
                w.Write('\n');
            }
        }

        public string ToText(IReadOnlyList<KeyValuePair<string, string>> environment = null)
        {
            StringWriter sw = new StringWriter(CultureInfo.InvariantCulture);
            Write(sw, environment);
            return sw.ToString();
        }

        private static void Kv(TextWriter w, string key, string value)
        {
            w.Write(key);
            w.Write('\t');
            w.Write(value);
            w.Write('\n');
        }

        private static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static int Bucket(float ms)
        {
            int b = (int)(ms * BucketsPerMs);
            return b < 0 ? 0 : b > BucketCount ? BucketCount : b;
        }

        // ceil-rank 分位；返回桶上沿（保守：分位值不会被低估超过一个桶宽 0.1ms）；溢出桶返回 250ms 下限
        private static float Percentile(int[] hist, int count, double p)
        {
            if (count == 0) return 0f;
            long rank = (long)Math.Ceiling(p * count);
            if (rank < 1) rank = 1;
            long seen = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                seen += hist[i];
                if (seen >= rank) return i >= BucketCount ? BucketCount / BucketsPerMs : (i + 1) / BucketsPerMs;
            }

            return BucketCount / BucketsPerMs;
        }
    }
}
