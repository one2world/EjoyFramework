using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using EjoyFramework.Core.Ecs;
using NUnit.Framework;

namespace EjoyFramework.EcsLifetimeTests
{
    public class DisposalTests
    {
        private struct Payload { public long Value; }

        [Test]
        public void RetainedQuery_DoesNotRetainComponentStorageAfterWorldDisposal()
        {
            var world = new World();
            var query = world.Query().WithAll<Payload>();
            var storage = AllocateAndObserveStorage(world);
            world.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(storage.IsAlive, Is.False, "A disposed world must release storage even if a caller caches its Query.");
            GC.KeepAlive(query);
            GC.KeepAlive(world);
        }

        // Observe the allocation directly: process memory counters cannot reliably prove collection.
        // NoInlining prevents the observed array from being kept alive by this test's stack frame.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference AllocateAndObserveStorage(World world)
        {
            for (int i = 0; i < 256; i++) world.Set(world.CreateEntity(), new Payload { Value = i });
            var pools = (IDictionary)typeof(World).GetField("m_Pools", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(world);
            var pool = pools[typeof(Payload)];
            var data = pool.GetType().GetField("m_Data", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pool);
            return new WeakReference(data);
        }
    }
}
