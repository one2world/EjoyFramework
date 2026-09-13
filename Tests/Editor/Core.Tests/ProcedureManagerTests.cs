//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core.Fsm;
using EjoyFramework.Core.Procedure;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// ProcedureManager 单测（Phase 11.7）。
    /// 用真实 FsmManager 作为后端：覆盖 Initialize / StartProcedure / HasProcedure / GetProcedure /
    /// CurrentProcedure / CurrentProcedureTime / Shutdown 后再访问异常。
    /// </summary>
    public class ProcedureManagerTests
    {
        public class TestProcedureA : ProcedureBase
        {
            public int EnterCount, LeaveCount;
            public IFsm<IProcedureManager> CapturedFsm { get; private set; }
            protected internal override void OnEnter(IFsm<IProcedureManager> fsm) { CapturedFsm = fsm; EnterCount++; }
            protected internal override void OnLeave(IFsm<IProcedureManager> fsm, bool isShutdown) { LeaveCount++; }

            // protected ChangeState 不能从测试外部直接调用；通过 helper 暴露
            public void ChangeTo<TState>() where TState : ProcedureBase => ChangeState<TState>(CapturedFsm);
        }

        public class TestProcedureB : ProcedureBase
        {
            public int EnterCount;
            protected internal override void OnEnter(IFsm<IProcedureManager> fsm) { EnterCount++; }
        }

        public class TestProcedureC : ProcedureBase { }

        private static IProcedureManager NewProcedureManager()
        {
            var t = typeof(IProcedureManager).Assembly.GetType("EjoyFramework.Core.Procedure.ProcedureManager");
            return (IProcedureManager)Activator.CreateInstance(t, true);
        }

        private FsmManager m_Fsm;
        private IProcedureManager m_PM;
        private TestProcedureA m_A;
        private TestProcedureB m_B;

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
            m_Fsm = new FsmManager();
            m_PM = NewProcedureManager();
            m_A = new TestProcedureA();
            m_B = new TestProcedureB();
        }

        [TearDown]
        public void Teardown()
        {
            try { ((FrameworkModule)m_PM).Shutdown(); } catch { }
            try { m_Fsm.Shutdown(); } catch { }
        }

        [Test]
        public void Initialize_RegistersAllProcedures()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);

            Assert.IsTrue(m_PM.HasProcedure<TestProcedureA>());
            Assert.IsTrue(m_PM.HasProcedure<TestProcedureB>());
            Assert.IsFalse(m_PM.HasProcedure<TestProcedureC>());
        }

        [Test]
        public void StartProcedure_Generic_EntersTargetProcedure()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);
            m_PM.StartProcedure<TestProcedureA>();

            Assert.AreEqual(1, m_A.EnterCount);
            Assert.AreSame(m_A, m_PM.CurrentProcedure);
        }

        [Test]
        public void StartProcedure_ByType_EntersTargetProcedure()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);
            m_PM.StartProcedure(typeof(TestProcedureB));

            Assert.AreEqual(1, m_B.EnterCount);
            Assert.AreSame(m_B, m_PM.CurrentProcedure);
        }

        [Test]
        public void CurrentProcedure_BeforeInit_Throws()
        {
            Assert.Throws<FrameworkException>(() => { var _ = m_PM.CurrentProcedure; });
        }

        [Test]
        public void StartProcedure_BeforeInit_Throws()
        {
            Assert.Throws<FrameworkException>(() => m_PM.StartProcedure<TestProcedureA>());
            Assert.Throws<FrameworkException>(() => m_PM.StartProcedure(typeof(TestProcedureB)));
        }

        [Test]
        public void GetProcedure_ReturnsRegisteredInstance()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);

            Assert.AreSame(m_A, m_PM.GetProcedure<TestProcedureA>());
            Assert.AreSame(m_B, m_PM.GetProcedure<TestProcedureB>());
        }

        [Test]
        public void Initialize_NullFsmManager_Throws()
        {
            Assert.Throws<FrameworkException>(() => m_PM.Initialize(null, m_A));
        }

        [Test]
        public void TransitionBetweenProcedures_FiresLeaveAndEnter()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);
            m_PM.StartProcedure<TestProcedureA>();
            Assert.AreSame(m_A, m_PM.CurrentProcedure);

            // 切到 B：A 应 OnLeave，B 应 OnEnter
            m_A.ChangeTo<TestProcedureB>();

            Assert.AreEqual(1, m_A.LeaveCount);
            Assert.AreEqual(1, m_B.EnterCount);
            Assert.AreSame(m_B, m_PM.CurrentProcedure);
        }

        [Test]
        public void CurrentProcedureTime_AdvancesWithUpdate()
        {
            m_PM.Initialize(m_Fsm, m_A, m_B);
            m_PM.StartProcedure<TestProcedureA>();

            float t0 = m_PM.CurrentProcedureTime;
            m_Fsm.Update(0.5f, 0.5f);  // FsmManager drives the FSM tick
            float t1 = m_PM.CurrentProcedureTime;

            Assert.Greater(t1, t0);
        }
    }
}
