//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// Framework.Shutdown 反向遍历回归测试（Audit Phase 11.1）。
    /// 关闭顺序必须是 Priority 升序（低优先级先关），保证 Procedure(-2/-100)
    /// 这种依赖底层模块（Resource/Event/Fsm）的业务模块先于其依赖被关闭。
    ///
    /// 注意：Framework.RegisterModule 用 Module 的 CLR Type 去重，所以每个被测优先级必须有独立 class。
    /// </summary>
    public class FrameworkShutdownTests
    {
        // 记录所有 Module Shutdown 调用顺序的 Priority 值
        private static readonly List<int> s_ShutdownLog = new List<int>();

        // === 不同 Priority 的具体子类（CLR Type 必须各不相同） ===
        private sealed class P_Minus100 : FrameworkModule
        {
            public override int Priority => -100;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(-100); }
        }
        private sealed class P_Minus2 : FrameworkModule
        {
            public override int Priority => -2;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(-2); }
        }
        private sealed class P_Zero : FrameworkModule
        {
            public override int Priority => 0;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(0); }
        }
        private sealed class P_One : FrameworkModule
        {
            public override int Priority => 1;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(1); }
        }
        private sealed class P_Four : FrameworkModule
        {
            public override int Priority => 4;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(4); }
        }
        private sealed class P_Five : FrameworkModule
        {
            public override int Priority => 5;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(5); }
        }
        private sealed class P_Seven : FrameworkModule
        {
            public override int Priority => 7;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(7); }
        }
        private sealed class P_Ten : FrameworkModule
        {
            public override int Priority => 10;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(10); }
        }
        private sealed class P_Twenty : FrameworkModule
        {
            public override int Priority => 20;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(20); }
        }
        private sealed class P_Fifty : FrameworkModule
        {
            public override int Priority => 50;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(50); }
        }
        private sealed class P_Hundred : FrameworkModule
        {
            public override int Priority => 100;
            public override void Update(float a, float b) { }
            public override void Shutdown() { s_ShutdownLog.Add(100); }
        }
        private sealed class ThrowingModule : FrameworkModule
        {
            public override int Priority => 5;
            public override void Update(float a, float b) { }
            public override void Shutdown()
            {
                s_ShutdownLog.Add(5);
                throw new System.InvalidOperationException("Deliberately throws");
            }
        }

        [SetUp]
        public void Setup()
        {
            // 完整重置 Framework 静态状态（清模块 + 清 MainThreadId）
            Framework.ResetForEnterPlayMode();
            Framework.MarkMainThread();
            s_ShutdownLog.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // 清理：模块 + MainThreadId 全部归零，避免污染其他 fixture
            Framework.ResetForEnterPlayMode();
            s_ShutdownLog.Clear();
        }

        [Test]
        public void Shutdown_LowPriorityFirst_HighPriorityLast()
        {
            Framework.RegisterModule(new P_Ten());
            Framework.RegisterModule(new P_Zero());

            Framework.Shutdown();

            Assert.AreEqual(new[] { 0, 10 }, s_ShutdownLog.ToArray());
        }

        [Test]
        public void Shutdown_OrderIndependentOfRegistrationOrder()
        {
            // 反序注册结果应一致 —— 顺序由 Priority 决定
            Framework.RegisterModule(new P_Zero());
            Framework.RegisterModule(new P_Ten());

            Framework.Shutdown();

            Assert.AreEqual(new[] { 0, 10 }, s_ShutdownLog.ToArray());
        }

        [Test]
        public void Shutdown_FiveModules_AscendingPriority()
        {
            Framework.RegisterModule(new P_Hundred());
            Framework.RegisterModule(new P_Minus100());
            Framework.RegisterModule(new P_Fifty());
            Framework.RegisterModule(new P_Zero());
            Framework.RegisterModule(new P_Twenty());

            Framework.Shutdown();

            Assert.AreEqual(new[] { -100, 0, 20, 50, 100 }, s_ShutdownLog.ToArray());
        }

        [Test]
        public void Shutdown_ModuleThrows_OthersStillRun_OrderPreserved()
        {
            Framework.RegisterModule(new P_Ten());
            Framework.RegisterModule(new ThrowingModule());      // priority 5
            Framework.RegisterModule(new P_Zero());

            // 异常被框架吞掉，不阻塞后续 Shutdown
            Assert.DoesNotThrow(() => Framework.Shutdown());

            // 全部 3 个 Shutdown 都触发，且按升序
            Assert.AreEqual(new[] { 0, 5, 10 }, s_ShutdownLog.ToArray());
        }

        [Test]
        public void Shutdown_NegativePriority_ShutdownFirst()
        {
            // 等价 Procedure(-2) 必须先于其依赖（Event/Resource/Fsm 等高优先级）关闭
            Framework.RegisterModule(new P_Seven());    // Event-class
            Framework.RegisterModule(new P_Four());     // Resource-class
            Framework.RegisterModule(new P_One());      // Fsm-class
            Framework.RegisterModule(new P_Minus2());   // Procedure-class

            Framework.Shutdown();

            Assert.AreEqual(new[] { -2, 1, 4, 7 }, s_ShutdownLog.ToArray());
        }
    }
}
