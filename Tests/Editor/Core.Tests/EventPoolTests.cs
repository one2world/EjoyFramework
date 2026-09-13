//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using EjoyFramework.Core.Event;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class EventPoolTests
    {
        public sealed class PingEvent : GameEventArgs
        {
            public override int Id { get { return 1001; } }
            public int Payload;
            public override void Clear() { Payload = 0; }
        }

        public sealed class PongEvent : GameEventArgs
        {
            public override int Id { get { return 1002; } }
            public override void Clear() { }
        }

        [SetUp]
        public void Setup()
        {
            // 标记主线程：让 Framework.EnsureMainThread 不会因为不同 setup 报错
            Framework.MarkMainThread();
        }

        [Test]
        public void Fire_DispatchedOnUpdate()
        {
            var em = new EventManager();
            int hits = 0;
            EventHandler<GameEventArgs> handler = (s, e) => { if (e is PingEvent) hits++; };
            em.Subscribe(1001, handler);

            var ping = ReferencePool.Acquire<PingEvent>();
            em.Fire(this, ping);

            // 还没 Update，不会派发
            Assert.AreEqual(0, hits);

            em.Update(0.016f, 0.016f);
            Assert.AreEqual(1, hits);

            em.Unsubscribe(1001, handler);
            em.Shutdown();
        }

        [Test]
        public void TryUnsubscribe_NotExisting_ReturnsFalseInsteadOfThrowing()
        {
            var em = new EventManager();
            EventHandler<GameEventArgs> handler = (s, e) => { };
            Assert.IsFalse(em.TryUnsubscribe(9999, handler));
            em.Shutdown();
        }

        [Test]
        public void HandlerThrowing_DoesNotBreakOtherHandlers()
        {
            var em = new EventManager();
            int hitsAfterThrow = 0;

            EventHandler<GameEventArgs> bad = (s, e) => { throw new InvalidOperationException("bad handler"); };
            EventHandler<GameEventArgs> good = (s, e) => { hitsAfterThrow++; };

            em.Subscribe(1001, bad);
            em.Subscribe(1001, good);

            var ping = ReferencePool.Acquire<PingEvent>();
            em.FireNow(this, ping);

            // 后续 handler 仍被调用（异常被吞）
            Assert.AreEqual(1, hitsAfterThrow);

            em.TryUnsubscribe(1001, bad);
            em.TryUnsubscribe(1001, good);
            em.Shutdown();
        }

        [Test]
        public void HandlerSelfUnsubscribesDuringDispatch_DoesNotSkipNext()
        {
            var em = new EventManager();
            int seq = 0;
            int aOrder = 0, bOrder = 0;

            EventHandler<GameEventArgs> a = null;
            a = (s, e) =>
            {
                aOrder = ++seq;
                em.TryUnsubscribe(1001, a);
            };
            EventHandler<GameEventArgs> b = (s, e) =>
            {
                bOrder = ++seq;
            };

            em.Subscribe(1001, a);
            em.Subscribe(1001, b);

            var ping = ReferencePool.Acquire<PingEvent>();
            em.FireNow(this, ping);

            Assert.AreEqual(1, aOrder);
            Assert.AreEqual(2, bOrder, "second handler should still fire even when first unsubscribed itself");

            em.TryUnsubscribe(1001, b);
            em.Shutdown();
        }

        [Test]
        public void Fire_FromBackgroundThread_DispatchedOnUpdate()
        {
            var em = new EventManager();
            int hits = 0;
            EventHandler<GameEventArgs> handler = (s, e) => Interlocked.Increment(ref hits);
            em.Subscribe(1001, handler);

            const int N = 200;
            Task.Run(() =>
            {
                for (int i = 0; i < N; i++)
                {
                    var p = ReferencePool.Acquire<PingEvent>();
                    em.Fire(this, p);
                }
            }).Wait();

            em.Update(0.016f, 0.016f);
            Assert.AreEqual(N, hits);

            em.Unsubscribe(1001, handler);
            em.Shutdown();
        }

        [Test]
        public void Fire_UsesStableReadonlyQueueLock()
        {
            var pool = new EventPool<GameEventArgs>(EventPoolMode.AllowNoHandler);
            FieldInfo lockField = typeof(EventPool<GameEventArgs>).GetField(
                "m_QueueLock",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(lockField,
                "双缓冲队列不能以可交换的 m_PendingEvents 字段本身作为锁；必须使用独立稳定锁。 ");
            Assert.IsTrue(lockField.IsInitOnly, "队列锁必须 readonly，避免运行时被替换。 ");

            object queueLock = lockField.GetValue(pool);
            Task fireTask = null;
            var started = new ManualResetEventSlim(false);
            Monitor.Enter(queueLock);
            try
            {
                fireTask = Task.Run(() =>
                {
                    var ping = ReferencePool.Acquire<PingEvent>();
                    started.Set();
                    pool.Fire(this, ping);
                });

                Assert.IsTrue(started.Wait(1000), "后台 Fire 任务应进入调用点。 ");
                Assert.IsFalse(fireTask.Wait(100),
                    "持有稳定队列锁时，后台 Fire 必须等待，不能绕过同步写入当前 pending queue。 ");
            }
            finally
            {
                Monitor.Exit(queueLock);
            }

            Assert.IsTrue(fireTask.Wait(1000), "释放队列锁后 Fire 应完成。 ");
            started.Dispose();
            pool.Clear();
        }
    }
}
