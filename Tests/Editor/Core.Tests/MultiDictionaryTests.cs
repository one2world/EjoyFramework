//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class MultiDictionaryTests
    {
        [Test]
        public void Add_MultipleValuesForSameKey_AllRetained()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1);
            md.Add("k", 2);
            md.Add("k", 3);

            var range = md["k"];
            Assert.AreEqual(3, range.Count);
            Assert.IsTrue(range.Contains(2));
        }

        [Test]
        public void Contains_NullValueDoesNotThrow_AndUsesDefaultEqualityComparer()
        {
            var md = new MultiDictionary<string, string>();
            md.Add("k", null);
            md.Add("k", "v");

            // 这里以前会 NRE（current.Value.Equals(value)），现在用 EqualityComparer.Default
            Assert.IsTrue(md.Contains("k", null));
            Assert.IsTrue(md.Contains("k", "v"));
            Assert.IsFalse(md.Contains("k", "missing"));
        }

        [Test]
        public void Remove_ByValue_RemovesFirstMatch()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1); md.Add("k", 2); md.Add("k", 1);

            Assert.IsTrue(md.Remove("k", 1));
            Assert.AreEqual(2, md["k"].Count);
        }

        [Test]
        public void Remove_LastValueForKey_ClearsKeyEntry()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1);
            Assert.AreEqual(1, md.Count);

            // 移除该 key 仅有的值后，key 条目本身应被清理（哨兵节点回收）。
            Assert.IsTrue(md.Remove("k", 1));
            Assert.IsFalse(md.Contains("k"));
            Assert.AreEqual(0, md.Count);
            Assert.IsFalse(md["k"].IsValid);
        }

        [Test]
        public void Remove_OneOfMany_KeepsKeyAndRemainingValues()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1); md.Add("k", 2); md.Add("k", 3);

            // 移除中间值：key 条目保留，其余值完好。
            Assert.IsTrue(md.Remove("k", 2));
            Assert.IsTrue(md.Contains("k"));
            Assert.AreEqual(1, md.Count);
            Assert.AreEqual(2, md["k"].Count);
            Assert.IsTrue(md.Contains("k", 1));
            Assert.IsTrue(md.Contains("k", 3));
            Assert.IsFalse(md.Contains("k", 2));
        }

        [Test]
        public void Remove_AllValuesOneByOne_ClearsKeyEntry()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1); md.Add("k", 2);

            // 逐个移除（先 First，再剩下的唯一值）后 key 应消失。
            Assert.IsTrue(md.Remove("k", 1));
            Assert.IsTrue(md.Contains("k"));
            Assert.IsTrue(md.Remove("k", 2));
            Assert.IsFalse(md.Contains("k"));
            Assert.AreEqual(0, md.Count);
        }

        [Test]
        public void RemoveAll_RemovesAllValuesForKey()
        {
            var md = new MultiDictionary<string, int>();
            md.Add("k", 1); md.Add("k", 2); md.Add("k", 3);
            md.Add("other", 9);

            Assert.IsTrue(md.RemoveAll("k"));
            Assert.IsFalse(md.Contains("k"));
            Assert.IsTrue(md.Contains("other"));
        }

        [Test]
        public void Range_Contains_NullValuesSafely()
        {
            var md = new MultiDictionary<int, object>();
            md.Add(1, null);
            md.Add(1, new object());

            var range = md[1];
            Assert.IsTrue(range.Contains(null));
            Assert.AreEqual(2, range.Count);
        }
    }
}
