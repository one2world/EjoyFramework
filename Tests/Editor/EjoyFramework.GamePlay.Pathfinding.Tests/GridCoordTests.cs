//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.GamePlay.Pathfinding;

namespace EjoyFramework.GamePlay.Tests.Pathfinding
{
    public class GridCoordTests
    {
        [Test]
        public void Constructor_StoresXAndY()
        {
            var c = new GridCoord(3, -7);
            Assert.AreEqual(3, c.X);
            Assert.AreEqual(-7, c.Y);
        }

        [Test]
        public void Equals_IsValueSemantics()
        {
            var a = new GridCoord(2, 5);
            var b = new GridCoord(2, 5);
            var c = new GridCoord(2, 6);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a.Equals((object)b));
            Assert.IsFalse(a.Equals(c));
            Assert.IsFalse(a.Equals((object)"not a coord"));
            Assert.IsFalse(a.Equals(null));
        }

        [Test]
        public void Operators_MatchEquals()
        {
            var a = new GridCoord(1, 1);
            var b = new GridCoord(1, 1);
            var c = new GridCoord(0, 1);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a == c);
            Assert.IsTrue(a != c);
            Assert.IsFalse(a != b);
        }

        [Test]
        public void GetHashCode_EqualCoordsShareHash()
        {
            var a = new GridCoord(9, -4);
            var b = new GridCoord(9, -4);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void UsableAsDictionaryKey()
        {
            var map = new Dictionary<GridCoord, string>();
            map[new GridCoord(1, 2)] = "a";
            map[new GridCoord(3, 4)] = "b";

            Assert.AreEqual("a", map[new GridCoord(1, 2)]);
            Assert.AreEqual("b", map[new GridCoord(3, 4)]);
            Assert.IsFalse(map.ContainsKey(new GridCoord(5, 6)));
        }

        [Test]
        public void ToString_IsReadable()
        {
            Assert.AreEqual("(4, 8)", new GridCoord(4, 8).ToString());
        }
    }
}
