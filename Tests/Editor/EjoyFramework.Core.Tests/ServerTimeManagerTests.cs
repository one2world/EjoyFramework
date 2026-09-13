//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.ServerTime;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// ServerTimeManager 单测。
    /// 验证：Sync 后 NowUnixMillis ≈ serverBase（+ 极小流逝量）、Synced=true、偏移量符号正确、未同步回退本地时钟。
    /// </summary>
    public class ServerTimeManagerTests
    {
        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void BeforeSync_SyncedFalse_OffsetZero()
        {
            var st = new ServerTimeManager();

            Assert.IsFalse(st.Synced);
            Assert.AreEqual(0L, st.OffsetMillis);

            // 未同步：NowUnixMillis 回退为本地系统时钟，应接近 DateTimeOffset.UtcNow。
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.That(st.NowUnixMillis, Is.EqualTo(localNow).Within(2000L));
        }

        [Test]
        public void Sync_NowApproximatesServerBase()
        {
            var st = new ServerTimeManager();

            // 选一个与当前本地时钟明显不同的服务器锚点（领先约 1 小时）。
            long localBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long serverBase = localBefore + 3_600_000L;

            st.Sync(serverBase);

            Assert.IsTrue(st.Synced);

            // 同步后立即读取，NowUnixMillis 应几乎等于 serverBase（只差极小的执行流逝）。
            long now = st.NowUnixMillis;
            Assert.That(now, Is.GreaterThanOrEqualTo(serverBase));
            Assert.That(now, Is.LessThan(serverBase + 5000L), "流逝量应远小于 5s");
        }

        [Test]
        public void Sync_OffsetSignCorrect_ServerAhead()
        {
            var st = new ServerTimeManager();

            long localBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long serverBase = localBefore + 3_600_000L; // 服务器领先本地 ~1h

            st.Sync(serverBase);

            // offset = serverBase - localBaseAtSync，服务器领先 → 正且接近 +1h。
            Assert.That(st.OffsetMillis, Is.GreaterThan(0L));
            Assert.That(st.OffsetMillis, Is.EqualTo(3_600_000L).Within(5000L));
        }

        [Test]
        public void Sync_OffsetSignCorrect_ServerBehind()
        {
            var st = new ServerTimeManager();

            long localBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long serverBase = localBefore - 3_600_000L; // 服务器落后本地 ~1h

            st.Sync(serverBase);

            Assert.That(st.OffsetMillis, Is.LessThan(0L));
            Assert.That(st.OffsetMillis, Is.EqualTo(-3_600_000L).Within(5000L));
        }

        [Test]
        public void UtcNow_MatchesNowUnixMillis()
        {
            var st = new ServerTimeManager();
            long serverBase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000_000L;
            st.Sync(serverBase);

            long expected = st.NowUnixMillis;
            long actual = new DateTimeOffset(st.UtcNow, TimeSpan.Zero).ToUnixTimeMilliseconds();
            Assert.That(actual, Is.EqualTo(expected).Within(50L));
        }

        [Test]
        public void Shutdown_ResetsState()
        {
            var st = new ServerTimeManager();
            st.Sync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100_000L);
            Assert.IsTrue(st.Synced);

            st.Shutdown();

            Assert.IsFalse(st.Synced);
            Assert.AreEqual(0L, st.OffsetMillis);
        }
    }
}
