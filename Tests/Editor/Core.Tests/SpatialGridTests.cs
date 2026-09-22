//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using EjoyFramework.Core;
using EjoyFramework.Core.Spatial;

namespace EjoyFramework.Tests
{
    /// <summary>WS3-M1：SpatialGrid——与暴力法结果等价（随机集合）、跨 cell 移动、负坐标、矩形/最近邻/回调查询、零分配。</summary>
    public sealed class SpatialGridTests
    {
        private readonly List<int> m_Out = new List<int>();

        [Test]
        public void QueryRadius_MatchesBruteForce_OnRandomSet()
        {
            var rng = new Random(2026);
            var grid = new SpatialGrid(7.5f);
            var pos = new Dictionary<int, Pt>();
            for (int i = 0; i < 400; i++)
            {
                float x = (float)(rng.NextDouble() * 400 - 200), z = (float)(rng.NextDouble() * 400 - 200);
                grid.AddOrUpdate(i, x, z);
                pos[i] = new Pt(x, z);
            }

            for (int q = 0; q < 50; q++)
            {
                float qx = (float)(rng.NextDouble() * 400 - 200), qz = (float)(rng.NextDouble() * 400 - 200);
                float r = (float)(rng.NextDouble() * 60);
                grid.QueryRadius(qx, qz, r, m_Out);
                var expected = new List<int>();
                foreach (var kv in pos)
                {
                    float dx = kv.Value.X - qx, dz = kv.Value.Z - qz;
                    if (dx * dx + dz * dz <= r * r) expected.Add(kv.Key);
                }

                m_Out.Sort();
                expected.Sort();
                CollectionAssert.AreEqual(expected, m_Out, "query " + q);
            }
        }

        [Test]
        public void Move_AcrossCells_UpdatesBuckets()
        {
            var grid = new SpatialGrid(10f);
            grid.AddOrUpdate(1, 1f, 1f);
            grid.QueryCells(1f, 1f, 0, m_Out);
            CollectionAssert.AreEqual(new[] { 1 }, m_Out);

            grid.AddOrUpdate(1, 25f, 25f);
            grid.QueryCells(1f, 1f, 0, m_Out);
            Assert.AreEqual(0, m_Out.Count, "移出旧 cell 后旧 cell 不应再含该元素。");
            grid.QueryCells(25f, 25f, 0, m_Out);
            CollectionAssert.AreEqual(new[] { 1 }, m_Out);
            Assert.AreEqual(1, grid.OccupiedCellCount, "空 cell 应被回收。");
        }

        [Test]
        public void NegativeCoordinates_FloorSymmetric()
        {
            var grid = new SpatialGrid(10f);
            grid.AddOrUpdate(1, -0.1f, -0.1f);
            grid.AddOrUpdate(2, 0.1f, 0.1f);
            grid.QueryCells(-0.1f, -0.1f, 0, m_Out);
            CollectionAssert.AreEqual(new[] { 1 }, m_Out, "-0.1 应落在 cell -1，而不是与 0.1 同 cell。");
            int cx, cz;
            grid.GetCell(-0.1f, -0.1f, out cx, out cz);
            Assert.AreEqual(-1, cx);
        }

        [Test]
        public void ExactCellBoundary_MapsToFloorCell_NotHigherPrecisionArtifact()
        {
            // Mono 以更高精度求值 float 乘法：-10 * 0.1f 的 double 中间值 -1.0000000149 会把边界元素 floor 到 -2。
            var grid = new SpatialGrid(10f);
            grid.AddOrUpdate(1, -10f, 0f);        // floor(-10/10) = -1
            grid.AddOrUpdate(2, -10.0001f, 0f);   // -2
            grid.QueryCells(-10f, 0f, 0, m_Out);
            CollectionAssert.AreEqual(new[] { 1 }, m_Out);
            int cx, cz;
            grid.GetCell(-10f, 0f, out cx, out cz);
            Assert.AreEqual(-1, cx);
        }

        [Test]
        public void Remove_And_Contains()
        {
            var grid = new SpatialGrid(5f);
            grid.AddOrUpdate(7, 1f, 1f);
            Assert.IsTrue(grid.Contains(7));
            Assert.IsTrue(grid.Remove(7));
            Assert.IsFalse(grid.Remove(7));
            Assert.IsFalse(grid.Contains(7));
            Assert.AreEqual(0, grid.Count);
            Assert.AreEqual(0, grid.OccupiedCellCount);
        }

        [Test]
        public void QueryRect_IncludesBoundaries_ExcludesOutside()
        {
            var grid = new SpatialGrid(4f);
            grid.AddOrUpdate(1, 0f, 0f);
            grid.AddOrUpdate(2, 10f, 10f);
            grid.AddOrUpdate(3, 10.1f, 5f);
            grid.QueryRect(0f, 0f, 10f, 10f, m_Out);
            m_Out.Sort();
            CollectionAssert.AreEqual(new[] { 1, 2 }, m_Out);
        }

