//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class GameLinkedListTests
    {
        [Test]
        public void AddLast_AppendsToEnd()
        {
            var list = new GameLinkedList<int>();
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list.First.Value);
            Assert.AreEqual(3, list.Last.Value);
        }

        [Test]
        public void Remove_NodeNotInList_ReturnsFalseInsteadOfThrowing()
        {
            var list1 = new GameLinkedList<int>();
            var list2 = new GameLinkedList<int>();
            var nodeInList1 = list1.AddLast(42);

            // 节点不属于 list2，应返回 false 而非抛异常
            Assert.IsFalse(list2.Remove(nodeInList1));
            Assert.AreEqual(1, list1.Count);
        }

        [Test]
        public void Remove_NullNode_ReturnsFalse()
        {
            var list = new GameLinkedList<int>();
            Assert.IsFalse(list.Remove((System.Collections.Generic.LinkedListNode<int>)null));
        }

        [Test]
        public void Clear_ReleasesNodesToCache()
        {
            var list = new GameLinkedList<int>();
            list.AddLast(1); list.AddLast(2); list.AddLast(3);
            int beforeCache = list.CachedNodeCount;
            list.Clear();
            Assert.AreEqual(0, list.Count);
            Assert.AreEqual(beforeCache + 3, list.CachedNodeCount);
        }

        [Test]
        public void MaxCachedNodeCount_LimitsCacheGrowth()
        {
            var list = new GameLinkedList<int> { MaxCachedNodeCount = 2 };
            for (int i = 0; i < 10; i++) list.AddLast(i);
            list.Clear();
            Assert.LessOrEqual(list.CachedNodeCount, 2);
        }

        [Test]
        public void Enumerator_AllowsRemovingCurrentNodeDuringIteration()
        {
            var list = new GameLinkedList<int>();
            list.AddLast(1); list.AddLast(2); list.AddLast(3);

            // 删除 current（值=2）应该不破坏迭代
            int sum = 0;
            foreach (var v in list)
            {
                sum += v;
                if (v == 2)
                {
                    list.Remove(v);
                }
            }
            Assert.AreEqual(6, sum);  // 1 + 2 + 3
            Assert.AreEqual(2, list.Count);
        }
    }
}
