//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Notification;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// NotificationManager 单测。
    /// 验证：ScheduleLocal 转发到平台；Id==0 时自动分配；ScheduleAfter 计算未来 FireAt 并返回 id；
    /// CancelLocal/CancelAllLocal 转发；未注入平台时 IsSupported=false 且调用不抛；LocalNotification.After 计算 FireAt。
    /// </summary>
    public class NotificationManagerTests
    {
        /// <summary>记录调用的假平台。</summary>
        private sealed class FakePlatform : INotificationPlatform
        {
            public readonly List<LocalNotification> Scheduled = new List<LocalNotification>();
            public readonly List<int> Cancelled = new List<int>();
            public int CancelAllCount;
            public int ClearBadgeCount;
            public int RequestAuthCount;
            public int RegisterRemoteCount;
            public bool SupportedValue = true;

            public bool IsSupported { get { return SupportedValue; } }

            public void ScheduleLocal(LocalNotification n) { Scheduled.Add(n); }
            public void CancelLocal(int id) { Cancelled.Add(id); }
            public void CancelAllLocal() { CancelAllCount++; }
            public void RequestAuthorization(Action<bool> onResult) { RequestAuthCount++; onResult?.Invoke(true); }
            public void RegisterForRemote(Action<string> onToken, Action<string> onError) { RegisterRemoteCount++; onToken?.Invoke("token-123"); }
            public void ClearBadge() { ClearBadgeCount++; }
        }

        private sealed class ThrowingPlatform : INotificationPlatform
        {
            public bool IsSupported { get { return true; } }
            public void ScheduleLocal(LocalNotification n) { throw new InvalidOperationException("boom"); }
            public void CancelLocal(int id) { throw new InvalidOperationException("boom"); }
            public void CancelAllLocal() { throw new InvalidOperationException("boom"); }
            public void RequestAuthorization(Action<bool> onResult) { throw new InvalidOperationException("boom"); }
            public void RegisterForRemote(Action<string> onToken, Action<string> onError) { throw new InvalidOperationException("boom"); }
            public void ClearBadge() { throw new InvalidOperationException("boom"); }
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void ScheduleLocal_ForwardsToPlatform()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);

            var n = new LocalNotification { Id = 42, Title = "t", Body = "b", FireAtUnixMillis = 1000 };
            int id = nm.ScheduleLocal(n);

            Assert.AreEqual(1, platform.Scheduled.Count);
            Assert.AreSame(n, platform.Scheduled[0]);
            Assert.AreEqual(42, id, "已显式指定 id 时应原样返回");
            Assert.AreEqual(42, n.Id);
        }

        [Test]
        public void ScheduleLocal_AutoAssignsId_WhenZero()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);

            var a = new LocalNotification { Id = 0, Title = "a" };
            var b = new LocalNotification { Id = 0, Title = "b" };

            int idA = nm.ScheduleLocal(a);
            int idB = nm.ScheduleLocal(b);

            Assert.AreNotEqual(0, idA, "id==0 应被自动分配为非 0");
            Assert.AreNotEqual(0, idB);
            Assert.AreNotEqual(idA, idB, "自动分配的 id 应递增且唯一");
            Assert.AreEqual(idA, a.Id, "自动分配的 id 应回写到通知对象");
            Assert.AreEqual(idB, b.Id);
        }

        [Test]
        public void ScheduleAfter_ComputesFutureFireAt_AndReturnsId()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);

            long beforeMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int id = nm.ScheduleAfter("title", "body", 60.0);

            Assert.AreNotEqual(0, id, "ScheduleAfter 应返回自动分配的 id");
            Assert.AreEqual(1, platform.Scheduled.Count);

            LocalNotification scheduled = platform.Scheduled[0];
            Assert.AreEqual(id, scheduled.Id);
            Assert.AreEqual("title", scheduled.Title);
            Assert.AreEqual("body", scheduled.Body);
            // FireAt 应在 (now + 60s) 附近且明确处于未来。
            Assert.GreaterOrEqual(scheduled.FireAtUnixMillis, beforeMillis + 60000,
                "FireAt 应不早于 now + 60s");
            Assert.Greater(scheduled.FireAtUnixMillis, beforeMillis, "FireAt 应处于未来");
        }

        [Test]
        public void CancelLocal_ForwardsToPlatform()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);

            nm.CancelLocal(7);

            Assert.AreEqual(1, platform.Cancelled.Count);
            Assert.AreEqual(7, platform.Cancelled[0]);
        }

        [Test]
        public void CancelAllLocal_ForwardsToPlatform()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);

            nm.CancelAllLocal();

            Assert.AreEqual(1, platform.CancelAllCount);
        }

        [Test]
        public void NoPlatform_IsSupportedFalse_AndCallsDoNotThrow()
        {
            var nm = new NotificationManager();

            Assert.IsFalse(nm.IsSupported, "未注入平台时 IsSupported 应为 false");

            Assert.DoesNotThrow(() => nm.ScheduleLocal(new LocalNotification { Title = "x" }));
            Assert.DoesNotThrow(() => nm.ScheduleAfter("t", "b", 10.0));
            Assert.DoesNotThrow(() => nm.CancelLocal(1));
            Assert.DoesNotThrow(() => nm.CancelAllLocal());
            Assert.DoesNotThrow(() => nm.ClearBadge());
            Assert.DoesNotThrow(() => nm.RequestAuthorization());
            Assert.DoesNotThrow(() => nm.RegisterForRemote(null));
        }

        [Test]
        public void NoPlatform_RequestAuthorization_CallbacksFalse()
        {
            var nm = new NotificationManager();
            bool? result = null;
            nm.RequestAuthorization(granted => result = granted);

            Assert.AreEqual(false, result, "未注入平台时授权回调应为 false");
        }

        [Test]
        public void NoPlatform_RegisterForRemote_CallbacksError()
        {
            var nm = new NotificationManager();
            string token = null;
            string error = null;
            nm.RegisterForRemote(t => token = t, e => error = e);

            Assert.IsNull(token, "未注入平台时不应回传 token");
            Assert.IsNotNull(error, "未注入平台时应回传错误");
        }

        [Test]
        public void SetPlatformNull_FallsBackToNullPlatform()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform();
            nm.SetPlatform(platform);
            nm.SetPlatform(null);

            Assert.IsFalse(nm.IsSupported, "清除平台后应回退到内置空实现");
            nm.ScheduleLocal(new LocalNotification { Title = "after_clear" });
            Assert.AreEqual(0, platform.Scheduled.Count, "清除平台后不应再转发到旧平台");
        }

        [Test]
        public void PlatformThrows_CallsAreSwallowed()
        {
            var nm = new NotificationManager();
            nm.SetPlatform(new ThrowingPlatform());

            Assert.DoesNotThrow(() => nm.ScheduleLocal(new LocalNotification { Title = "x" }));
            Assert.DoesNotThrow(() => nm.CancelLocal(1));
            Assert.DoesNotThrow(() => nm.CancelAllLocal());
            Assert.DoesNotThrow(() => nm.ClearBadge());
            Assert.DoesNotThrow(() => nm.RequestAuthorization());
            Assert.DoesNotThrow(() => nm.RegisterForRemote(null));
        }

        [Test]
        public void IsSupported_ReflectsPlatformSupportedFlag()
        {
            var nm = new NotificationManager();
            var platform = new FakePlatform { SupportedValue = false };
            nm.SetPlatform(platform);
            Assert.IsFalse(nm.IsSupported);

            platform.SupportedValue = true;
            Assert.IsTrue(nm.IsSupported);
        }

        [Test]
        public void LocalNotification_After_ComputesFireAt()
        {
            long now = 1_000_000L;
            LocalNotification n = LocalNotification.After(5, "title", "body", 30.0, now);

            Assert.AreEqual(5, n.Id);
            Assert.AreEqual("title", n.Title);
            Assert.AreEqual("body", n.Body);
            Assert.AreEqual(now + 30000L, n.FireAtUnixMillis, "FireAt 应为 now + delay*1000");
            Assert.AreEqual("default", n.Channel, "Channel 默认应为 default");
        }
    }
}