        [Test]
        public void QueryNearest_FindsTrueNearest_AcrossRings()
        {
            var grid = new SpatialGrid(10f);
            grid.AddOrUpdate(1, 9.9f, 0f);      // 同 cell 但较远
            grid.AddOrUpdate(2, -0.5f, 0f);     // 邻 cell 更近
            int id;
            float d;
            Assert.IsTrue(grid.QueryNearest(0.1f, 0f, 50f, out id, out d));
            Assert.AreEqual(2, id, "邻 cell 中更近的元素必须胜出。");
            Assert.AreEqual(0.6f, d, 1e-4f);

            Assert.IsTrue(grid.QueryNearest(0.1f, 0f, 50f, out id, out d, excludeId: 2));
            Assert.AreEqual(1, id);
            Assert.IsFalse(grid.QueryNearest(500f, 500f, 10f, out id, out d), "超出 maxRadius 应返回 false。");
        }

        [Test]
        public void QueryNearest_MatchesBruteForce_OnRandomSet()
        {
            var rng = new Random(9);
            var grid = new SpatialGrid(6f);
            var pos = new List<Pt>();
            for (int i = 0; i < 300; i++)
            {
                float x = (float)(rng.NextDouble() * 300 - 150), z = (float)(rng.NextDouble() * 300 - 150);
                grid.AddOrUpdate(i, x, z);
                pos.Add(new Pt(x, z));
            }

            for (int q = 0; q < 40; q++)
            {
                float qx = (float)(rng.NextDouble() * 300 - 150), qz = (float)(rng.NextDouble() * 300 - 150);
                float best = float.MaxValue;
                int bestId = -1;
                for (int i = 0; i < pos.Count; i++)
                {
                    float dx = pos[i].X - qx, dz = pos[i].Z - qz;
                    float dd = dx * dx + dz * dz;
                    if (dd < best) { best = dd; bestId = i; }
                }

                int id;
                float d;
                Assert.IsTrue(grid.QueryNearest(qx, qz, 1000f, out id, out d));
                Assert.AreEqual(bestId, id, "query " + q);
                Assert.AreEqual(Math.Sqrt(best), d, 1e-3);
            }
        }

        [Test]
        public void VisitorQuery_CanTerminateEarly()
        {
            var grid = new SpatialGrid(5f);
            for (int i = 0; i < 20; i++) grid.AddOrUpdate(i, i * 0.1f, 0f);
            var visitor = new CountingVisitor { StopAfter = 3 };
            grid.QueryRadius(0f, 0f, 100f, visitor);
            Assert.AreEqual(3, visitor.Visited);
        }

        [Test]
        public void Clear_ResetsAndReusable()
        {
            var grid = new SpatialGrid(5f);
            for (int i = 0; i < 100; i++) grid.AddOrUpdate(i, i, i);
            grid.Clear();
            Assert.AreEqual(0, grid.Count);
            grid.AddOrUpdate(1, 0f, 0f);
            grid.QueryRadius(0f, 0f, 1f, m_Out);
            CollectionAssert.AreEqual(new[] { 1 }, m_Out);
        }

        [Test]
        public void UpdateAndQuery_SteadyState_DoNotAllocate()
        {
            var grid = new SpatialGrid(8f);
            for (int i = 0; i < 64; i++) grid.AddOrUpdate(i, i % 8 * 3f, i / 8 * 3f);
            var results = new List<int>(64);
            var visitor = new CountingVisitor { StopAfter = int.MaxValue };
            float t = 0f;
            TestDelegate body = () =>
            {
                t += 0.37f;
                for (int i = 0; i < 64; i++) grid.AddOrUpdate(i, i % 8 * 3f + t, i / 8 * 3f - t);   // 跨 cell 移动
                grid.QueryRadius(10f, 10f, 12f, results);
                grid.QueryRect(0f, 0f, 20f, 20f, results);
                grid.QueryCells(10f, 10f, 1, results);
                grid.QueryRadius(10f, 10f, 12f, visitor);
                int id;
                float d;
                grid.QueryNearest(10f, 10f, 50f, out id, out d);
            };
            body();
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        private readonly struct Pt
        {
            public readonly float X;
            public readonly float Z;
            public Pt(float x, float z) { X = x; Z = z; }
        }

        private sealed class CountingVisitor : ISpatialVisitor
        {
            public int StopAfter;
            public int Visited;
            public bool Visit(int id, float distanceSquared) { Visited++; return Visited < StopAfter; }
        }
    }
}
