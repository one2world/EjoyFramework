//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Netcode.Sync;

namespace EjoyFramework.GamePlay.Tests.Netcode.Sync
{
    public class WorldSnapshotTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Ctor_StoresTickAndServerTime()
        {
            var world = new WorldSnapshot(42u, 1.25d);
            Assert.AreEqual(42u, world.Tick);
            Assert.AreEqual(1.25d, world.ServerTimeSec, 1e-9d);
            Assert.AreEqual(0, world.Entities.Count);
        }

        [Test]
        public void AddEntity_ThenTryGet_ReturnsStoredValues()
        {
            var world = new WorldSnapshot(1u, 0d);
            world.AddEntity(new EntitySnapshot(7, new[] { 1f, 2f, 3f }));

            EntitySnapshot got;
            Assert.IsTrue(world.TryGetEntity(7, out got));
            Assert.AreEqual(7, got.EntityId);
            Assert.AreEqual(3, got.Values.Length);
            Assert.AreEqual(1f, got.Values[0], Delta);
            Assert.AreEqual(2f, got.Values[1], Delta);
            Assert.AreEqual(3f, got.Values[2], Delta);
        }

        [Test]
        public void TryGet_MissingEntity_ReturnsFalse()
        {
            var world = new WorldSnapshot(1u, 0d);
            world.AddEntity(new EntitySnapshot(1, new[] { 0f }));

            EntitySnapshot got;
            Assert.IsFalse(world.TryGetEntity(999, out got));
        }

        [Test]
        public void Entities_ReflectsAllAdded_InInsertionOrder()
        {
            var world = new WorldSnapshot(1u, 0d);
            world.AddEntity(new EntitySnapshot(10, new[] { 0f }));
            world.AddEntity(new EntitySnapshot(20, new[] { 0f }));
            world.AddEntity(new EntitySnapshot(30, new[] { 0f }));

            Assert.AreEqual(3, world.Entities.Count);
            Assert.AreEqual(10, world.Entities[0].EntityId);
            Assert.AreEqual(20, world.Entities[1].EntityId);
            Assert.AreEqual(30, world.Entities[2].EntityId);
        }

        [Test]
        public void AddEntity_DuplicateId_Overwrites_NoDuplicateEntry()
        {
            var world = new WorldSnapshot(1u, 0d);
            world.AddEntity(new EntitySnapshot(5, new[] { 1f }));
            world.AddEntity(new EntitySnapshot(5, new[] { 9f }));

            Assert.AreEqual(1, world.Entities.Count, "同 id 应覆盖而非追加");
            EntitySnapshot got;
            Assert.IsTrue(world.TryGetEntity(5, out got));
            Assert.AreEqual(9f, got.Values[0], Delta, "应保留后写入的值");
        }
    }
}
