//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Worldmap;

namespace EjoyFramework.GamePlay.Tests.Worldmap
{
    public class MapMathTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Distance_ReturnsEuclideanLength()
        {
            var a = new MapCoord(0f, 0f);
            var b = new MapCoord(3f, 4f);
            Assert.AreEqual(5f, MapMath.Distance(a, b), Delta);
        }

        [Test]
        public void SqrDistance_ReturnsSquaredLength()
        {
            var a = new MapCoord(1f, 2f);
            var b = new MapCoord(4f, 6f);
            // dx=3, dy=4 -> 9+16 = 25
            Assert.AreEqual(25f, MapMath.SqrDistance(a, b), Delta);
        }

        [Test]
        public void Distance_SamePoint_IsZero()
        {
            var a = new MapCoord(7f, -3f);
            Assert.AreEqual(0f, MapMath.Distance(a, a), Delta);
            Assert.AreEqual(0f, MapMath.SqrDistance(a, a), Delta);
        }

        [Test]
        public void WorldToNormalized_MapsCornersAndCenter()
        {
            var min = new MapCoord(-100f, -50f);
            var max = new MapCoord(100f, 50f);

            MapCoord lo = MapMath.WorldToNormalized(min, min, max);
            Assert.AreEqual(0f, lo.X, Delta);
            Assert.AreEqual(0f, lo.Y, Delta);

            MapCoord hi = MapMath.WorldToNormalized(max, min, max);
            Assert.AreEqual(1f, hi.X, Delta);
            Assert.AreEqual(1f, hi.Y, Delta);

            MapCoord mid = MapMath.WorldToNormalized(new MapCoord(0f, 0f), min, max);
            Assert.AreEqual(0.5f, mid.X, Delta);
            Assert.AreEqual(0.5f, mid.Y, Delta);
        }

        [Test]
        public void WorldToNormalized_DoesNotClamp_OutOfRange()
        {
            var min = new MapCoord(0f, 0f);
            var max = new MapCoord(10f, 10f);

            MapCoord n = MapMath.WorldToNormalized(new MapCoord(20f, -5f), min, max);
            Assert.AreEqual(2f, n.X, Delta, "超出范围不应被钳制");
            Assert.AreEqual(-0.5f, n.Y, Delta, "低于范围不应被钳制");
        }

        [Test]
        public void WorldToNormalized_MinEqualsMax_ReturnsZero_NoDivideByZero()
        {
            var min = new MapCoord(5f, 5f);
            var max = new MapCoord(5f, 8f); // X 轴退化，Y 轴正常

            MapCoord n = MapMath.WorldToNormalized(new MapCoord(5f, 8f), min, max);
            Assert.AreEqual(0f, n.X, Delta, "退化轴返回 0");
            Assert.AreEqual(1f, n.Y, Delta, "正常轴照常归一化");
        }

        [Test]
        public void NormalizedToWorld_RoundTrips_WithWorldToNormalized()
        {
            var min = new MapCoord(-20f, 30f);
            var max = new MapCoord(80f, 130f);
            var world = new MapCoord(12.5f, 77.25f);

            MapCoord n = MapMath.WorldToNormalized(world, min, max);
            MapCoord back = MapMath.NormalizedToWorld(n, min, max);

            Assert.AreEqual(world.X, back.X, Delta);
            Assert.AreEqual(world.Y, back.Y, Delta);
        }

        [Test]
        public void NormalizedToWorld_MapsCenter()
        {
            var min = new MapCoord(0f, 0f);
            var max = new MapCoord(100f, 200f);

            MapCoord w = MapMath.NormalizedToWorld(new MapCoord(0.5f, 0.5f), min, max);
            Assert.AreEqual(50f, w.X, Delta);
            Assert.AreEqual(100f, w.Y, Delta);
        }
    }
}
