//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.Production;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Production
{
    /// <summary>
    /// 针对 <see cref="ResourceProducer"/> 线性累积、容量封顶、领取重定基、速率携带与离线累积行为的单元测试。
    /// </summary>
    [TestFixture]
    public class ResourceProducerTests
    {
        private long m_Now;

        private Func<long> Provider
        {
            get { return () => m_Now; }
        }

        [SetUp]
        public void SetUp()
        {
            m_Now = 0L;
        }

        [Test]
        public void Pending_AccruesLinearly_WithElapsedTime()
        {
            ResourceProducer producer = new ResourceProducer(2d, 100d, Provider);

            m_Now = 1000L; // 1 秒
            Assert.AreEqual(2d, producer.Pending, 1e-9);

            m_Now = 5000L; // 5 秒
            Assert.AreEqual(10d, producer.Pending, 1e-9);
        }

        [Test]
        public void Pending_CapsAtCapacity_AndIsFull()
        {
            ResourceProducer producer = new ResourceProducer(10d, 25d, Provider);

            m_Now = 2000L; // 20 < 25，未满
            Assert.AreEqual(20d, producer.Pending, 1e-9);
            Assert.IsFalse(producer.IsFull);

            m_Now = 10000L; // 100，封顶到 25
            Assert.AreEqual(25d, producer.Pending, 1e-9);
            Assert.IsTrue(producer.IsFull);
        }

        [Test]
        public void Collect_DrainsAndRebaselines()
        {
            ResourceProducer producer = new ResourceProducer(5d, 1000d, Provider);

            m_Now = 4000L; // 20
            double collected = producer.Collect();

            Assert.AreEqual(20d, collected, 1e-9);
            Assert.AreEqual(0d, producer.Pending, 1e-9); // 领取后立即清零

            m_Now = 6000L; // 自领取后再过 2 秒 → 10
            Assert.AreEqual(10d, producer.Pending, 1e-9);
        }

        [Test]
        public void SetRate_CarriesPending_AndAppliesNewRate()
        {
            ResourceProducer producer = new ResourceProducer(1d, 1000d, Provider);

            m_Now = 10000L; // 10（旧速率 1/s × 10 秒）
            Assert.AreEqual(10d, producer.Pending, 1e-9);

            producer.SetRate(3d); // 封存 10，重定基线
            Assert.AreEqual(3d, producer.RatePerSec, 1e-9);
            Assert.AreEqual(10d, producer.Pending, 1e-9); // 切换瞬间仍为携带量 10

            m_Now = 12000L; // 再过 2 秒 × 3/s = 6，叠加携带 10 → 16
            Assert.AreEqual(16d, producer.Pending, 1e-9);
        }

        [Test]
        public void SetRate_CarriedPlusNew_StillCapsAtCapacity()
        {
            ResourceProducer producer = new ResourceProducer(10d, 50d, Provider);

            m_Now = 4000L; // 40
            producer.SetRate(20d); // 携带 40
            Assert.AreEqual(40d, producer.Pending, 1e-9);

            m_Now = 5000L; // 再过 1 秒 × 20 = 20，40+20=60，封顶到 50
            Assert.AreEqual(50d, producer.Pending, 1e-9);
            Assert.IsTrue(producer.IsFull);
        }

        [Test]
        public void OfflineAccrual_JumpForward_YieldsCappedAmount()
        {
            ResourceProducer producer = new ResourceProducer(1d, 3600d, Provider);

            // 模拟离线：now 一次性跳到 2 小时后。
            m_Now = 2L * 3600L * 1000L; // 7200 秒
            Assert.AreEqual(3600d, producer.Pending, 1e-9); // 封顶
            Assert.IsTrue(producer.IsFull);

            double collected = producer.Collect();
            Assert.AreEqual(3600d, collected, 1e-9);
            Assert.AreEqual(0d, producer.Pending, 1e-9);
        }

        [Test]
        public void OnFull_FiresOnce_WhenPendingFirstReachesCapacity()
        {
            ResourceProducer producer = new ResourceProducer(10d, 50d, Provider);
            int fireCount = 0;
            producer.OnFull += _ => fireCount++;

            m_Now = 4000L; // 40，未满
            double _p1 = producer.Pending;
            Assert.AreEqual(0, fireCount);

            m_Now = 5000L; // 50，恰满 → 触发一次
            double _p2 = producer.Pending;
            Assert.AreEqual(1, fireCount);

            m_Now = 9000L; // 90 仍封顶到 50，不重复触发
            double _p3 = producer.Pending;
            Assert.AreEqual(1, fireCount);
        }

        [Test]
        public void OnFull_RearmsAfterCollect()
        {
            ResourceProducer producer = new ResourceProducer(10d, 50d, Provider);
            int fireCount = 0;
            producer.OnFull += _ => fireCount++;

            m_Now = 5000L; // 满 → 触发 1
            Assert.IsTrue(producer.IsFull);
            Assert.AreEqual(1, fireCount);

            producer.Collect(); // 重置基线，重新武装
            m_Now = 10000L; // 自领取后再过 5 秒 = 50，再次满 → 触发 2
            Assert.IsTrue(producer.IsFull);
            Assert.AreEqual(2, fireCount);
        }

        [Test]
        public void SetCapacity_ExpandsAndAllowsFurtherAccrual()
        {
            ResourceProducer producer = new ResourceProducer(10d, 50d, Provider);

            m_Now = 6000L; // 60 封顶到 50
            Assert.AreEqual(50d, producer.Pending, 1e-9);

            producer.SetCapacity(100d); // 携带 50，上限提高
            Assert.AreEqual(50d, producer.Pending, 1e-9);

            m_Now = 9000L; // 再过 3 秒 × 10 = 30，50+30 = 80（< 100）
            Assert.AreEqual(80d, producer.Pending, 1e-9);
            Assert.IsFalse(producer.IsFull);
        }

        [Test]
        public void SetCapacity_Shrink_ClampsPendingDown()
        {
            ResourceProducer producer = new ResourceProducer(10d, 100d, Provider);

            m_Now = 8000L; // 80
            producer.SetCapacity(30d); // 携带 min(100,80)=80，但上限变 30 → 立即封顶到 30
            Assert.AreEqual(30d, producer.Pending, 1e-9);
            Assert.IsTrue(producer.IsFull);
        }

        [Test]
        public void Constructor_NegativeRate_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceProducer(-1d, 10d, Provider));
        }

        [Test]
        public void Constructor_NegativeCapacity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceProducer(1d, -10d, Provider));
        }

        [Test]
        public void Constructor_NullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ResourceProducer(1d, 10d, null));
        }

        [Test]
        public void SetRate_Negative_Throws()
        {
            ResourceProducer producer = new ResourceProducer(1d, 10d, Provider);
            Assert.Throws<ArgumentOutOfRangeException>(() => producer.SetRate(-2d));
        }

        [Test]
        public void ClockGoesBackward_BelowBaseline_TreatedAsNoAccrual()
        {
            ResourceProducer producer = new ResourceProducer(1d, 100d, Provider);

            m_Now = 5000L; // 5
            Assert.AreEqual(5d, producer.Collect(), 1e-9); // 领取并把基线重定到 5000

            m_Now = 2000L; // now 回退到基线（5000）之前 → 累积按 0 处理
            Assert.AreEqual(0d, producer.Pending, 1e-9);

            m_Now = 7000L; // 回到基线之后 2 秒 → 2
            Assert.AreEqual(2d, producer.Pending, 1e-9);
        }
    }
}
