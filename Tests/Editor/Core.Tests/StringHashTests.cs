//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine.TestTools;
using EjoyFramework.Core;
using EjoyFramework.Core.Blobs;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1: StringHash (FNV-1a-32/64 over UTF-8) and the collision registry.
    /// costarring/liquid, altarage/zinke, declinate/macallums, altarages/zinkes are known FNV-1a-32 collision pairs
    /// (cross-checked with an independent implementation).
    /// </summary>
    public class StringHashTests
    {
        private bool m_Enabled;
        private bool m_Throw;

        [SetUp]
        public void SetUp()
        {
            m_Enabled = StringHashRegistry.Enabled;
            m_Throw = StringHashRegistry.ThrowOnCollision;
            StringHashRegistry.Clear();
            StringHashRegistry.Enabled = true;
            StringHashRegistry.ThrowOnCollision = false;
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            StringHashRegistry.Enabled = m_Enabled;
            StringHashRegistry.ThrowOnCollision = m_Throw;
            StringHashRegistry.MaxEntries = StringHashRegistry.DefaultMaxEntries;
            StringHashRegistry.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Compute_MatchesStandardFnv1aVectors()
        {
            Assert.AreEqual(0x811C9DC5u, StringHash.Compute(""));
            Assert.AreEqual(0xE40C292Cu, StringHash.Compute("a"));
            Assert.AreEqual(0xBF9CF968u, StringHash.Compute("foobar"));
            Assert.AreEqual(0xCBF29CE484222325UL, StringHash.Compute64(""));
            Assert.AreEqual(0xAF63DC4C8601EC8CUL, StringHash.Compute64("a"));
            Assert.AreEqual(0x85944171F73967E8UL, StringHash.Compute64("foobar"));
            Assert.AreEqual(0xC488F045u, StringHash.Compute("中文"));
            Assert.AreEqual(0xB8F2D250u, StringHash.Compute("😀x"));
        }

        [Test]
        public void Compute_MatchesEncodingUtf8Bytes_IncludingLoneSurrogates()
        {
            var inputs = new List<string> { "\uD800", "a\uDC00b", "􏿿", "\uD83D", "x\uD83Dy", "é", "߿", "ࠀ", "￿" };
            var random = new Random(12345);
            for (int n = 0; n < 300; n++)
            {
                var sb = new StringBuilder();
                int length = random.Next(0, 12);
                for (int i = 0; i < length; i++)
                {
                    switch (random.Next(4))
                    {
                        case 0: sb.Append((char)random.Next(0x20, 0x7F)); break;
                        case 1: sb.Append((char)random.Next(0x80, 0x800)); break;
                        case 2: sb.Append((char)random.Next(0x800, 0x10000)); break;   // includes lone surrogates
                        default: sb.Append("😃"); break;
                    }
                }
                inputs.Add(sb.ToString());
            }

            foreach (string s in inputs)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                uint h32 = StringHash.OffsetBasis;
                ulong h64 = StringHash.OffsetBasis64;
                foreach (byte b in bytes)
                {
                    h32 = unchecked((h32 ^ b) * StringHash.Prime);
                    h64 = unchecked((h64 ^ b) * StringHash.Prime64);
                }

                Assert.AreEqual(h32, StringHash.Compute(s), "32-bit mismatch for " + Dump(s));
                Assert.AreEqual(h64, StringHash.Compute64(s), "64-bit mismatch for " + Dump(s));
                Assert.AreEqual(h64, ConfigBlobHash.Compute(s), "ConfigBlobHash must stay identical (blob compatibility)");
            }
        }

        [Test]
        public void Append_IsStreamingEquivalentToConcatenation()
        {
            uint streamed = StringHash.Append(StringHash.Append(StringHash.OffsetBasis, "user42".AsSpan()), ":exp-7".AsSpan());
            Assert.AreEqual(StringHash.Compute("user42:exp-7"), streamed);
            Assert.AreEqual(StringHash.Compute64("ab中"), StringHash.Append64(StringHash.Compute64("a"), "b中".AsSpan()));
        }

        [Test]
        public void Of_EqualityAndNone()
        {
            StringHash a = StringHash.Of("MaxHp");
            Assert.AreEqual(a, StringHash.Of("MaxHp"));
            Assert.AreNotEqual(a, StringHash.Of("MaxMp"));
            Assert.IsTrue(default(StringHash).IsNone);
            Assert.IsFalse(a.IsNone);
            Assert.Throws<FrameworkException>(() => StringHash.Of(null));
            string name;
            Assert.IsTrue(a.TryGetName(out name));
            Assert.AreEqual("MaxHp", name);
            StringAssert.StartsWith("MaxHp (0x", a.ToString());
        }

        [Test]
        public void Registry_DetectsCollisionOncePerPair()
        {
            Assert.AreEqual(StringHash.Of("costarring"), StringHash.Of("liquid"), "known FNV-1a-32 collision");
            StringHash.Of("liquid");
            StringHash.Of("costarring");
            Assert.AreEqual(1, StringHashRegistry.CollisionCount);

            var list = new List<StringHashCollision>();
            StringHashRegistry.GetCollisions(list);
            Assert.AreEqual("costarring", list[0].First);
            Assert.AreEqual("liquid", list[0].Second);
            Assert.AreEqual(0x5E4DAA9Du, list[0].Hash.Value);
        }

        [Test]
        public void Registry_ThrowOnCollision_Throws()
        {
            StringHashRegistry.ThrowOnCollision = true;
            StringHash.Of("altarage");
            var ex = Assert.Throws<FrameworkException>(() => StringHash.Of("zinke"));
            StringAssert.Contains("altarage", ex.Message);
            StringAssert.Contains("zinke", ex.Message);
        }

        [Test]
        public void Registry_Disabled_RecordsNothing()
        {
            StringHashRegistry.Enabled = false;
            StringHash.Of("costarring");
            StringHash.Of("liquid");
            Assert.AreEqual(0, StringHashRegistry.Count);
            Assert.AreEqual(0, StringHashRegistry.CollisionCount);
        }

        [Test]
        public void Registry_StopsRecordingAtMaxEntries()
        {
            StringHashRegistry.MaxEntries = 2;
            StringHash.Of("a");
            StringHash.Of("b");
            StringHash.Of("c");
            Assert.AreEqual(2, StringHashRegistry.Count);
        }

        [Test]
        public void FindCollisions_IsPureBatchCheck()
        {
            var results = new List<StringHashCollision>();
            int found = StringHashRegistry.FindCollisions(
                new[] { "declinate", "macallums", "macallums", "altarages", "zinkes", "unique", null, "unique" }, results);
            Assert.AreEqual(2, found);
            Assert.AreEqual(2, results.Count);
            Assert.AreEqual(0, StringHashRegistry.Count, "FindCollisions must not touch the global registry");
        }

        [Test]
        public void Compute_IsAllocationFree()
        {
            string text = "Player/Inventory/Slot中文";
            uint sink = 0;
            ZeroAlloc.Assert(() => { sink ^= StringHash.Compute(text.AsSpan()); sink ^= (uint)StringHash.Compute64(text.AsSpan()); });
            ZeroAlloc.Assert(() => { sink ^= StringHash.Of(text).Value; });   // re-registering a known name does not allocate
            Assert.AreNotEqual(1u, sink | 1u);
        }

        private static string Dump(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(((int)c).ToString("X4")).Append(' ');
            return sb.ToString();
        }
    }
}
