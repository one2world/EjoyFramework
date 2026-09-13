using System;
using System.Diagnostics;
using System.Threading;

namespace EjoyFramework.Core.Performance
{
    public readonly struct DurationPerformanceSnapshot
    {
        private static readonly double MillisecondsPerTimestampTick = 1000d / Stopwatch.Frequency;

        public DurationPerformanceSnapshot(string name, long sampleCount, long lastTicks, long totalTicks, long maxTicks)
        {
            Name = name;
            SampleCount = sampleCount;
            LastMilliseconds = lastTicks * MillisecondsPerTimestampTick;
            TotalMilliseconds = totalTicks * MillisecondsPerTimestampTick;
            MaxMilliseconds = maxTicks * MillisecondsPerTimestampTick;
        }

        public string Name { get; }
        public long SampleCount { get; }
        public double LastMilliseconds { get; }
        public double TotalMilliseconds { get; }
        public double MeanMilliseconds => SampleCount > 0 ? TotalMilliseconds / SampleCount : 0d;
        public double MaxMilliseconds { get; }
    }

    public readonly struct ValuePerformanceSnapshot
    {
        public ValuePerformanceSnapshot(string name, string unit, long sampleCount, long lastValue, long totalValue, long maxValue)
        {
            Name = name;
            Unit = unit;
            SampleCount = sampleCount;
            LastValue = lastValue;
            TotalValue = totalValue;
            MaxValue = maxValue;
        }

        public string Name { get; }
        public string Unit { get; }
        public long SampleCount { get; }
        public long LastValue { get; }
        public long TotalValue { get; }
        public double MeanValue => SampleCount > 0 ? (double)TotalValue / SampleCount : 0d;
        public long MaxValue { get; }
    }

    /// <summary>
    /// Thread-safe duration statistics. Measure() returns a value-type scope and allocates no managed memory.
    /// </summary>
    public sealed class DurationPerformanceCounter
    {
        private long m_SampleCount;
        private long m_LastTicks;
        private long m_TotalTicks;
        private long m_MaxTicks;

        public DurationPerformanceCounter(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Counter name is required.", nameof(name));
            Name = name;
        }

        public string Name { get; }

        public DurationPerformanceSample Measure()
        {
            return new DurationPerformanceSample(this, Stopwatch.GetTimestamp());
        }

        public DurationPerformanceSnapshot Snapshot()
        {
            return new DurationPerformanceSnapshot(
                Name,
                Interlocked.Read(ref m_SampleCount),
                Interlocked.Read(ref m_LastTicks),
                Interlocked.Read(ref m_TotalTicks),
                Interlocked.Read(ref m_MaxTicks));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref m_SampleCount, 0);
            Interlocked.Exchange(ref m_LastTicks, 0);
            Interlocked.Exchange(ref m_TotalTicks, 0);
            Interlocked.Exchange(ref m_MaxTicks, 0);
        }

        internal void Record(long elapsedTicks)
        {
            if (elapsedTicks < 0) elapsedTicks = 0;
            Interlocked.Exchange(ref m_LastTicks, elapsedTicks);
            Interlocked.Add(ref m_TotalTicks, elapsedTicks);
            Interlocked.Increment(ref m_SampleCount);
            UpdateMax(ref m_MaxTicks, elapsedTicks);
        }

        private static void UpdateMax(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }

    public readonly struct DurationPerformanceSample : IDisposable
    {
        private readonly DurationPerformanceCounter m_Counter;
        private readonly long m_StartTimestamp;

        internal DurationPerformanceSample(DurationPerformanceCounter counter, long startTimestamp)
        {
            m_Counter = counter;
            m_StartTimestamp = startTimestamp;
        }

        public void Dispose()
        {
            m_Counter?.Record(Stopwatch.GetTimestamp() - m_StartTimestamp);
        }
    }

    /// <summary>
    /// Thread-safe gauge/statistics counter for counts, bytes and other integral runtime values.
    /// </summary>
    public sealed class ValuePerformanceCounter
    {
        private long m_SampleCount;
        private long m_LastValue;
        private long m_TotalValue;
        private long m_MaxValue;

        public ValuePerformanceCounter(string name, string unit)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Counter name is required.", nameof(name));
            Name = name;
            Unit = unit ?? string.Empty;
        }

        public string Name { get; }
        public string Unit { get; }

        public void Record(long value)
        {
            Interlocked.Exchange(ref m_LastValue, value);
            Interlocked.Add(ref m_TotalValue, value);
            Interlocked.Increment(ref m_SampleCount);
            UpdateMax(ref m_MaxValue, value);
        }

        public ValuePerformanceSnapshot Snapshot()
        {
            return new ValuePerformanceSnapshot(
                Name,
                Unit,
                Interlocked.Read(ref m_SampleCount),
                Interlocked.Read(ref m_LastValue),
                Interlocked.Read(ref m_TotalValue),
                Interlocked.Read(ref m_MaxValue));
        }

        public void Reset()
        {
            Interlocked.Exchange(ref m_SampleCount, 0);
            Interlocked.Exchange(ref m_LastValue, 0);
            Interlocked.Exchange(ref m_TotalValue, 0);
            Interlocked.Exchange(ref m_MaxValue, 0);
        }

        private static void UpdateMax(ref long target, long value)
        {
            long current = Interlocked.Read(ref target);
            while (value > current)
            {
                long observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
