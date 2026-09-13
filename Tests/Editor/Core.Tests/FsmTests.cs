//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Fsm;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    public class FsmTests
    {
        public class FakeOwner { }

        public class StateA : FsmState<FakeOwner>
        {
            public bool Entered, Left;
            public bool LeftWasShutdown;

            protected internal override void OnEnter(IFsm<FakeOwner> fsm) { Entered = true; }
            protected internal override void OnLeave(IFsm<FakeOwner> fsm, bool isShutdown) { Left = true; LeftWasShutdown = isShutdown; }

            public void ChangeToReentrant(IFsm<FakeOwner> fsm)
            {
                ChangeState<ReentrantState>(fsm);
            }
        }

        public class StateB : FsmState<FakeOwner>
        {
            public bool Entered;
            protected internal override void OnEnter(IFsm<FakeOwner> fsm) { Entered = true; }
        }

        public class ReentrantState : FsmState<FakeOwner>
        {
            public bool ReentryReturned;
            public bool TryReenter;

            protected internal override void OnEnter(IFsm<FakeOwner> fsm)
            {
                if (!TryReenter) return;
                ChangeState<StateB>(fsm);
                ReentryReturned = true;
            }
        }

        public class LoopStateA : FsmState<FakeOwner>
        {
            protected internal override void OnEnter(IFsm<FakeOwner> fsm) { ChangeState<LoopStateB>(fsm); }
        }

        public class LoopStateB : FsmState<FakeOwner>
        {
            protected internal override void OnEnter(IFsm<FakeOwner> fsm) { ChangeState<LoopStateA>(fsm); }
        }

        [Test]
        public void Start_TransitionsToInitialState()
        {
            var owner = new FakeOwner();
            var a = new StateA();
            var fsmMgr = new FsmManager();
            var fsm = fsmMgr.CreateFsm(owner, a);
            fsm.Start<StateA>();
            Assert.IsTrue(a.Entered);
            Assert.AreSame(a, fsm.CurrentState);
            fsmMgr.Shutdown();
        }

        [Test]
        public void Shutdown_InvokesOnLeaveWithIsShutdownTrue()
        {
            var owner = new FakeOwner();
            var a = new StateA();
            var fsmMgr = new FsmManager();
            var fsm = fsmMgr.CreateFsm(owner, a);
            fsm.Start<StateA>();
            fsmMgr.Shutdown();

            // 关键修复：Clear 路径必须触发 OnLeave(isShutdown=true)
            Assert.IsTrue(a.Left, "OnLeave should be called on shutdown");
            Assert.IsTrue(a.LeftWasShutdown, "isShutdown flag should be true");
        }

        [Test]
        public void ChangeState_NestedDuringOnEnter_IsDeferredUntilOuterTransitionCompletes()
        {
            var owner = new FakeOwner();
            var a = new StateA();
            var reentry = new ReentrantState { TryReenter = true };
            var b = new StateB();
            var fsmMgr = new FsmManager();
            var fsm = fsmMgr.CreateFsm(owner, a, reentry, b);
            fsm.Start<StateA>();

            Assert.DoesNotThrow(() => a.ChangeToReentrant(fsm));

            Assert.IsTrue(reentry.ReentryReturned, "Nested ChangeState should return after enqueueing the transition.");
            Assert.IsTrue(b.Entered, "The deferred transition should run after the outer OnEnter completes.");
            Assert.AreSame(b, fsm.CurrentState);

            fsmMgr.Shutdown();
        }

        [Test]
        public void ChangeState_DeferredTransitionLoop_ThrowsDiagnosticException()
        {
            var fsmMgr = new FsmManager();
            var fsm = fsmMgr.CreateFsm(new FakeOwner(), new LoopStateA(), new LoopStateB());

            FrameworkException exception = Assert.Throws<FrameworkException>(() => fsm.Start<LoopStateA>());
            StringAssert.Contains("deferred state changes", exception.Message);

            fsmMgr.Shutdown();
        }
    }
}
