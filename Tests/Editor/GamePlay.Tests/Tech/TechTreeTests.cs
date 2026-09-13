//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Tech;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Tech
{
    /// <summary>
    /// 针对 <see cref="TechTree"/>、<see cref="TechNode"/> 与 <see cref="TechNodeStatus"/> 的单元测试。
    /// </summary>
    [TestFixture]
    public class TechTreeTests
    {
        private static TechNode Node(string id, int maxLevel = 1, params string[] prerequisites)
        {
            return new TechNode(id, maxLevel, prerequisites);
        }

        // ---- 定义 / 查询 ----

        [Test]
        public void Define_Then_HasAndGetNode()
        {
            TechTree tree = new TechTree();
            TechNode root = Node("root");
            tree.Define(root);

            Assert.IsTrue(tree.Has("root"));
            Assert.AreSame(root, tree.GetNode("root"));
        }

        [Test]
        public void GetNode_Missing_ReturnsNull()
        {
            TechTree tree = new TechTree();

            Assert.IsFalse(tree.Has("missing"));
            Assert.IsNull(tree.GetNode("missing"));
        }

        [Test]
        public void Define_Duplicate_Throws()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("dup"));

            Assert.Throws<System.ArgumentException>(() => tree.Define(Node("dup")));
        }

        [Test]
        public void Define_Null_Throws()
        {
            TechTree tree = new TechTree();

            Assert.Throws<System.ArgumentNullException>(() => tree.Define(null));
        }

        [Test]
        public void Nodes_EnumeratesAllDefined()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("a"));
            tree.Define(Node("b"));

            List<string> ids = new List<string>();
            foreach (TechNode node in tree.Nodes)
            {
                ids.Add(node.Id);
            }

            Assert.AreEqual(2, ids.Count);
            CollectionAssert.Contains(ids, "a");
            CollectionAssert.Contains(ids, "b");
        }

        // ---- 初始状态 ----

        [Test]
        public void RootNode_NoPrerequisites_IsAvailable()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("root"));

            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("root"));
            Assert.IsTrue(tree.ArePrerequisitesMet("root"));
            Assert.IsTrue(tree.CanUnlock("root"));
            Assert.AreEqual(0, tree.GetLevel("root"));
            Assert.IsFalse(tree.IsUnlocked("root"));
            Assert.IsFalse(tree.IsMaxed("root"));
        }

        [Test]
        public void NodeWithUnmetPrerequisite_IsLocked()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("root"));
            tree.Define(Node("child", 1, "root"));

            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("child"));
            Assert.IsFalse(tree.ArePrerequisitesMet("child"));
            Assert.IsFalse(tree.CanUnlock("child"));
        }

        [Test]
        public void GetStatus_MissingNode_IsLocked()
        {
            TechTree tree = new TechTree();

            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("nope"));
            Assert.IsFalse(tree.CanUnlock("nope"));
            Assert.IsFalse(tree.IsUnlocked("nope"));
            Assert.IsFalse(tree.IsMaxed("nope"));
            Assert.AreEqual(0, tree.GetLevel("nope"));
        }

        // ---- 解锁与级联 ----

        [Test]
        public void UnlockRoot_DependentBecomesAvailable_FiredOnce()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("root"));
            tree.Define(Node("child", 1, "root"));

            List<string> availableEvents = new List<string>();
            tree.OnNodeAvailable += (t, id) => availableEvents.Add(id);

            Assert.IsTrue(tree.Unlock("root"));

            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("child"));
            Assert.IsTrue(tree.CanUnlock("child"));
            // child 由 Locked 转为 Available，仅通知一次；root 本身不通知（它从未处于 Locked）。
            Assert.AreEqual(1, availableEvents.Count);
            Assert.AreEqual("child", availableEvents[0]);
        }

        [Test]
        public void Unlock_ReturnsFalse_WhenPrerequisiteUnmet()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("root"));
            tree.Define(Node("child", 1, "root"));

            Assert.IsFalse(tree.CanUnlock("child"));
            Assert.IsFalse(tree.Unlock("child"));
            Assert.AreEqual(0, tree.GetLevel("child"));
        }

        [Test]
        public void Unlock_ReturnsFalse_WhenAlreadyMaxed()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("root"));

            Assert.IsTrue(tree.Unlock("root"));
            Assert.IsTrue(tree.IsMaxed("root"));
            Assert.IsFalse(tree.CanUnlock("root"));
            Assert.IsFalse(tree.Unlock("root"));
            Assert.AreEqual(1, tree.GetLevel("root"));
        }

        [Test]
        public void Unlock_MissingNode_ReturnsFalse()
        {
            TechTree tree = new TechTree();

            Assert.IsFalse(tree.Unlock("ghost"));
        }

        // ---- 多级节点 ----

        [Test]
        public void MultiLevelNode_UnlockedThenMaxed_LevelChangedPerUnlock()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("skill", 3));

            List<int> levels = new List<int>();
            tree.OnLevelChanged += (t, id, level) =>
            {
                Assert.AreSame(tree, t);
                Assert.AreEqual("skill", id);
                levels.Add(level);
            };

            // 0 -> 1：Unlocked
            Assert.IsTrue(tree.Unlock("skill"));
            Assert.AreEqual(1, tree.GetLevel("skill"));
            Assert.IsTrue(tree.IsUnlocked("skill"));
            Assert.IsFalse(tree.IsMaxed("skill"));
            Assert.AreEqual(TechNodeStatus.Unlocked, tree.GetStatus("skill"));

            // 1 -> 2：仍 Unlocked
            Assert.IsTrue(tree.Unlock("skill"));
            Assert.AreEqual(TechNodeStatus.Unlocked, tree.GetStatus("skill"));

            // 2 -> 3：Maxed
            Assert.IsTrue(tree.Unlock("skill"));
            Assert.AreEqual(3, tree.GetLevel("skill"));
            Assert.IsTrue(tree.IsMaxed("skill"));
            Assert.AreEqual(TechNodeStatus.Maxed, tree.GetStatus("skill"));

            // 不能再升级
            Assert.IsFalse(tree.CanUnlock("skill"));
            Assert.IsFalse(tree.Unlock("skill"));

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, levels);
        }

        [Test]
        public void MultiLevelDependent_BecomesAvailableOnce_OnFirstUnlockOfPrereq()
        {
            // 前置为多级节点：仅在其首次解锁（0->1）时通知依赖者一次，后续升级不再重复通知。
            TechTree tree = new TechTree();
            tree.Define(Node("base", 3));
            tree.Define(Node("dependent", 1, "base"));

            List<string> availableEvents = new List<string>();
            tree.OnNodeAvailable += (t, id) => availableEvents.Add(id);

            tree.Unlock("base"); // 0 -> 1
            tree.Unlock("base"); // 1 -> 2
            tree.Unlock("base"); // 2 -> 3

            Assert.AreEqual(1, availableEvents.Count);
            Assert.AreEqual("dependent", availableEvents[0]);
        }

        // ---- 链式级联 ----

        [Test]
        public void Cascade_AThenBThenC_UnlockInOrder()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("A"));
            tree.Define(Node("B", 1, "A"));
            tree.Define(Node("C", 1, "B"));

            List<string> availableEvents = new List<string>();
            tree.OnNodeAvailable += (t, id) => availableEvents.Add(id);

            // 初始：A 可用，B/C 锁定
            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("A"));
            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("B"));
            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("C"));

            // 解锁 A -> B 可用，C 仍锁定
            Assert.IsTrue(tree.Unlock("A"));
            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("B"));
            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("C"));

            // 解锁 B -> C 可用
            Assert.IsTrue(tree.Unlock("B"));
            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("C"));

            // 解锁 C
            Assert.IsTrue(tree.Unlock("C"));
            Assert.IsTrue(tree.IsUnlocked("C"));

            // 通知顺序：B 在前，C 在后，各一次。
            CollectionAssert.AreEqual(new[] { "B", "C" }, availableEvents);
        }

        [Test]
        public void MultiplePrerequisites_AvailableOnlyWhenAllUnlocked()
        {
            TechTree tree = new TechTree();
            tree.Define(Node("p1"));
            tree.Define(Node("p2"));
            tree.Define(Node("target", 1, "p1", "p2"));

            List<string> availableEvents = new List<string>();
            tree.OnNodeAvailable += (t, id) => availableEvents.Add(id);

            tree.Unlock("p1");
            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("target"));
            Assert.IsFalse(tree.ArePrerequisitesMet("target"));
            CollectionAssert.DoesNotContain(availableEvents, "target");

            tree.Unlock("p2");
            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("target"));
            Assert.IsTrue(tree.ArePrerequisitesMet("target"));
            Assert.AreEqual(1, availableEvents.Count);
            Assert.AreEqual("target", availableEvents[0]);
        }

        [Test]
        public void AvailableFromDefinition_NeverFiresOnNodeAvailable()
        {
            // 天生可用的根节点（定义即满足前置）从未经历 Locked->Available 转变，
            // 因此即便后续发生其它解锁，也不应被当作“新可用”而通知。
            TechTree tree = new TechTree();
            tree.Define(Node("rootA"));
            tree.Define(Node("rootB"));

            List<string> availableEvents = new List<string>();
            tree.OnNodeAvailable += (t, id) => availableEvents.Add(id);

            tree.Unlock("rootA");

            // rootB 自始至终都是 Available，不应因 rootA 的解锁而触发通知。
            Assert.AreEqual(0, availableEvents.Count);
            Assert.AreEqual(TechNodeStatus.Available, tree.GetStatus("rootB"));
        }

        // ---- 缺失前置 ----

        [Test]
        public void MissingPrerequisiteId_KeepsNodeLocked()
        {
            TechTree tree = new TechTree();
            // 前置 "ghost" 从未定义，应视为永远无法满足。
            tree.Define(Node("node", 1, "ghost"));

            Assert.AreEqual(TechNodeStatus.Locked, tree.GetStatus("node"));
            Assert.IsFalse(tree.ArePrerequisitesMet("node"));
            Assert.IsFalse(tree.CanUnlock("node"));
            Assert.IsFalse(tree.Unlock("node"));
        }

        // ---- TechNode 归一化 ----

        [Test]
        public void TechNode_MaxLevelBelowOne_NormalizedToOne()
        {
            TechNode zero = new TechNode("z", 0);
            TechNode negative = new TechNode("n", -3);

            Assert.AreEqual(1, zero.MaxLevel);
            Assert.AreEqual(1, negative.MaxLevel);
        }

        [Test]
        public void TechNode_NullPrerequisites_BecomesEmpty()
        {
            TechNode node = new TechNode("x");

            Assert.IsNotNull(node.Prerequisites);
            Assert.AreEqual(0, node.Prerequisites.Count);
        }

        [Test]
        public void TechNode_EmptyId_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new TechNode(null));
            Assert.Throws<System.ArgumentException>(() => new TechNode(string.Empty));
        }

        [Test]
        public void TechNode_StoresPayload()
        {
            object payload = new object();
            TechNode node = new TechNode("p", 2, null, payload);

            Assert.AreSame(payload, node.Payload);
            Assert.AreEqual(2, node.MaxLevel);
        }
    }
}
