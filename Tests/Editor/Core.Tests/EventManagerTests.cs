//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Event;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// EventManager 单测（Phase 11.7）。
    /// EventPool 已有独立测试，本文件聚焦 Manager 层封装（订阅 / Fire / FireNow / Count / Check / TryUnsubscribe）。
    /// </summary>
    public class EventManagerTests
    {
        public sealed class TestEvent : GameEventArgs
        {
            public override int Id { get { return 9001; } }
            public int Payload;
            public override void Clear() { Payload = 0; }
        }

        public sealed class OtherEvent : GameEventArgs
        {
            public override int Id { get { return 9002; } }
            public override void Clear() { }
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void Subscribe_IncrementsHandlerCount()
        {
            var em = new EventManager();
            Assert.AreEqual(0, em.Count(9001));

            EventHandler<GameEventArgs> h = (s, e) => { };
            em.Subscribe(9001, h);

            Assert.AreEqual(1, em.Count(9001));
            Assert.IsTrue(em.Check(9001, h));
        }

        [Test]
        public void Unsubscribe_RemovesHandler()
        {
            var em = new EventManager();
            EventHandler<GameEventArgs> h = (s, e) => { };
            em.Subscribe(9001, h);

            em.Unsubscribe(9001, h);

            Assert.AreEqual(0, em.Count(9001));
            Assert.IsFalse(em.Check(9001, h));
        }

        [Test]
        public void TryUnsubscribe_MissingHandler_ReturnsFalse_NoThrow()
        {
            var em = new EventManager();
            EventHandler<GameEventArgs> h = (s, e) => { };
            Assert.IsFalse(em.TryUnsubscribe(9001, h));
        }

        [Test]
        public void TryUnsubscribe_PresentHandler_ReturnsTrue()
        {
            var em = new EventManager();
            EventHandler<GameEventArgs> h = (s, e) => { };
            em.Subscribe(9001, h);

            Assert.IsTrue(em.TryUnsubscribe(9001, h));
            Assert.AreEqual(0, em.Count(9001));
        }

        [Test]
        public void Fire_QueuedUntilUpdate()
        {
            var em = new EventManager();
            int hits = 0;
            em.Subscribe(9001, (s, e) => hits++);

            var ev = ReferencePool.Acquire<TestEvent>();
            em.Fire(this, ev);

            // Fire 是异步：还没 Update 时 handler 未被调用
            Assert.AreEqual(0, hits);

            em.Update(0.016f, 0.016f);
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void FireNow_ImmediatelyInvokesHandler()
        {
            var em = new EventManager();
            int hits = 0;
            em.Subscribe(9001, (s, e) => hits++);

            var ev = ReferencePool.Acquire<TestEvent>();
            em.FireNow(this, ev);

            Assert.AreEqual(1, hits);
        }

        [Test]
        public void EventCount_TracksQueuedEvents()
        {
            var em = new EventManager();
            em.Subscribe(9001, (s, e) => { });

            Assert.AreEqual(0, em.EventCount);
            em.Fire(this, ReferencePool.Acquire<TestEvent>());
            em.Fire(this, ReferencePool.Acquire<TestEvent>());
            Assert.AreEqual(2, em.EventCount);

            em.Update(0.016f, 0.016f);
            Assert.AreEqual(0, em.EventCount, "队列应在 Update 后清空");
        }

        [Test]
        public void SetDefaultHandler_CalledForUnsubscribedEventId()
        {
            var em = new EventManager();
            int defaultHits = 0;
            int defaultId = -1;
            em.SetDefaultHandler((s, e) => { defaultHits++; defaultId = e.Id; });

            // 没有人订阅 9002，使用默认 handler
            em.FireNow(this, ReferencePool.Acquire<OtherEvent>());

            Assert.AreEqual(1, defaultHits);
            Assert.AreEqual(9002, defaultId);
        }

        [Test]
        public void EventHandlerCount_AggregatesAcrossEventTypes()
        {
            var em = new EventManager();
            em.Subscribe(9001, (s, e) => { });
            em.Subscribe(9001, (s, e) => { });
            em.Subscribe(9002, (s, e) => { });

            Assert.AreEqual(3, em.EventHandlerCount);
        }

        [Test]
        public void Shutdown_ClearsPendingEventsAndHandlers()
        {
            var em = new EventManager();
            em.Subscribe(9001, (s, e) => { });
            em.Fire(this, ReferencePool.Acquire<TestEvent>());

            em.Shutdown();

            Assert.AreEqual(0, em.EventCount);
            Assert.AreEqual(0, em.EventHandlerCount);
        }
    }
}
