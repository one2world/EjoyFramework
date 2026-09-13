using System;
using NUnit.Framework;
using EjoyFramework.Core.Ecs;
using EjoyGame.Samples.EcsDemo;

namespace EjoyFramework.EcsSampleTests
{
    public class ScenarioTests
    {
        [TestCase(0f, 5f, 2f)]
        [TestCase(5f, 0f, -2f)]
        public void Projectile_MovesHitsDamagesAndDestroysBothEntities(float start, float targetX, float speed)
        {
            using var world = new World();
            using var systems = new SystemGroup(world);
            var scenario = new ProjectileScenario(world, systems);
            var target = scenario.SpawnTarget(targetX, 10);
            var shot = scenario.Fire(start, target, speed, 10);
            systems.Update(1);
            Assert.That(world.Get<Position>(shot).X, Is.EqualTo(start + speed));
            Assert.That(world.Get<Health>(target).Value, Is.EqualTo(10));
            systems.Update(2); // Overshoots target; hit still occurs.
            Assert.That(world.IsAlive(shot), Is.False);
            Assert.That(world.IsAlive(target), Is.False);
            Assert.That(world.EntityCount, Is.Zero);
        }

        [Test]
        public void TargetDestroyedBeforeImpact_ProjectileIsCleanedUp()
        {
            using var world = new World();
            using var systems = new SystemGroup(world);
            var scenario = new ProjectileScenario(world, systems);
            var target = scenario.SpawnTarget(5, 10);
            var shot = scenario.Fire(0, target, 1, 10);
            world.DestroyEntity(target);
            var replacement = scenario.SpawnTarget(5, 20);
            systems.Update(1);
            Assert.That(world.IsAlive(shot), Is.False);
            Assert.That(world.Get<Health>(replacement).Value, Is.EqualTo(20));
        }

        [Test]
        public void WarmMovement_HasNoPerTickManagedAllocations()
        {
            using var world = new World();
            using var systems = new SystemGroup(world);
            for (int i = 0; i < 10000; i++)
            {
                var entity = world.CreateEntity();
                world.Set(entity, new Position());
                world.Set(entity, new Velocity { X = 1 });
            }
            systems.Add(new MovementSystem(world));
            for (int i = 0; i < 30; i++) systems.Update(0.5f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) systems.Update(0.5f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            world.Query().ForEach<Position>((Entity e, ref Position p) => Assert.That(p.X, Is.EqualTo(65f)));
        }
    }
}
