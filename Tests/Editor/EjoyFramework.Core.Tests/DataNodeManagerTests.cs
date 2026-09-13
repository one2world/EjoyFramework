//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.DataNode;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// DataNodeManager 单测（Phase 11.7）。
    /// 覆盖：路径解析（. / \）、Set/Get、GetOrAdd 创建链、RemoveNode、Clear、子节点枚举。
    /// </summary>
    public class DataNodeManagerTests
    {
        private static IDataNodeManager NewManager()
        {
            var t = typeof(IDataNodeManager).Assembly.GetType("EjoyFramework.Core.DataNode.DataNodeManager");
            return (IDataNodeManager)Activator.CreateInstance(t, true);
        }

        private static VarInt32 IntVar(int v)
        {
            var x = ReferencePool.Acquire<VarInt32>();
            x.Value = v;
            return x;
        }

        private static VarString StringVar(string v)
        {
            var x = ReferencePool.Acquire<VarString>();
            x.Value = v;
            return x;
        }

        [SetUp]
        public void Setup() { Framework.MarkMainThread(); }

        [Test]
        public void Root_ExistsAndNamedRoot()
        {
            var m = NewManager();
            Assert.IsNotNull(m.Root);
            Assert.AreEqual("Root", m.Root.Name);
            Assert.AreEqual(0, m.Root.ChildCount);
        }

        [Test]
        public void GetNode_EmptyPath_ReturnsRoot()
        {
            var m = NewManager();
            Assert.AreSame(m.Root, m.GetNode(""));
            Assert.AreSame(m.Root, m.GetNode(null));
        }

        [Test]
        public void GetNode_NonexistentPath_ReturnsNull()
        {
            var m = NewManager();
            Assert.IsNull(m.GetNode("a.b.c"));
        }

        [Test]
        public void GetOrAddNode_DotSeparated_CreatesChain()
        {
            var m = NewManager();
            var n = m.GetOrAddNode("player.stats.hp");
            Assert.IsNotNull(n);
            Assert.AreEqual("hp", n.Name);
            // 路径上 3 层都存在
            Assert.IsNotNull(m.GetNode("player"));
            Assert.IsNotNull(m.GetNode("player.stats"));
            Assert.AreSame(n, m.GetNode("player.stats.hp"));
        }

        [Test]
        public void GetOrAddNode_SlashSeparated_AlsoWorks()
        {
            var m = NewManager();
            var n = m.GetOrAddNode("a/b/c");
            Assert.IsNotNull(n);
            // 同节点不同分隔符也能再 Get
            Assert.AreSame(n, m.GetNode("a.b.c"));
        }

        [Test]
        public void SetData_GetData_RoundTrip()
        {
            var m = NewManager();
            var node = m.GetOrAddNode("player.hp");
            node.SetData(IntVar(100));

            var v = node.GetData<VarInt32>();
            Assert.IsNotNull(v);
            Assert.AreEqual(100, v.Value);
        }

        [Test]
        public void SetData_OverwritePreviousValue_ReplacesIt()
        {
            var m = NewManager();
            var node = m.GetOrAddNode("counter");
            node.SetData(IntVar(1));
            node.SetData(IntVar(2)); // 内部会 Release 旧 var 回池

            Assert.AreEqual(2, node.GetData<VarInt32>().Value);
        }

        [Test]
        public void RemoveNode_RemovesLeaf()
        {
            var m = NewManager();
            m.GetOrAddNode("a.b.c");
            Assert.IsNotNull(m.GetNode("a.b.c"));

            m.RemoveNode("a.b.c");
            Assert.IsNull(m.GetNode("a.b.c"));
            Assert.IsNotNull(m.GetNode("a.b"), "Parent should survive");
        }

        [Test]
        public void RemoveNode_NonexistentPath_NoThrow()
        {
            var m = NewManager();
            Assert.DoesNotThrow(() => m.RemoveNode("nowhere.does.not.exist"));
        }

        [Test]
        public void Clear_RemovesAllNodes()
        {
            var m = NewManager();
            m.GetOrAddNode("a");
            m.GetOrAddNode("b");
            m.GetOrAddNode("a.x");
            Assert.AreEqual(2, m.Root.ChildCount);

            m.Clear();
            Assert.AreEqual(0, m.Root.ChildCount);
        }

        [Test]
        public void GetAllChild_ReturnsAllImmediateChildren()
        {
            var m = NewManager();
            m.GetOrAddNode("a");
            m.GetOrAddNode("b");
            m.GetOrAddNode("c");

            var arr = m.Root.GetAllChild();
            Assert.AreEqual(3, arr.Length);
        }

        [Test]
        public void FullName_ReflectsPath()
        {
            var m = NewManager();
            var n = m.GetOrAddNode("a.b.c");
            Assert.AreEqual("Root.a.b.c", n.FullName);
        }
    }
}
