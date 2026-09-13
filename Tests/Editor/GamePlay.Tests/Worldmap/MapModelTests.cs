//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using EjoyFramework.GamePlay.Worldmap;

namespace EjoyFramework.GamePlay.Tests.Worldmap
{
    public class MapModelTests
    {
        private static MapPoint Town(string id, float x, float y, bool fastTravel = true)
        {
            return new MapPoint(id, new MapCoord(x, y), "town", fastTravel);
        }

        // ---------- AddPoint / GetPoint ----------

        [Test]
        public void AddPoint_ThenGetPoint_ReturnsSameInstance()
        {
            var model = new MapModel();
            var p = Town("a", 0f, 0f);
            model.AddPoint(p);

            Assert.AreSame(p, model.GetPoint("a"));
        }

        [Test]
        public void GetPoint_Missing_ReturnsNull()
        {
            var model = new MapModel();
            Assert.IsNull(model.GetPoint("nope"));
        }

        [Test]
        public void AddPoint_DuplicateId_Throws()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            Assert.Throws<System.ArgumentException>(() => model.AddPoint(Town("a", 1f, 1f)));
        }

        [Test]
        public void Points_EnumeratesAllAdded()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.AddPoint(Town("b", 1f, 1f));

            var ids = model.Points.Select(pt => pt.Id).OrderBy(s => s).ToArray();
            CollectionAssert.AreEqual(new[] { "a", "b" }, ids);
        }

        // ---------- Discover / IsDiscovered / DiscoveredPoints ----------

        [Test]
        public void Discover_MarksAsDiscovered()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));

            Assert.IsFalse(model.IsDiscovered("a"));
            model.Discover("a");
            Assert.IsTrue(model.IsDiscovered("a"));
        }

        [Test]
        public void Discover_FiresOnDiscovered_OnlyOnFirstTime()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));

            int count = 0;
            string lastId = null;
            model.OnDiscovered += (m, id) => { count++; lastId = id; Assert.AreSame(model, m); };

            model.Discover("a");
            model.Discover("a"); // 幂等：第二次不应再触发

            Assert.AreEqual(1, count);
            Assert.AreEqual("a", lastId);
        }

        [Test]
        public void Discover_UnknownId_DoesNothing()
        {
            var model = new MapModel();
            int count = 0;
            model.OnDiscovered += (m, id) => count++;

            model.Discover("ghost");

            Assert.IsFalse(model.IsDiscovered("ghost"));
            Assert.AreEqual(0, count, "发现一个未注册的点不应触发事件");
        }

        [Test]
        public void DiscoveredPoints_ContainsOnlyDiscovered()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.AddPoint(Town("b", 1f, 1f));
            model.AddPoint(Town("c", 2f, 2f));

            model.Discover("a");
            model.Discover("c");

            var ids = model.DiscoveredPoints.Select(pt => pt.Id).OrderBy(s => s).ToArray();
            CollectionAssert.AreEqual(new[] { "a", "c" }, ids);
        }

        // ---------- CanFastTravel ----------

        [Test]
        public void CanFastTravel_ValidPair_ReturnsTrue()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.AddPoint(Town("b", 10f, 0f));
            model.Discover("a");
            model.Discover("b");

            Assert.IsTrue(model.CanFastTravel("a", "b"));
        }

        [Test]
        public void CanFastTravel_SamePoint_ReturnsFalse()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.Discover("a");

            Assert.IsFalse(model.CanFastTravel("a", "a"));
        }

        [Test]
        public void CanFastTravel_UndiscoveredDestination_ReturnsFalse()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.AddPoint(Town("b", 10f, 0f));
            model.Discover("a"); // b 未发现

            Assert.IsFalse(model.CanFastTravel("a", "b"));
        }

        [Test]
        public void CanFastTravel_UndiscoveredSource_ReturnsFalse()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.AddPoint(Town("b", 10f, 0f));
            model.Discover("b"); // a 未发现

            Assert.IsFalse(model.CanFastTravel("a", "b"));
        }

        [Test]
        public void CanFastTravel_NonFastTravelPoint_ReturnsFalse()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f, fastTravel: true));
            model.AddPoint(Town("b", 10f, 0f, fastTravel: false)); // 非快速旅行
            model.Discover("a");
            model.Discover("b");

            Assert.IsFalse(model.CanFastTravel("a", "b"));
        }

        [Test]
        public void CanFastTravel_MissingPoint_ReturnsFalse()
        {
            var model = new MapModel();
            model.AddPoint(Town("a", 0f, 0f));
            model.Discover("a");

            Assert.IsFalse(model.CanFastTravel("a", "missing"));
            Assert.IsFalse(model.CanFastTravel("missing", "a"));
        }

        // ---------- GetFastTravelDestinations ----------

        [Test]
        public void GetFastTravelDestinations_ExcludesSelf_Undiscovered_NonFastTravel()
        {
            var model = new MapModel();
            model.AddPoint(Town("home", 0f, 0f, fastTravel: true));
            model.AddPoint(Town("ft_discovered", 10f, 0f, fastTravel: true));
            model.AddPoint(Town("ft_undiscovered", 20f, 0f, fastTravel: true));
            model.AddPoint(Town("nonft", 30f, 0f, fastTravel: false));

            model.Discover("home");
            model.Discover("ft_discovered");
            model.Discover("nonft"); // 已发现但非快速旅行
            // ft_undiscovered 未发现

            var dest = model.GetFastTravelDestinations("home");
            var ids = dest.Select(p => p.Id).OrderBy(s => s).ToArray();

            CollectionAssert.AreEqual(new[] { "ft_discovered" }, ids);
        }

        [Test]
        public void GetFastTravelDestinations_FromUndiscoveredSource_ReturnsEmpty()
        {
            var model = new MapModel();
            model.AddPoint(Town("home", 0f, 0f));
            model.AddPoint(Town("b", 10f, 0f));
            model.Discover("b"); // home 未发现

            IReadOnlyList<MapPoint> dest = model.GetFastTravelDestinations("home");
            Assert.IsNotNull(dest);
            Assert.AreEqual(0, dest.Count);
        }

        [Test]
        public void GetFastTravelDestinations_FromNonFastTravelSource_ReturnsEmpty()
        {
            var model = new MapModel();
            model.AddPoint(Town("home", 0f, 0f, fastTravel: false));
            model.AddPoint(Town("b", 10f, 0f, fastTravel: true));
            model.Discover("home");
            model.Discover("b");

            Assert.AreEqual(0, model.GetFastTravelDestinations("home").Count);
        }

        [Test]
        public void GetFastTravelDestinations_MissingSource_ReturnsEmptyNotNull()
        {
            var model = new MapModel();
            IReadOnlyList<MapPoint> dest = model.GetFastTravelDestinations("nope");
            Assert.IsNotNull(dest);
            Assert.AreEqual(0, dest.Count);
        }

        // ---------- FindNearest ----------

        [Test]
        public void FindNearest_NoFilter_ReturnsClosest()
        {
            var model = new MapModel();
            model.AddPoint(Town("far", 100f, 100f));
            model.AddPoint(Town("near", 1f, 1f));
            model.AddPoint(Town("mid", 10f, 10f));

            MapPoint result = model.FindNearest(new MapCoord(0f, 0f));
            Assert.IsNotNull(result);
            Assert.AreEqual("near", result.Id);
        }

        [Test]
        public void FindNearest_WithFilter_RespectsPredicate()
        {
            var model = new MapModel();
            model.AddPoint(new MapPoint("dungeon", new MapCoord(1f, 1f), "dungeon"));
            model.AddPoint(new MapPoint("town", new MapCoord(5f, 5f), "town"));

            // 最近的是 dungeon，但只接受 town。
            MapPoint result = model.FindNearest(new MapCoord(0f, 0f), p => p.Type == "town");
            Assert.IsNotNull(result);
            Assert.AreEqual("town", result.Id);
        }

        [Test]
        public void FindNearest_Empty_ReturnsNull()
        {
            var model = new MapModel();
            Assert.IsNull(model.FindNearest(new MapCoord(0f, 0f)));
        }

        [Test]
        public void FindNearest_FilterExcludesAll_ReturnsNull()
        {
            var model = new MapModel();
            model.AddPoint(new MapPoint("a", new MapCoord(1f, 1f), "town"));
            Assert.IsNull(model.FindNearest(new MapCoord(0f, 0f), p => p.Type == "dungeon"));
        }
    }
}
