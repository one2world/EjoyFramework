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
    /// <summary>BinaryHeap：与参考排序等价、键调整、任意位置移除、零分配。</summary>
    public sealed class BinaryHeapTests
    {
        private sealed class IntComparer : IComparer<int>
        {
            public static readonly IntComparer Instance = new IntComparer();
            public int Compare(int a, int b) { return a.CompareTo(b); }
        }

        [Test]
        public void PushPop_YieldsAscendingOrder_WithDuplicates()
        {
            var rng = new Random(3);
            var heap = new BinaryHeap<int>(IntComparer.Instance);
            var expected = new List<int>();
            for (int i = 0; i < 500; i++) { int v = rng.Next(0, 40); heap.Push(v); expected.Add(v); }
            expected.Sort();

            var actual = new List<int>();
            while (heap.Count > 0) actual.Add(heap.Pop());
            CollectionAssert.AreEqual(expected, actual);
        }

        [Test]
        public void Peek_And_EmptyBehaviour()
        {
            var heap = new BinaryHeap<int>(IntComparer.Instance);
            Assert.Throws<FrameworkException>(() => heap.Peek());
            Assert.Throws<FrameworkException>(() => heap.Pop());
            int v;
            Assert.IsFalse(heap.TryPop(out v));
            heap.Push(5); heap.Push(1);
            Assert.AreEqual(1, heap.Peek());
            Assert.IsTrue(heap.TryPop(out v));
            Assert.AreEqual(1, v);
        }

        [Test]
        public void DecreaseIncreaseKey_ReorderCorrectly()
        {
            var heap = new BinaryHeap<Box>(BoxComparer.Instance);
            var boxes = new Box[6];
            for (int i = 0; i < boxes.Length; i++) { boxes[i] = new Box { Key = 10 + i }; heap.Push(boxes[i]); }

            boxes[5].Key = 0;                       // 最大变最小
            heap.DecreaseKeyAt(heap.IndexOf(boxes[5]));
            Assert.AreSame(boxes[5], heap.Peek());

            boxes[5].Key = 100;                     // 再变最大
            heap.IncreaseKeyAt(heap.IndexOf(boxes[5]));
            Assert.AreSame(boxes[0], heap.Peek());

            Box last = null;
            while (heap.Count > 0) last = heap.Pop();
            Assert.AreSame(boxes[5], last);
        }

        [Test]
        public void RemoveAt_KeepsHeapValid()
        {
            var rng = new Random(11);
            var heap = new BinaryHeap<int>(IntComparer.Instance);
            var mirror = new List<int>();
            for (int i = 0; i < 200; i++) { int v = rng.Next(1000); heap.Push(v); mirror.Add(v); }
            for (int i = 0; i < 50; i++)
            {
                int index = rng.Next(heap.Count);
                int removed = heap[index];
                heap.RemoveAt(index);
                mirror.Remove(removed);
            }

            mirror.Sort();
            var drained = new List<int>();
            while (heap.Count > 0) drained.Add(heap.Pop());
            CollectionAssert.AreEqual(mirror, drained);
        }

        [Test]
        public void PushPop_SteadyState_DoesNotAllocate()
        {
            var heap = new BinaryHeap<int>(IntComparer.Instance, 64);
            TestDelegate body = () =>
            {
                for (int i = 0; i < 48; i++) heap.Push(48 - i);
                while (heap.Count > 0) heap.Pop();
            };
            body();
            Assert.That(body, Is.Not.AllocatingGCMemory());
        }

        private sealed class Box { public int Key; }

        private sealed class BoxComparer : IComparer<Box>
        {
            public static readonly BoxComparer Instance = new BoxComparer();
            public int Compare(Box a, Box b) { return a.Key.CompareTo(b.Key); }
        }
    }
}
