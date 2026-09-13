using EjoyFramework.Core.Performance;
using NUnit.Framework;

namespace EjoyFramework.Tests
{
    public sealed class PerformanceCounterTests
    {
        [Test]
        public void DurationCounter_RecordsScopeStatistics()
        {
            var counter = new DurationPerformanceCounter("test.duration");

            using (counter.Measure()) { }
            using (counter.Measure()) { }

            DurationPerformanceSnapshot snapshot = counter.Snapshot();
            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.TotalMilliseconds, Is.GreaterThanOrEqualTo(0d));
            Assert.That(snapshot.MaxMilliseconds, Is.GreaterThanOrEqualTo(snapshot.LastMilliseconds));
        }

        [Test]
        public void ValueCounter_RecordsAndResetsStatistics()
        {
            var counter = new ValuePerformanceCounter("test.value", "count");
            counter.Record(4);
            counter.Record(10);

            ValuePerformanceSnapshot snapshot = counter.Snapshot();
            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.LastValue, Is.EqualTo(10));
            Assert.That(snapshot.MeanValue, Is.EqualTo(7d));
            Assert.That(snapshot.MaxValue, Is.EqualTo(10));

            counter.Reset();
            Assert.That(counter.Snapshot().SampleCount, Is.Zero);
        }
    }
}
