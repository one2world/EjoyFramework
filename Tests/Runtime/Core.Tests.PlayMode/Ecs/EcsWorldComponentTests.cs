using System.Collections;
using EjoyFramework.Core.Ecs;
using EjoyFramework.Core.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EjoyFramework.Tests.PlayMode.Ecs
{
    public class EcsWorldComponentTests
    {
        [Test]
        public void RejectedShutdown_DoesNotPartiallyDisposeDriver()
        {
            var go = new GameObject("EcsShutdownTest");
            try
            {
                var driver = go.AddComponent<EcsWorldComponent>();
                driver.UpdateMode = EcsUpdateMode.Manual;
                driver.World.CreateEntity();
                driver.World.Query().ForEach(e =>
                    Assert.Throws<System.InvalidOperationException>(() => driver.Shutdown()));
                Assert.That(driver.IsInitialized, Is.True);
                Assert.DoesNotThrow(() => driver.Step(0.1f));
                Assert.DoesNotThrow(() => driver.Shutdown());
                Assert.That(driver.IsInitialized, Is.False);
            }
            finally { Object.DestroyImmediate(go); }
        }

        private sealed class CountSystem : ISystem
        {
            public int Count;
            public float Elapsed;
            public void Update(World world, CommandBuffer commands, float deltaTime) { Count++; Elapsed += deltaTime; }
        }

        [UnityTest]
        public IEnumerator Driver_ManualAutomaticPauseAndDestructionRespectOwnership()
        {
            var go = new GameObject("EcsWorldTest");
            try
            {
                var driver = go.AddComponent<EcsWorldComponent>();
                driver.UpdateMode = EcsUpdateMode.Manual;
                var world = driver.World;
                var system = new CountSystem();
                driver.Systems.Add(system);
                yield return null;
                Assert.That(system.Count, Is.Zero);
                driver.Step(0.25f);
                Assert.That(system.Count, Is.EqualTo(1));
                Assert.That(system.Elapsed, Is.EqualTo(0.25f));
                driver.UpdateMode = EcsUpdateMode.Update;
                yield return null;
                yield return null;
                Assert.That(system.Count, Is.GreaterThan(1));
                driver.enabled = false;
                int paused = system.Count;
                yield return null;
                Assert.That(system.Count, Is.EqualTo(paused));
                Assert.That(world.IsDisposed, Is.False);
                Object.Destroy(go);
                yield return null;
                Assert.That(world.IsDisposed, Is.True);
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }

        [Test]
        public void View_InvalidatesOnEntityReuseAndUnbindDoesNotDestroySimulation()
        {
            using var world = new World();
            var go = new GameObject("EcsViewTest");
            try
            {
                var view = go.AddComponent<EcsEntityView>();
                var original = world.CreateEntity();
                view.Bind(world, original);
                Assert.That(view.IsBound, Is.True);
                view.Unbind();
                Assert.That(world.IsAlive(original), Is.True);
                view.Bind(world, original);
                world.DestroyEntity(original);
                var recycled = world.CreateEntity();
                Assert.That(recycled.Index, Is.EqualTo(original.Index));
                Assert.That(view.IsBound, Is.False);
                view.Bind(world, recycled);
                Object.DestroyImmediate(go);
                Assert.That(world.IsAlive(recycled), Is.True);
            }
            finally { if (go != null) Object.DestroyImmediate(go); }
        }
    }
}
