using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Ecs;

namespace EjoyFramework.Tests.Ecs
{
    public class SystemGroupTests
    {
        private sealed class CallbackSystem : ISystem
        {
            public Action<World, CommandBuffer, float> Callback;
            public void Update(World world, CommandBuffer commands, float deltaTime) => Callback(world, commands, deltaTime);
        }

        [Test]
        public void BorrowedCommandBuffer_CannotBeDisposedBySystemOrRetainedCaller()
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            CommandBuffer retained = null;
            group.Add(new CallbackSystem
            {
                Callback = (w, c, dt) =>
                {
                    retained = c;
                    Assert.Throws<InvalidOperationException>(() => c.Dispose());
                    c.Set(c.CreateEntity(), new Health());
                }
            });
            Assert.DoesNotThrow(() => group.Update(1));
            Assert.Throws<InvalidOperationException>(() => retained.Dispose());
            Assert.DoesNotThrow(() => group.Update(1));
            Assert.That(world.EntityCount, Is.EqualTo(2));
        }

        [Test]
        public void Systems_OrderIsStableAndChangesAreVisibleToNextSystem()
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            var order = new List<int>();
            var observe = new CallbackSystem
            {
                Callback = (w, c, dt) =>
                {
                    order.Add(2);
                    Assert.That(w.Query().WithAll<Health>().Count, Is.EqualTo(1));
                    Assert.That(dt, Is.EqualTo(0.25f));
                }
            };
            var spawn = new CallbackSystem
            {
                Callback = (w, c, dt) =>
                {
                    order.Add(1);
                    c.Set(c.CreateEntity(), new Health());
                }
            };
            var last = new CallbackSystem { Callback = (w, c, dt) => order.Add(3) };
            group.Add(observe, 10); group.Add(spawn, 0); group.Add(last, 10);
            group.Update(0.25f);
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Systems_EnableRemoveAndDuplicateRegistrationAreExplicit()
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            int calls = 0;
            var system = new CallbackSystem { Callback = (w, c, dt) => calls++ };
            group.Add(system);
            Assert.Throws<InvalidOperationException>(() => group.Add(system));
            group.SetEnabled(system, false);
            group.Update(1);
            Assert.That(calls, Is.Zero);
            group.SetEnabled(system, true);
            group.Update(1);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(group.Remove(system), Is.True);
            Assert.That(group.Remove(system), Is.False);
            Assert.Throws<InvalidOperationException>(() => group.SetEnabled(system, true));
        }

        [Test]
        public void SystemExecution_RejectsReentrancyAndDirectStructuralMutation()
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            CallbackSystem system = null;
            system = new CallbackSystem
            {
                Callback = (w, c, dt) =>
                {
                    Assert.Throws<InvalidOperationException>(() => group.Update(1));
                    Assert.Throws<InvalidOperationException>(() => group.Remove(system));
                    Assert.Throws<InvalidOperationException>(() => group.Dispose());
                    Assert.Throws<InvalidOperationException>(() => w.CreateEntity());
                    Assert.Throws<InvalidOperationException>(() => c.Playback());
                }
            };
            group.Add(system);
            Assert.DoesNotThrow(() => group.Update(1));
        }

        [Test]
        public void SystemException_DiscardsItsCommandsAndStopsLaterSystemsButGroupCanRecover()
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            int later = 0;
            var fail = new CallbackSystem
            {
                Callback = (w, c, dt) =>
                {
                    c.CreateEntity();
                    throw new ApplicationException();
                }
            };
            group.Add(fail);
            group.Add(new CallbackSystem { Callback = (w, c, dt) => later++ });
            Assert.Throws<ApplicationException>(() => group.Update(1));
            Assert.That(world.EntityCount, Is.Zero);
            Assert.That(later, Is.Zero);
            group.Remove(fail);
            group.Update(1);
            Assert.That(later, Is.EqualTo(1));
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidTime_IsRejectedBeforeSystemsExecute(float deltaTime)
        {
            using var world = new World();
            using var group = new SystemGroup(world);
            Assert.Throws<ArgumentOutOfRangeException>(() => group.Update(deltaTime));
        }

        [Test]
        public void DisposingGroup_DoesNotDisposeWorldOrCallerOwnedSystems()
        {
            using var world = new World();
            var group = new SystemGroup(world);
            group.Dispose(); group.Dispose();
            Assert.DoesNotThrow(() => world.CreateEntity());
            Assert.Throws<ObjectDisposedException>(() => group.Update(1));
        }
    }
}
