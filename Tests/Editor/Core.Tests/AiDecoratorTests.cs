//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.AI;

namespace EjoyFramework.Tests
{
    public class AiDecoratorTests
    {
        private sealed class Owner
        {
            public int Counter;
        }

        private static Blackboard<Owner> NewBb() => new Blackboard<Owner>();

        // ===== Inverter =====

        [Test]
        public void Inverter_FlipsSuccessToFailure()
        {
            var inv = new InverterNode<Owner>(new ConditionNode<Owner>((o, bb) => true));
            Assert.AreEqual(NodeStatus.Failure, inv.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Inverter_FlipsFailureToSuccess()
        {
            var inv = new InverterNode<Owner>(new ConditionNode<Owner>((o, bb) => false));
            Assert.AreEqual(NodeStatus.Success, inv.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Inverter_PassesRunningThrough()
        {
            var inv = new InverterNode<Owner>(new ActionNode<Owner>((o, bb) => NodeStatus.Running));
            Assert.AreEqual(NodeStatus.Running, inv.Tick(new Owner(), NewBb()));
        }

        // ===== Succeeder =====

        [Test]
        public void Succeeder_TurnsFailureIntoSuccess()
        {
            var s = new SucceederNode<Owner>(new ConditionNode<Owner>((o, bb) => false));
            Assert.AreEqual(NodeStatus.Success, s.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Succeeder_PassesRunningThrough()
        {
            var s = new SucceederNode<Owner>(new ActionNode<Owner>((o, bb) => NodeStatus.Running));
            Assert.AreEqual(NodeStatus.Running, s.Tick(new Owner(), NewBb()));
        }

        // ===== Parallel =====

        [Test]
        public void Parallel_RequireOne_SucceedsWhenOneChildSucceeds()
        {
            var par = new ParallelNode<Owner>(ParallelPolicy.RequireOne)
                .Add(new ConditionNode<Owner>((o, bb) => false))
                .Add(new ConditionNode<Owner>((o, bb) => true));
            Assert.AreEqual(NodeStatus.Success, par.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Parallel_RequireOne_FailsWhenAllChildrenFail()
        {
            var par = new ParallelNode<Owner>(ParallelPolicy.RequireOne)
                .Add(new ConditionNode<Owner>((o, bb) => false))
                .Add(new ConditionNode<Owner>((o, bb) => false));
            Assert.AreEqual(NodeStatus.Failure, par.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Parallel_RequireAll_FailsWhenOneChildFails()
        {
            var par = new ParallelNode<Owner>(ParallelPolicy.RequireAll)
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ConditionNode<Owner>((o, bb) => false));
            Assert.AreEqual(NodeStatus.Failure, par.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Parallel_RequireAll_SucceedsWhenAllChildrenSucceed()
        {
            var par = new ParallelNode<Owner>(ParallelPolicy.RequireAll)
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ConditionNode<Owner>((o, bb) => true));
            Assert.AreEqual(NodeStatus.Success, par.Tick(new Owner(), NewBb()));
        }

        [Test]
        public void Parallel_RequireAll_RunningWhenAnyChildRunning()
        {
            var par = new ParallelNode<Owner>(ParallelPolicy.RequireAll)
                .Add(new ConditionNode<Owner>((o, bb) => true))
                .Add(new ActionNode<Owner>((o, bb) => NodeStatus.Running));
            Assert.AreEqual(NodeStatus.Running, par.Tick(new Owner(), NewBb()));
        }

        // ===== Repeat =====

        [Test]
        public void Repeat_TicksChildExactlyNTimes()
        {
            var owner = new Owner();
            var repeat = new RepeatNode<Owner>(
                new ActionNode<Owner>((o, bb) => { o.Counter++; return NodeStatus.Success; }),
                count: 3);
            Assert.AreEqual(NodeStatus.Success, repeat.Tick(owner, NewBb()));
            Assert.AreEqual(3, owner.Counter);
        }

        [Test]
        public void Repeat_FailsImmediatelyWhenChildFails()
        {
            var owner = new Owner();
            var repeat = new RepeatNode<Owner>(
                new ActionNode<Owner>((o, bb) => { o.Counter++; return NodeStatus.Failure; }),
                count: 5);
            Assert.AreEqual(NodeStatus.Failure, repeat.Tick(owner, NewBb()));
            Assert.AreEqual(1, owner.Counter);
        }

        [Test]
        public void Repeat_PassesRunningThrough()
        {
            int ticks = 0;
            var repeat = new RepeatNode<Owner>(
                new ActionNode<Owner>((o, bb) => { ticks++; return NodeStatus.Running; }),
                count: 3);
            Assert.AreEqual(NodeStatus.Running, repeat.Tick(new Owner(), NewBb()));
            Assert.AreEqual(1, ticks);
        }

        [Test]
        public void Repeat_InfiniteWithSuccessChild_YieldsRunningPerTick_NoHang()
        {
            // count<=0 表示无限重复；子节点同步返回 Success 时，每次 Tick 应让出 Running，
            // 而非在 while(true) 内就地重启子节点导致永不退出、卡死主线程。
            int ticks = 0;
            var repeat = new RepeatNode<Owner>(
                new ActionNode<Owner>((o, bb) => { ticks++; return NodeStatus.Success; }),
                count: 0);
            Assert.AreEqual(NodeStatus.Running, repeat.Tick(new Owner(), NewBb()));
            Assert.AreEqual(1, ticks);
            Assert.AreEqual(NodeStatus.Running, repeat.Tick(new Owner(), NewBb()));
            Assert.AreEqual(2, ticks);
        }

        // ===== Cooldown =====

        [Test]
        public void Cooldown_GatesBySubsequentTimeAfterSuccess()
        {
            int runs = 0;
            var node = new CooldownNode<Owner>(
                new ActionNode<Owner>((o, bb) => { runs++; return NodeStatus.Success; }),
                cooldownSeconds: 2f);
            var bb = NewBb();

            // First tick: ready, child runs, success, cooldown starts.
            bb.Set(BehaviorTree<Owner>.DeltaKey, 0f);
            Assert.AreEqual(NodeStatus.Success, node.Tick(new Owner(), bb));
            Assert.AreEqual(1, runs);

            // Within cooldown: blocked, child NOT run.
            bb.Set(BehaviorTree<Owner>.DeltaKey, 1f);
            Assert.AreEqual(NodeStatus.Failure, node.Tick(new Owner(), bb));
            Assert.AreEqual(1, runs);

            // Accumulated time passes cooldown: ready again.
            bb.Set(BehaviorTree<Owner>.DeltaKey, 1.5f);
            Assert.AreEqual(NodeStatus.Success, node.Tick(new Owner(), bb));
            Assert.AreEqual(2, runs);
        }

        // ===== Wait =====

        [Test]
        public void Wait_RunningUntilAccumulatedTimeReached()
        {
            var node = new WaitNode<Owner>(waitSeconds: 3f);

            var tree = new BehaviorTree<Owner>(node);
            Assert.AreEqual(NodeStatus.Running, tree.Tick(new Owner(), 1f));
            Assert.AreEqual(NodeStatus.Running, tree.Tick(new Owner(), 1f));
            Assert.AreEqual(NodeStatus.Success, tree.Tick(new Owner(), 1.5f));
        }

        // ===== Tree time-aware Tick overload feeds time-gated nodes =====

        [Test]
        public void Tree_TickWithDelta_DrivesCooldownNode()
        {
            int runs = 0;
            var tree = new BehaviorTree<Owner>(new CooldownNode<Owner>(
                new ActionNode<Owner>((o, bb) => { runs++; return NodeStatus.Success; }),
                cooldownSeconds: 1f));

            Assert.AreEqual(NodeStatus.Success, tree.Tick(new Owner(), 0f));
            Assert.AreEqual(NodeStatus.Failure, tree.Tick(new Owner(), 0.5f));  // still cooling
            Assert.AreEqual(NodeStatus.Success, tree.Tick(new Owner(), 0.6f));  // ready
            Assert.AreEqual(2, runs);
        }
    }
}
