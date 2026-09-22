//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>WS1-M3：CollectionPool / ListPool / HashSetPool / DictionaryPool / StringBuilderPool。</summary>
    public sealed class CollectionPoolTests
    {
        [SetUp]
        public void SetUp()
        {
            CollectionPool<List<int>, int>.ClearCurrentThread();
            CollectionPool<HashSet<string>, string>.ClearCurrentThread();
            StringBuilderPool.ClearCurrentThread();
        }

        [Test]
        public void GetRelease_ReusesInstance_AndClearsContents()
        {
            List<int> a = ListPool<int>.Get();
            a.Add(1); a.Add(2);
            ListPool<int>.Release(a);

            List<int> b = ListPool<int>.Get();
            Assert.AreSame(a, b, "归还后再租借应拿回同一实例。");
            Assert.AreEqual(0, b.Count, "归还时必须 Clear。");
            ListPool<int>.Release(b);
        }

        [Test]
        public void UsingScope_ReturnsToPool()
        {
            List<int> captured;
            using (ListPool<int>.Get(out captured))
            {
                captured.Add(42);
            }

            Assert.AreEqual(1, CollectionPool<List<int>, int>.IdleCountOnCurrentThread);
            Assert.AreSame(captured, ListPool<int>.Get());
        }

        [Test]
        public void HashSetAndDictionaryPools_Work()
        {
            HashSet<string> set;
            using (HashSetPool<string>.Get(out set)) { set.Add("a"); }
            Dictionary<int, string> dict;
            using (DictionaryPool<int, string>.Get(out dict)) { dict[1] = "x"; }

            Assert.AreEqual(0, HashSetPool<string>.Get().Count);
            Assert.AreEqual(0, DictionaryPool<int, string>.Get().Count);
        }

        [Test]
        public void Release_Null_IsIgnored()
        {
            Assert.DoesNotThrow(() => ListPool<int>.Release(null));
        }

        [Test]
        public void MaxRetained_CapsIdleCount()
        {
            int old = CollectionPool<List<int>, int>.MaxRetainedPerThread;
            try
            {
                CollectionPool<List<int>, int>.MaxRetainedPerThread = 2;
                for (int i = 0; i < 5; i++) ListPool<int>.Release(new List<int>());
                Assert.AreEqual(2, CollectionPool<List<int>, int>.IdleCountOnCurrentThread);
            }
            finally
            {
                CollectionPool<List<int>, int>.MaxRetainedPerThread = old;
            }
        }

        [Test]
        public void DoubleRelease_ThrowsInEditor()
        {
            List<int> list = ListPool<int>.Get();
            ListPool<int>.Release(list);
            FrameworkException ex = Assert.Throws<FrameworkException>(() => ListPool<int>.Release(list));
            StringAssert.Contains("重复归还", ex.Message);
        }

        [Test]
        public void Pools_AreThreadLocal()
        {
            ListPool<int>.Release(new List<int>());
            int idleOnWorker = -1;
            var t = new Thread(() => { idleOnWorker = CollectionPool<List<int>, int>.IdleCountOnCurrentThread; });
            t.Start();
            t.Join();

            Assert.AreEqual(0, idleOnWorker, "工作线程应有自己独立的空栈。");
            Assert.AreEqual(1, CollectionPool<List<int>, int>.IdleCountOnCurrentThread);
        }

        [Test]
        public void Metrics_CountRentedReleasedCreated()
        {
            long rented = CollectionPool<List<int>, int>.RentedCount;
            long created = CollectionPool<List<int>, int>.CreatedCount;
            long released = CollectionPool<List<int>, int>.ReleasedCount;

            List<int> a = ListPool<int>.Get();
            ListPool<int>.Release(a);
            List<int> b = ListPool<int>.Get();
            ListPool<int>.Release(b);

            Assert.AreEqual(rented + 2, CollectionPool<List<int>, int>.RentedCount);
            Assert.AreEqual(created + 1, CollectionPool<List<int>, int>.CreatedCount, "第二次租借应命中池。");
            Assert.AreEqual(released + 2, CollectionPool<List<int>, int>.ReleasedCount);
        }

        [Test]
        public void StringBuilderPool_GetStringAndRelease()
        {
            StringBuilder sb = StringBuilderPool.Get();
            sb.Append("ab").Append(12);
            string s = StringBuilderPool.GetStringAndRelease(sb);
            Assert.AreEqual("ab12", s);
            Assert.AreSame(sb, StringBuilderPool.Get());
            Assert.AreEqual(0, sb.Length);
        }

        [Test]
        public void StringBuilderPool_DropsOversizedBuilders()
        {
            StringBuilder big = StringBuilderPool.Get();
            big.Append('x', StringBuilderPool.MaxRetainedCapacity + 1);
            StringBuilderPool.Release(big);
            Assert.AreNotSame(big, StringBuilderPool.Get(), "容量超限的 builder 不应回池。");
        }

        [Test]
        public void ListPool_GetRelease_SteadyState_DoesNotAllocate()
        {
            TestDelegate body = () =>
            {
                for (int i = 0; i < 32; i++)
                {
                    List<int> list;
                    using (ListPool<int>.Get(out list))
                    {
                        list.Add(i);
                    }
                }
            };
            body();   // 同一委托先执行一遍：首次 JIT / 泛型实例化会被计为分配（见 AllocProbeTests）
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void StringBuilderPool_GetRelease_SteadyState_DoesNotAllocate()
        {
            TestDelegate body = () =>
            {
                for (int i = 0; i < 32; i++)
                {
                    StringBuilder sb;
                    using (StringBuilderPool.Get(out sb))
                    {
                        sb.Append('c');
                    }
                }
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }
    }
}
