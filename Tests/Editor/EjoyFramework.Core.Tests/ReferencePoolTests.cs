//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class ReferencePoolTests
    {
        private class TestRef : IReference
        {
            public int Payload;
            public void Clear() { Payload = 0; }
        }

        private class ThrowingRef : IReference
        {
            public void Clear() { throw new System.InvalidOperationException("Clear deliberately throws"); }
        }

        [SetUp]
        public void Setup()
        {
            ReferencePool.RemoveAll<TestRef>();
            ReferencePool.RemoveAll<ThrowingRef>();
        }

        [Test]
        public void Acquire_ReusesReleasedInstance()
        {
            var r1 = ReferencePool.Acquire<TestRef>();
            r1.Payload = 42;
            ReferencePool.Release(r1);

            var r2 = ReferencePool.Acquire<TestRef>();
            // 同一池实例
            Assert.AreSame(r1, r2);
            // 已 Clear，Payload 归零
            Assert.AreEqual(0, r2.Payload);
        }

        [Test]
        public void Release_ClearException_DoesNotPoisonCounters()
        {
            var r = ReferencePool.Acquire<ThrowingRef>();
            // 不应抛出（异常被吞掉 + 计数对齐）
            Assert.DoesNotThrow(() => ReferencePool.Release(r));
        }

        [Test]
        public void MaxCapacity_DiscardsBeyondLimit()
        {
            ReferencePool.SetMaxCapacity<TestRef>(2);

            var r1 = new TestRef();
            var r2 = new TestRef();
            var r3 = new TestRef();
            ReferencePool.Release(r1);
            ReferencePool.Release(r2);
            ReferencePool.Release(r3); // 超出，被丢弃

            // 重新拿应该只能拿到 2 个池缓存的，第 3 次会 new
            var x1 = ReferencePool.Acquire<TestRef>();
            var x2 = ReferencePool.Acquire<TestRef>();
            var x3 = ReferencePool.Acquire<TestRef>();
            // x3 是新建的（不在原 r1/r2 内）
            Assert.That(x3, Is.Not.SameAs(r1).And.Not.SameAs(r2).And.Not.SameAs(r3));

            ReferencePool.SetMaxCapacity<TestRef>(0);  // reset
        }

        [Test]
        public void Concurrent_AcquireRelease_NoCounterCorruption()
        {
            // 取 baseline 计数（ReferencePool 是静态、跨测试累计；RemoveAll 只清队列不清计数）
            long baseAcq = 0, baseRel = 0, baseUsing = 0;
            foreach (var i in ReferencePool.GetAllReferencePoolInfos())
            {
                if (i.Type == typeof(TestRef))
                {
                    baseAcq = i.AcquireReferenceCount;
                    baseRel = i.ReleaseReferenceCount;
                    baseUsing = i.UsingReferenceCount;
                    break;
                }
            }

            const int threads = 4;
            const int iterations = 1000;
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var r = ReferencePool.Acquire<TestRef>();
                        ReferencePool.Release(r);
                    }
                });
            }
            Task.WaitAll(tasks);

            var infos = ReferencePool.GetAllReferencePoolInfos();
            foreach (var info in infos)
            {
                if (info.Type == typeof(TestRef))
                {
                    Assert.AreEqual(threads * iterations, info.AcquireReferenceCount - baseAcq);
                    Assert.AreEqual(threads * iterations, info.ReleaseReferenceCount - baseRel);
                    Assert.AreEqual(baseUsing, info.UsingReferenceCount); // 净 using 应回到基线
                }
            }
        }
    }
}
