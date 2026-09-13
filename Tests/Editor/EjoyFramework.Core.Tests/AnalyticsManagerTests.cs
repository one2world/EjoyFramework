//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Analytics;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// AnalyticsManager 单测。
    /// 验证：注入后端后 Track / SetUserProperty 正确转发；后端抛异常被吞掉不外抛。
    /// </summary>
    public class AnalyticsManagerTests
    {
        /// <summary>记录调用的假后端。</summary>
        private sealed class FakeBackend : IAnalyticsBackend
        {
            public string LastEvent;
            public IDictionary<string, object> LastProps;
            public int TrackCount;

            public string LastUserKey;
            public object LastUserValue;
            public int UserPropCount;

            public void OnTrack(string e, IDictionary<string, object> p)
            {
                LastEvent = e; LastProps = p; TrackCount++;
            }

            public void OnUserProperty(string k, object v)
            {
                LastUserKey = k; LastUserValue = v; UserPropCount++;
            }
        }

        private sealed class ThrowingBackend : IAnalyticsBackend
        {
            public void OnTrack(string e, IDictionary<string, object> p) { throw new System.InvalidOperationException("boom"); }
            public void OnUserProperty(string k, object v) { throw new System.InvalidOperationException("boom"); }
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void Track_ForwardsToBackend()
        {
            var am = new AnalyticsManager();
            var backend = new FakeBackend();
            am.SetBackend(backend);

            var props = new Dictionary<string, object> { { "level", 3 } };
            am.Track("level_up", props);

            Assert.AreEqual(1, backend.TrackCount);
            Assert.AreEqual("level_up", backend.LastEvent);
            Assert.AreSame(props, backend.LastProps);
        }

        [Test]
        public void SetUserProperty_ForwardsToBackend()
        {
            var am = new AnalyticsManager();
            var backend = new FakeBackend();
            am.SetBackend(backend);

            am.SetUserProperty("vip", true);

            Assert.AreEqual(1, backend.UserPropCount);
            Assert.AreEqual("vip", backend.LastUserKey);
            Assert.AreEqual(true, backend.LastUserValue);
        }

        [Test]
        public void NoBackend_Track_DoesNotThrow()
        {
            var am = new AnalyticsManager();
            Assert.DoesNotThrow(() => am.Track("event_without_backend"));
            Assert.DoesNotThrow(() => am.SetUserProperty("k", "v"));
        }

        [Test]
        public void BackendThrows_Track_IsSwallowed()
        {
            var am = new AnalyticsManager();
            am.SetBackend(new ThrowingBackend());

            Assert.DoesNotThrow(() => am.Track("e"));
            Assert.DoesNotThrow(() => am.SetUserProperty("k", "v"));
        }

        [Test]
        public void SetBackendNull_ClearsBackend()
        {
            var am = new AnalyticsManager();
            var backend = new FakeBackend();
            am.SetBackend(backend);
            am.SetBackend(null);

            am.Track("after_clear");

            Assert.AreEqual(0, backend.TrackCount, "清除后端后不应再转发");
        }

        [Test]
        public void Track_EmptyEventName_NoForward()
        {
            var am = new AnalyticsManager();
            var backend = new FakeBackend();
            am.SetBackend(backend);

            am.Track("");
            am.Track(null);

            Assert.AreEqual(0, backend.TrackCount);
        }
    }
}
