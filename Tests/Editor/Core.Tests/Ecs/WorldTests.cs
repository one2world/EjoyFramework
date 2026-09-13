using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Ecs;

namespace EjoyFramework.Tests.Ecs
{
    public struct Position { public float X; }
    public struct Velocity { public float X; }
    public struct Health { public int Value; }
    public struct Disabled { }

    public class WorldTests
    {
        [Test]
        public void RecycledEntity_RejectsOldHandleAndClearsComponents()
        {
            using var world = new World(1);
            var old = world.CreateEntity();
            world.Set(old, new Health { Value = 10 });
            world.DestroyEntity(old);
            var next = world.CreateEntity();
            Assert.That(next.Index, Is.EqualTo(old.Index));
            Assert.That(next.Version, Is.Not.EqualTo(old.Version));
            Assert.That(world.IsAlive(old), Is.False);
            Assert.That(world.Has<Health>(next), Is.False);
            Assert.Throws<InvalidOperationException>(() => world.Set(old, new Health()));
            Assert.Throws<InvalidOperationException>(() => world.DestroyEntity(old));
            Assert.That(world.EntityCount, Is.EqualTo(1));
        }

        [Test]
        public void Worlds_RejectEachOthersHandlesAndDefaultEntity()
        {
            using var first = new World();
            using var second = new World();
            var a = first.CreateEntity();
            var b = second.CreateEntity();
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(second.IsAlive(a), Is.False);
            Assert.That(first.IsAlive(default), Is.False);
            Assert.Throws<InvalidOperationException>(() => second.Set(a, new Health()));
            Assert.Throws<InvalidOperationException>(() => first.Get<Health>(default));
        }

        [Test]
        public void Components_GrowAndRemoveWithoutCorruptingOtherEntities()
        {
            using var world = new World(1);
            var entities = new Entity[1024];
            for (int i = 0; i < entities.Length; i++)
            {
                entities[i] = world.CreateEntity();
                world.Set(entities[i], new Health { Value = i });
            }
            for (int i = 0; i < entities.Length; i += 2) world.Remove<Health>(entities[i]);
            for (int i = 1; i < entities.Length; i += 2)
                Assert.That(world.Get<Health>(entities[i]).Value, Is.EqualTo(i));
            world.Get<Health>(entities[1]).Value = 42;
            Assert.That(world.TryGet(entities[1], out Health health), Is.True);
            Assert.That(health.Value, Is.EqualTo(42));
            Assert.That(world.TryGet(entities[0], out Health _), Is.False);
            Assert.That(world.Remove<Health>(entities[0]), Is.False);
        }

        [Test]
        public void RandomOperations_MatchIndependentDictionaryModel()
        {
            using var world = new World(2);
            var expected = new Dictionary<Entity, int?>();
            var active = new List<Entity>();
            var random = new Random(12345);
            for (int step = 0; step < 10000; step++)
            {
                int operation = random.Next(4);
                if (active.Count == 0 || operation == 0)
                {
                    var created = world.CreateEntity();
                    active.Add(created);
                    expected.Add(created, null);
                }
                else
                {
                    int index = random.Next(active.Count);
                    var entity = active[index];
                    if (operation == 1)
                    {
                        world.DestroyEntity(entity);
                        active.RemoveAt(index);
                        expected.Remove(entity);
                        Assert.That(world.IsAlive(entity), Is.False);
                    }
                    else if (operation == 2)
                    {
                        world.Set(entity, new Health { Value = step });
                        expected[entity] = step;
                    }
                    else
                    {
                        world.Remove<Health>(entity);
                        expected[entity] = null;
                    }
                }
                if (step % 100 != 0) continue;
                Assert.That(world.EntityCount, Is.EqualTo(expected.Count));
                int components = 0;
                foreach (var pair in expected)
                {
                    Assert.That(world.TryGet(pair.Key, out Health actual), Is.EqualTo(pair.Value.HasValue));
                    if (!pair.Value.HasValue) continue;
                    components++;
                    Assert.That(actual.Value, Is.EqualTo(pair.Value.Value));
                }
                Assert.That(world.Query().WithAll<Health>().Count, Is.EqualTo(components));
            }
        }

        [Test]
        public void DisposedWorld_InvalidatesQueriesAndBuffers()
        {
            var world = new World();
            var query = world.Query();
            using var commands = new CommandBuffer(world);
            var entity = commands.CreateEntity();
            world.Dispose();
            world.Dispose();
            Assert.That(world.IsAlive(entity), Is.False);
            Assert.Throws<ObjectDisposedException>(() => world.CreateEntity());
            Assert.Throws<ObjectDisposedException>(() => { var unused = query.Count; });
            Assert.Throws<ObjectDisposedException>(() => commands.Playback());
        }
    }
}
