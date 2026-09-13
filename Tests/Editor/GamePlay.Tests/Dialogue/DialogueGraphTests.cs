//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Dialogue;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Dialogue
{
    /// <summary>
    /// 针对 <see cref="DialogueGraph"/> 的节点注册、重复 Id、查询缺失与节点计数的单元测试。
    /// </summary>
    [TestFixture]
    public class DialogueGraphTests
    {
        private static LineNode Line(string id, string next)
        {
            return new LineNode(id, "NPC", "...", next);
        }

        [Test]
        public void AddNode_IncrementsCountAndIsFluent()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            DialogueGraph returned = graph.AddNode(Line("n1", "n2")).AddNode(Line("n2", null));

            Assert.AreSame(graph, returned);
            Assert.AreEqual(2, graph.NodeCount);
        }

        [Test]
        public void AddNode_DuplicateId_Throws()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            graph.AddNode(Line("n1", null));

            Assert.Throws<System.ArgumentException>(() => graph.AddNode(Line("n1", null)));
        }

        [Test]
        public void GetNode_Missing_ReturnsNull()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            graph.AddNode(Line("n1", null));

            Assert.IsNull(graph.GetNode("nope"));
        }

        [Test]
        public void TryGetNode_Missing_ReturnsFalse()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            graph.AddNode(Line("n1", null));

            Assert.IsFalse(graph.TryGetNode("nope", out DialogueNode node));
            Assert.IsNull(node);
        }

        [Test]
        public void TryGetNode_Existing_ReturnsTrue()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            LineNode n1 = Line("n1", null);
            graph.AddNode(n1);

            Assert.IsTrue(graph.TryGetNode("n1", out DialogueNode node));
            Assert.AreSame(n1, node);
        }

        [Test]
        public void AddNode_Null_Throws()
        {
            DialogueGraph graph = new DialogueGraph("g", "n1");
            Assert.Throws<System.ArgumentNullException>(() => graph.AddNode(null));
        }
    }
}
