//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.AI;

namespace EjoyFramework.Tests
{
    public class BehaviorTreeTests
    {
        private sealed class Owner
        {
            public int Hp = 100;
            public int Mana = 50;
            public string LastAction;
        }

        // ===== Blackboard =====

        [Test]
        public void Blackboard_SetGet_RoundTrip()
        {
            var bb = new Blackboard<Owner>();
            bb.Set("target", "Goblin");
            bb.Set("range", 10);
            Assert.AreEqual("Goblin", bb.Get<string>("target"));
            Assert.AreEqual(10, bb.Get<int>("range"));
        }

        [Test]
        public void Blackboard_Defaults_ForMissingKey()
        {
            var bb = new Blackboard<Owner>();
            Assert.AreEqual(default(int), bb.Get<int>("missing"));
            Assert.AreEqual("fallback", bb.Get<string>("missing", "fallback"));
        }

        [Test]
        public void Blackboard_Remove_Has()
        {
            var bb = new Blackboard<Owner>();
            bb.Set("k", 1);
            Assert.IsTrue(bb.Has("k"));
            Assert.IsTrue(bb.Remove("k"));
            Assert.IsFalse(bb.Has("k"));
        }

        // ===== Sequence =====

        [Test]
        public void Sequence_AllSuccess_ReturnsSuccess()
        {
            var seq = new SequenceNode<Owner>()
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ActionNode<Owner>((o, bb) => NodeStatus.Success));
            Assert.AreEqual(NodeStatus.Success, seq.Tick(new Owner(), new Blackboard<Owner>()));
        }

        [Test]
        public void Sequence_OneFailure_ReturnsFailure()
        {
            var seq = new SequenceNode<Owner>()
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ConditionNode<Owner>((o, bb) => false))    // fail
                .Add(new ActionNode<Owner>((o, bb) => NodeStatus.Success));   // 不会执行
            int aCalled = 0;
            seq = new SequenceNode<Owner>()
                .Add(new ActionNode<Owner>((o, bb) => { aCalled++; return NodeStatus.Success; }))
                .Add(new ConditionNode<Owner>((o, bb) => false));
            Assert.AreEqual(NodeStatus.Failure, seq.Tick(new Owner(), new Blackboard<Owner>()));
        }

        // ===== Selector =====

        [Test]
        public void Selector_FirstSuccess_ReturnsSuccess()
        {
            var sel = new SelectorNode<Owner>()
                .Add(new ConditionNode<Owner>((o, bb) => false))
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ActionNode<Owner>((o, bb) => NodeStatus.Failure));
            Assert.AreEqual(NodeStatus.Success, sel.Tick(new Owner(), new Blackboard<Owner>()));
        }

        [Test]
        public void Selector_AllFail_ReturnsFailure()
        {
            var sel = new SelectorNode<Owner>()
                .Add(new ConditionNode<Owner>((o, bb) => false))
                .Add(new ActionNode<Owner>((o, bb) => NodeStatus.Failure));
            Assert.AreEqual(NodeStatus.Failure, sel.Tick(new Owner(), new Blackboard<Owner>()));
        }

        // ===== Inverter =====

        [Test]
        public void Inverter_FlipsResult()
        {
            var inv = new InverterNode<Owner>(new ConditionNode<Owner>((o, bb) => true));
            Assert.AreEqual(NodeStatus.Failure, inv.Tick(new Owner(), new Blackboard<Owner>()));
        }

        // ===== Tree + Running =====

        [Test]
        public void Tree_RunningNode_PausesSequence()
        {
            int ticks = 0;
            var slowAction = new ActionNode<Owner>((o, bb) =>
            {
                ticks++;
                if (ticks < 3) return NodeStatus.Running;
                return NodeStatus.Success;
            });
            var seq = new SequenceNode<Owner>()
                .Add(slowAction)
                .Add(new ActionNode<Owner>((o, bb) => { o.LastAction = "done"; return NodeStatus.Success; }));

            var tree = new BehaviorTree<Owner>(seq);
            var owner = new Owner();
            Assert.AreEqual(NodeStatus.Running, tree.Tick(owner));
            Assert.AreEqual(NodeStatus.Running, tree.Tick(owner));
            Assert.AreEqual(NodeStatus.Success, tree.Tick(owner));
            Assert.AreEqual("done", owner.LastAction);
        }

        // ===== Realistic AI 用例 =====

        [Test]
        public void EnemyAi_Patrol_OnSpot_Attack()
        {
            var owner = new Owner();
            var bb = new Blackboard<Owner>();
            bool enemySpotted = false;

            var tree = new BehaviorTree<Owner>(
                new SelectorNode<Owner>()
                    .Add(new SequenceNode<Owner>()
                        .Add(new ConditionNode<Owner>((o, b) => enemySpotted))
                        .Add(new ActionNode<Owner>((o, b) => { o.LastAction = "attack"; return NodeStatus.Success; })))
                    .Add(new ActionNode<Owner>((o, b) => { o.LastAction = "patrol"; return NodeStatus.Success; })),
                bb);

            tree.Tick(owner);
            Assert.AreEqual("patrol", owner.LastAction);

            enemySpotted = true;
            tree.Tick(owner);
            Assert.AreEqual("attack", owner.LastAction);
        }
    }
}
