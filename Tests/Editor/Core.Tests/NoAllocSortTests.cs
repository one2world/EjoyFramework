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

namespace EjoyFramework.Tests
{
    /// <summary>NoAllocSort：与 List.Sort 结果等价（多组随机输入、含重复键）、区间排序、零分配。</summary>
    public sealed class NoAllocSortTests
    {
        private sealed class IntComparer : IComparer<int>
        {
            public static readonly IntComparer Instance = new IntComparer();
            public int Compare(int a, int b) { return a.CompareTo(b); }
        }

        [Test]
        public void Sort_MatchesReferenceSort_AcrossSizesAndDuplicates()
        {
            var rng = new Random(20260921);
            int[] sizes = { 0, 1, 2, 3, 15, 16, 17, 100, 1000 };
            for (int s = 0; s < sizes.Length; s++)
            {
                for (int round = 0; round < 5; round++)
                {
                    var expected = new List<int>(sizes[s]);
                    for (int i = 0; i < sizes[s]; i++) expected.Add(rng.Next(0, 50));   // 小值域制造大量重复
                    var actual = new List<int>(expected);

                    expected.Sort();
                    NoAllocSort.Sort(actual, IntComparer.Instance);

                    CollectionAssert.AreEqual(expected, actual, "size=" + sizes[s] + " round=" + round);
                }
            }
        }

        [Test]
        public void Sort_Range_OnlyTouchesRequestedSlice()
        {
            var list = new List<int> { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 };
            NoAllocSort.Sort(list, 2, 5, IntComparer.Instance);   // 排 [7,6,5,4,3] → [3,4,5,6,7]
            CollectionAssert.AreEqual(new[] { 9, 8, 3, 4, 5, 6, 7, 2, 1, 0 }, list);
        }

        [Test]
        public void Sort_InvalidArguments_Throw()
        {
            var list = new List<int> { 1 };
            Assert.Throws<FrameworkException>(() => NoAllocSort.Sort<int>(null, IntComparer.Instance));
            Assert.Throws<FrameworkException>(() => NoAllocSort.Sort(list, null));
            Assert.Throws<FrameworkException>(() => NoAllocSort.Sort(list, 0, 2, IntComparer.Instance));
        }

        [Test]
        public void Sort_DoesNotAllocate_ForBothInsertionAndHeapPaths()
        {
            var rng = new Random(7);
            int[] smallSeed = new int[12];
            int[] largeSeed = new int[1024];
            for (int i = 0; i < smallSeed.Length; i++) smallSeed[i] = rng.Next();
            for (int i = 0; i < largeSeed.Length; i++) largeSeed[i] = rng.Next();

            var small = new List<int>(smallSeed);
            var large = new List<int>(largeSeed);
            NoAllocSort.Sort(small, IntComparer.Instance);
            NoAllocSort.Sort(large, IntComparer.Instance);

            // 被测块只含"用索引器回填乱序 + 排序"：List.Reverse 等 BCL 调用在 Mono 下自身可能装箱，不能混进来。
            Assert.That(() =>
            {
                for (int i = 0; i < smallSeed.Length; i++) small[i] = smallSeed[i];
                for (int i = 0; i < largeSeed.Length; i++) large[i] = largeSeed[i];
                NoAllocSort.Sort(small, IntComparer.Instance);   // 插入排序路径（n <= 16）
                NoAllocSort.Sort(large, IntComparer.Instance);   // 堆排序路径（n > 16）
            }, Is.Not.AllocatingGCMemory());

            for (int i = 1; i < large.Count; i++) Assert.LessOrEqual(large[i - 1], large[i]);
        }
    }
}
