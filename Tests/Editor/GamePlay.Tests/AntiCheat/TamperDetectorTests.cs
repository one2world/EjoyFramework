//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.GamePlay.AntiCheat;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.AntiCheat
{
    /// <summary>
    /// 针对 <see cref="TamperDetector"/> 的注册、完整性检查、事件触发与清理语义测试。
    /// </summary>
    [TestFixture]
    public class TamperDetectorTests
    {
        [Test]
        public void Watch_IncrementsWatchCount()
        {
            TamperDetector detector = new TamperDetector();
            Assert.AreEqual(0, detector.WatchCount);

            detector.Watch(() => false);
            detector.Watch(() => false);
            Assert.AreEqual(2, detector.WatchCount);
        }

        [Test]
        public void Watch_Null_Throws()
        {
            TamperDetector detector = new TamperDetector();
            Assert.Throws<ArgumentNullException>(() => detector.Watch(null));
        }

        [Test]
        public void CheckIntegrity_AllClean_ReturnsFalse()
        {
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => false);
            detector.Watch(() => false);

            Assert.IsFalse(detector.CheckIntegrity());
        }

        [Test]
        public void CheckIntegrity_OneTripped_ReturnsTrue()
        {
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => false);
            detector.Watch(() => true);
            detector.Watch(() => false);

            Assert.IsTrue(detector.CheckIntegrity());
        }

        [Test]
        public void CheckIntegrity_FiresOnTamperDetected_OncePerTrippedCheck()
        {
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => true);
            detector.Watch(() => true);

            int fired = 0;
            detector.OnTamperDetected += () => fired++;

            Assert.IsTrue(detector.CheckIntegrity());
            // 即使多个探针报告篡改，单次检查也只触发一次事件。
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void CheckIntegrity_DoesNotFire_WhenClean()
        {
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => false);

            int fired = 0;
            detector.OnTamperDetected += () => fired++;

            Assert.IsFalse(detector.CheckIntegrity());
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void CheckIntegrity_WithObscuredProbe_DetectsRealTamper()
        {
            ObscuredInt gold = new ObscuredInt(1000);
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => gold.IsTampered);

            // 未篡改时干净。
            Assert.IsFalse(detector.CheckIntegrity());

            // 通过内部访问器模拟外部内存篡改隐藏字段。
            gold.m_Hidden = gold.m_Hidden ^ 0x10;
            // 注意：探针捕获的是局部变量 gold，闭包读取的是同一实例。
            bool tripped = false;
            TamperDetector detector2 = new TamperDetector();
            detector2.Watch(() => gold.IsTampered);
            detector2.OnTamperDetected += () => tripped = true;

            Assert.IsTrue(detector2.CheckIntegrity());
            Assert.IsTrue(tripped);
        }

        [Test]
        public void Clear_RemovesAllProbes()
        {
            TamperDetector detector = new TamperDetector();
            detector.Watch(() => true);
            detector.Watch(() => true);
            Assert.AreEqual(2, detector.WatchCount);

            detector.Clear();
            Assert.AreEqual(0, detector.WatchCount);
            // 清空后检查应为干净。
            Assert.IsFalse(detector.CheckIntegrity());
        }

        [Test]
        public void CheckIntegrity_EvaluatesAllProbes_NoShortCircuit()
        {
            TamperDetector detector = new TamperDetector();
            int probeBCalls = 0;

            detector.Watch(() => true);            // 第一个即报告篡改
            detector.Watch(() => { probeBCalls++; return false; });

            Assert.IsTrue(detector.CheckIntegrity());
            // 第二个探针仍应被求值（不短路），以便其副作用执行。
            Assert.AreEqual(1, probeBCalls);
        }
    }
}
