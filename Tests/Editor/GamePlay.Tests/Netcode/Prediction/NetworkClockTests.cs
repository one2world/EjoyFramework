//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Prediction;

namespace EjoyFramework.GamePlay.Tests.Netcode.Prediction
{
    /// <summary>
    /// <see cref="NetworkClock"/> 的单元测试：偏移估计、渲染时间、RTT 平滑收敛与 tick 换算。
    /// </summary>
    public class NetworkClockTests
    {
        private const double Delta = 1e-6;

        [Test]
        public void Constructor_DefaultRate_30Hz()
        {
            var clock = new NetworkClock();
            Assert.AreEqual(30d, clock.TickRateHz, Delta);
            Assert.AreEqual(1d / 30d, clock.TickIntervalSec, Delta);
        }

        [Test]
        public void Constructor_CustomRate_SetsInterval()
        {
            var clock = new NetworkClock(60d);
            Assert.AreEqual(60d, clock.TickRateHz, Delta);
            Assert.AreEqual(1d / 60d, clock.TickIntervalSec, Delta);
        }

        [Test]
        public void Constructor_NonPositiveRate_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkClock(0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkClock(-5d));
        }

        [Test]
        public void EstimatedServerTime_BeforeAnySample_EqualsLocal()
        {
            var clock = new NetworkClock();
            Assert.AreEqual(123.45d, clock.EstimatedServerTimeSec(123.45d), Delta);
        }

        [Test]
        public void OnServerTimeReceived_FirstSample_OffsetIncludesHalfRtt()
        {
            var clock = new NetworkClock();
            // 本地时间 10，服务器时间 100，rtt=0.2 => 单程 0.1。
            // 样本到达此刻服务器约为 100.1，offset = 100.1 - 10 = 90.1。
            clock.OnServerTimeReceived(100d, 10d, 0.2d);

            Assert.AreEqual(100.1d, clock.EstimatedServerTimeSec(10d), Delta);
            Assert.AreEqual(0.2d, clock.SmoothedRttSec, Delta);
        }

        [Test]
        public void EstimatedServerTime_TracksLocalDelta()
        {
            var clock = new NetworkClock();
            clock.OnServerTimeReceived(100d, 10d, 0d); // offset = 90
            // 本地推进到 15，估计服务器 = 15 + 90 = 105。
            Assert.AreEqual(105d, clock.EstimatedServerTimeSec(15d), Delta);
        }

        [Test]
        public void RenderTime_IsEstimatedMinusInterpolationDelay()
        {
            var clock = new NetworkClock();
            clock.OnServerTimeReceived(100d, 10d, 0d); // offset 90 => est at local 10 == 100
            double est = clock.EstimatedServerTimeSec(10d);
            double render = clock.RenderTimeSec(10d, 0.1d);

            Assert.AreEqual(100d, est, Delta);
            Assert.AreEqual(est - 0.1d, render, Delta);
            Assert.AreEqual(99.9d, render, Delta);
        }

        [Test]
        public void SmoothedRtt_EmaConverges()
        {
            var clock = new NetworkClock();
            // 首个样本直接为初值 0.1。
            clock.OnServerTimeReceived(0d, 0d, 0.1d);
            Assert.AreEqual(0.1d, clock.SmoothedRttSec, Delta);

            // 持续喂入稳定的 0.3，EMA 应单调逼近 0.3。
            double prev = clock.SmoothedRttSec;
            for (int i = 0; i < 200; i++)
            {
                clock.OnServerTimeReceived(0d, 0d, 0.3d);
                Assert.GreaterOrEqual(clock.SmoothedRttSec, prev - Delta); // 单调不减地逼近
                prev = clock.SmoothedRttSec;
            }

            Assert.AreEqual(0.3d, clock.SmoothedRttSec, 1e-3);
        }

        [Test]
        public void SmoothedOffset_EmaConverges()
        {
            var clock = new NetworkClock();
            // 首样本 offset = 100 - 0 = 100（rtt=0）。
            clock.OnServerTimeReceived(100d, 0d, 0d);
            Assert.AreEqual(100d, clock.EstimatedServerTimeSec(0d), Delta);

            // 真实 offset 跳到 200，多次样本后 EMA 应逼近 200。
            for (int i = 0; i < 300; i++)
            {
                clock.OnServerTimeReceived(200d, 0d, 0d);
            }

            Assert.AreEqual(200d, clock.EstimatedServerTimeSec(0d), 1e-3);
        }

        [Test]
        public void OnServerTimeReceived_NegativeRtt_ClampedToZero()
        {
            var clock = new NetworkClock();
            clock.OnServerTimeReceived(100d, 10d, -1d);
            // rtt 被钳为 0 => 单程 0 => offset = 90。
            Assert.AreEqual(0d, clock.SmoothedRttSec, Delta);
            Assert.AreEqual(100d, clock.EstimatedServerTimeSec(10d), Delta);
        }

        [Test]
        public void TimeToTick_FloorsToTickIndex()
        {
            var clock = new NetworkClock(30d); // interval = 1/30 ≈ 0.03333
            // serverTime 1.0s => 1.0 / (1/30) = 30 => tick 30。
            Assert.AreEqual(30u, clock.TimeToTick(1d));
            // 0.05s => 0.05*30 = 1.5 => floor => 1。
            Assert.AreEqual(1u, clock.TimeToTick(0.05d));
        }

        [Test]
        public void TimeToTick_ZeroOrNegative_ReturnsZero()
        {
            var clock = new NetworkClock(30d);
            Assert.AreEqual(0u, clock.TimeToTick(0d));
            Assert.AreEqual(0u, clock.TimeToTick(-5d));
        }

        [Test]
        public void TimeToTick_60Hz_Doubles()
        {
            var clock = new NetworkClock(60d);
            // 1s @ 60Hz => 60 ticks。
            Assert.AreEqual(60u, clock.TimeToTick(1d));
        }
    }
}
