using System;
using NUnit.Framework;
using EjoyFramework.Core.Ecs;

namespace EjoyFramework.Tests.Ecs
{
    public class QueryTests
    {
        [Test]
        public void Query_AllAnyNoneAndTypedAccessSelectCorrectEntities()
        {
            using var world = new World();
            var moving = world.CreateEntity();
            world.Set(moving, new Position { X = 2 });
            world.Set(moving, new Velocity { X = 3 });
            var idle = world.CreateEntity();
            world.Set(idle, new Position { X = 10 });
            world.Set(idle, new Health { Value = 10 });
            var disabled = world.CreateEntity();
            world.Set(disabled, new Position());
            world.Set(disabled, new Disabled());
            var all = world.Query().WithAll<Position>();
            var selected = all.WithAny<Velocity>().WithAny<Health>().WithNone<Disabled>();
            Assert.That(all.Count, Is.EqualTo(3));
            Assert.That(selected.Count, Is.EqualTo(2));
            selected.ForEach<Position, Velocity>((Entity entity, ref Position p, ref Velocity v) => p.X += v.X);
            Assert.That(world.Get<Position>(moving).X, Is.EqualTo(5));
            Assert.That(world.Get<Position>(idle).X, Is.EqualTo(10));
            world.Remove<Velocity>(moving);
            Assert.That(selected.Count, Is.EqualTo(1));
        }

        [Test]
        public void CachedQuery_SeesLaterComponentsAndNeverDuplicatesAnyMatches()
        {
            using var world = new World();
            var query = world.Query().WithAny<Position>().WithAny<Velocity>();
            Assert.That(query.Count, Is.Zero);
            var entity = world.CreateEntity();
            world.Set(entity, new Position());
            world.Set(entity, new Velocity());
            Assert.That(query.Count, Is.EqualTo(1));
            Assert.That(world.Query().Count, Is.EqualTo(1));
            world.DestroyEntity(entity);
            Assert.That(query.Count, Is.Zero);
        }

        [Test]
        public void Iteration_RejectsStructuralChangesButAllowsValueWritesAndNestedQueries()
        {
            using var world = new World();
            var entity = world.CreateEntity();
            world.Set(entity, new Health());
            var query = world.Query().WithAll<Health>();
            query.ForEach<Health>((Entity e, ref Health health) =>
            {
                Assert.Throws<InvalidOperationException>(() => world.CreateEntity());
                Assert.Throws<InvalidOperationException>(() => world.DestroyEntity(e));
                Assert.Throws<InvalidOperationException>(() => world.Remove<Health>(e));
                Assert.Throws<InvalidOperationException>(() => world.Set(e, new Position()));
                Assert.Throws<InvalidOperationException>(() => world.Dispose());
                world.Set(e, new Health { Value = 10 });
                Assert.That(query.Count, Is.EqualTo(1));
                health.Value++;
            });
            Assert.That(world.Get<Health>(entity).Value, Is.EqualTo(11));
        }

        [Test]
        public void CallbackException_ReleasesIterationGuard()
        {
            using var world = new World();
            world.CreateEntity();
            Assert.Throws<ApplicationException>(() => world.Query().ForEach(e => throw new ApplicationException()));
            Assert.DoesNotThrow(() => world.CreateEntity());
        }

        [Test]
        public void FourComponentQuery_UpdatesAllTypedReferences()
        {
            using var world = new World();
            var e = world.CreateEntity();
            world.Set(e, new Position()); world.Set(e, new Velocity());
            world.Set(e, new Health()); world.Set(e, new Disabled());
            world.Query().ForEach<Position, Velocity, Health, Disabled>(
                (Entity entity, ref Position p, ref Velocity v, ref Health h, ref Disabled d) =>
                { p.X = 1; v.X = 2; h.Value = 3; });
            Assert.That(world.Get<Position>(e).X, Is.EqualTo(1));
            Assert.That(world.Get<Velocity>(e).X, Is.EqualTo(2));
            Assert.That(world.Get<Health>(e).Value, Is.EqualTo(3));
        }
    }
}
