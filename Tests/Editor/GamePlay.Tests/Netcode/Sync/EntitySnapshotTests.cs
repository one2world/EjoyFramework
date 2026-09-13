//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Sync;

namespace EjoyFramework.GamePlay.Tests.Netcode.Sync
{
    public class EntitySnapshotTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Ctor_StoresIdAndValues()
        {
            var snap = new EntitySnapshot(11, new[] { 1f, 2f });
            Assert.AreEqual(11, snap.EntityId);
            Assert.AreEqual(2, snap.Values.Length);
            Assert.AreEqual(1f, snap.Values[0], Delta);
            Assert.AreEqual(2f, snap.Values[1], Delta);
        }

        [Test]
        public void Ctor_NullValues_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new EntitySnapshot(1, null));
        }

        [Test]
        public void LerpValues_EqualLength_LerpsEachComponent()
        {
            var result = SnapshotInterpolator.LerpValues(new[] { 0f, 10f, -4f }, new[] { 10f, 20f, 4f }, 0.5f);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(5f, result[0], Delta);
            Assert.AreEqual(15f, result[1], Delta);
            Assert.AreEqual(0f, result[2], Delta);
        }

        [Test]
        public void LerpValues_T0_ReturnsA()
        {
            var result = SnapshotInterpolator.LerpValues(new[] { 3f, 7f }, new[] { 100f, 200f }, 0f);
            Assert.AreEqual(3f, result[0], Delta);
            Assert.AreEqual(7f, result[1], Delta);
        }

        [Test]
        public void LerpValues_T1_ReturnsB()
        {
            var result = SnapshotInterpolator.LerpValues(new[] { 3f, 7f }, new[] { 100f, 200f }, 1f);
            Assert.AreEqual(100f, result[0], Delta);
            Assert.AreEqual(200f, result[1], Delta);
        }

        [Test]
        public void LerpValues_DifferentLength_LerpsPrefix_KeepsLongerTail()
        {
            // a 较短：公共前缀插值，b 的尾部原样保留，结果长度取较大值。
            var result = SnapshotInterpolator.LerpValues(new[] { 0f }, new[] { 10f, 99f, 7f }, 0.5f);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(5f, result[0], Delta, "公共前缀插值");
            Assert.AreEqual(99f, result[1], Delta, "较长一侧尾部原样保留");
            Assert.AreEqual(7f, result[2], Delta);
        }

        [Test]
        public void LerpValues_LongerA_KeepsATail()
        {
            var result = SnapshotInterpolator.LerpValues(new[] { 0f, 4f, 8f }, new[] { 10f }, 0.5f);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(5f, result[0], Delta);
            Assert.AreEqual(4f, result[1], Delta, "a 的尾部原样保留");
            Assert.AreEqual(8f, result[2], Delta);
        }
    }
}
