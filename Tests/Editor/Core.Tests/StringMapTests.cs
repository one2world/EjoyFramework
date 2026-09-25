//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：StringMap —— string / span / StringHash 三种查找、两种碰撞模式、删除与空闲槽复用、枚举、零分配。
    /// </summary>
    public class StringMapTests
    {
        [Test]
        public void AddAndLookup_ByStringSpanAndHash()
        {
            var map = new StringMap<int>(0, true);
            map.Add("MaxHp", 100);
            map.Add("MaxMp", 50);
            int v;
            Assert.IsTrue(map.TryGetValue("MaxHp", out v)); Assert.AreEqual(100, v);
            Assert.IsTrue(map.TryGetValue("xMaxMpx".AsSpan(1, 5), out v)); Assert.AreEqual(50, v);
            Assert.IsTrue(map.TryGetValue(StringHash.Of("MaxHp"), out v)); Assert.AreEqual(100, v);
            Assert.IsFalse(map.TryGetValue("maxhp", out v), "ordinal comparison");
            Assert.IsFalse(map.TryGetValue((string)null, out v));
            Assert.AreEqual(2, map.Count);
            Assert.Throws<FrameworkException>(() => map.Add("MaxHp", 1));
            Assert.IsFalse(map.TryAdd("MaxHp", 1));
            map["MaxHp"] = 120;
            Assert.AreEqual(120, map["MaxHp"]);
            Assert.Throws<FrameworkException>(() => { int unused = map["Missing"]; });
        }

        [Test]
        public void UniqueHashes_RejectsCollidingKey()
        {
            var map = new StringMap<int>(0, true);
            map.Add("costarring", 1);
            Assert.IsFalse(map.TryAdd("liquid", 2));
            var ex = Assert.Throws<FrameworkException>(() => map.Add("liquid", 2));
            StringAssert.Contains("costarring", ex.Message);
            Assert.Throws<FrameworkException>(() => map["liquid"] = 2);
            string existing;
            Assert.IsTrue(map.TryGetKey(StringHash.Of("liquid"), out existing));
            Assert.AreEqual("costarring", existing);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void DefaultMode_AllowsCollisions_ButForbidsHashLookup()
        {
            var map = new StringMap<int>();
            map.Add("costarring", 1);
            map.Add("liquid", 2);
            int v;
            Assert.IsTrue(map.TryGetValue("costarring", out v)); Assert.AreEqual(1, v);
            Assert.IsTrue(map.TryGetValue("liquid".AsSpan(), out v)); Assert.AreEqual(2, v);
            Assert.Throws<FrameworkException>(() => map.TryGetValue(StringHash.Of("liquid"), out v));
            Assert.IsTrue(map.Remove("costarring"));
            Assert.IsTrue(map.TryGetValue("liquid", out v)); Assert.AreEqual(2, v);
            Assert.IsFalse(map.ContainsKey("costarring"));
        }

        [Test]
        public void RemoveGrowAndEnumerate_KeepDictionaryOrdering()
        {
            var map = new StringMap<int>();
            var reference = new Dictionary<string, int>();
            for (int i = 0; i < 200; i++)
            {
                map.Add("k" + i, i);
                reference.Add("k" + i, i);
            }
            for (int i = 0; i < 200; i += 3)
            {
                Assert.IsTrue(map.Remove(("k" + i).AsSpan()));
                reference.Remove("k" + i);
            }
            for (int i = 0; i < 20; i++)
            {
                map.Add("n" + i, -i);
                reference.Add("n" + i, -i);
            }

            Assert.AreEqual(reference.Count, map.Count);
            var fromMap = new List<string>();
            foreach (var kv in map)
            {
                Assert.AreEqual(reference[kv.Key], kv.Value);
                fromMap.Add(kv.Key);
            }
            CollectionAssert.AreEqual(new List<string>(reference.Keys), fromMap, "enumeration order matches Dictionary (free slots reused LIFO)");
        }

        [Test]
        public void ModificationDuringEnumeration_Throws()
        {
            var map = new StringMap<int>();
            map.Add("a", 1);
            map.Add("b", 2);
            Assert.Throws<FrameworkException>(() =>
            {
                foreach (var kv in map) map.Add("c" + kv.Key, 0);
            });
        }

        [Test]
        public void Clear_ResetsAndReuses()
        {
            var map = new StringMap<string>(8);
            map.Add("a", "1");
            map.Clear();
            Assert.AreEqual(0, map.Count);
            Assert.IsFalse(map.ContainsKey("a"));
            map.Add("a", "2");
            Assert.AreEqual("2", map["a"]);
        }

        [Test]
        public void Lookups_AreAllocationFree()
        {
            var map = new StringMap<int>(0, true);
            for (int i = 0; i < 64; i++) map.Add("Key" + i, i);
            string key = "Key42";
            char[] chars = "xxKey17".ToCharArray();
            StringHash hash = StringHash.Of("Key63");
            int sink = 0;
            ZeroAlloc.Assert(() =>
            {
                int v;
                if (map.TryGetValue(key, out v)) sink += v;
                if (map.TryGetValue(new System.ReadOnlySpan<char>(chars, 2, 5), out v)) sink += v;
                if (map.TryGetValue(hash, out v)) sink += v;
                foreach (var kv in map) sink += kv.Value;
            });
            Assert.Greater(sink, 0);
        }
    }
}
