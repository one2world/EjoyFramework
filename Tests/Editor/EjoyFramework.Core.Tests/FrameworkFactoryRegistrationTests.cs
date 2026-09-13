//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// Framework.RegisterFactory + GetModule 工厂路径回归测试（iOS IL2CPP 裁剪修复的运行时侧）。
    /// 覆盖：工厂创建、单例幂等、参数校验、工厂返回 null / 返回错误类型、同实现多接口共享单例。
    ///
    /// 注意：每个被测优先级/类型必须有独立 CLR Type（框架按具体类型去重）。
    /// ResetForEnterPlayMode 清模块实例与 per-T 缓存，但<b>不</b>清工厂字典——故每个用例显式重注册自己所需工厂。
    /// </summary>
    public class FrameworkFactoryRegistrationTests
    {
        // === 测试用模块接口与实现（CLR Type 各不相同） ===
        private interface IAlphaModule { }
        private interface IBetaModule { }
        private interface INullFactoryModule { }
        private interface IWrongTypeModule { }
        private interface ICycleModuleA { }
        private interface ICycleModuleB { }

        // 同时实现两个模块接口，用于"多接口共享单例"用例。
        private sealed class AlphaModule : FrameworkModule, IAlphaModule, IBetaModule
        {
            public override int Priority => 0;
            public override void Update(float elapseSeconds, float realElapseSeconds) { }
            public override void Shutdown() { }
        }

        // 不实现 IWrongTypeModule，用于验证 GetModule 对"工厂返回不匹配类型"的防御。
        private sealed class UnrelatedModule : FrameworkModule
        {
            public override int Priority => 0;
            public override void Update(float elapseSeconds, float realElapseSeconds) { }
            public override void Shutdown() { }
        }

        private sealed class CycleModuleA : FrameworkModule, ICycleModuleA
        {
            public override int Priority => 0;
            public override void Update(float elapseSeconds, float realElapseSeconds) { }
            public override void Shutdown() { }
        }

        private sealed class CycleModuleB : FrameworkModule, ICycleModuleB
        {
            public override int Priority => 0;
            public override void Update(float elapseSeconds, float realElapseSeconds) { }
            public override void Shutdown() { }
        }

        [SetUp]
        public void Setup()
        {
            Framework.ResetForEnterPlayMode();
            Framework.MarkMainThread();
        }

        [TearDown]
        public void TearDown()
        {
            Framework.ResetForEnterPlayMode();
        }

        [Test]
        public void RegisterFactory_ThenGetModule_ReturnsFactoryCreatedInstance()
        {
            Framework.RegisterFactory(typeof(IAlphaModule), static () => new AlphaModule());

            IAlphaModule m = Framework.GetModule<IAlphaModule>();

            Assert.IsNotNull(m);
            Assert.IsInstanceOf<AlphaModule>(m);
        }

        [Test]
        public void GetModule_ViaFactory_IsIdempotent_SameInstance()
        {
            Framework.RegisterFactory(typeof(IAlphaModule), static () => new AlphaModule());

            IAlphaModule first = Framework.GetModule<IAlphaModule>();
            IAlphaModule second = Framework.GetModule<IAlphaModule>();

            Assert.AreSame(first, second, "连续 GetModule 必须返回同一缓存实例");
        }

        [Test]
        public void RegisterFactory_NullInterface_Throws()
        {
            Assert.Throws<FrameworkException>(
                () => Framework.RegisterFactory(null, static () => new AlphaModule()));
        }

        [Test]
        public void RegisterFactory_NullFactory_Throws()
        {
            Assert.Throws<FrameworkException>(
                () => Framework.RegisterFactory(typeof(IAlphaModule), null));
        }

        [Test]
        public void GetModule_FactoryReturnsNull_Throws()
        {
            Framework.RegisterFactory(typeof(INullFactoryModule), static () => null);

            Assert.Throws<FrameworkException>(() => Framework.GetModule<INullFactoryModule>());
        }

        [Test]
        public void GetModule_FactoryReturnsTypeNotImplementingInterface_Throws()
        {
            // 工厂注册有误：为 IWrongTypeModule 提供了不实现它的 UnrelatedModule。
            Framework.RegisterFactory(typeof(IWrongTypeModule), static () => new UnrelatedModule());

            Assert.Throws<FrameworkException>(() => Framework.GetModule<IWrongTypeModule>());
        }

        [Test]
        public void GetModule_TwoInterfacesSameImpl_ShareSingleInstance()
        {
            Framework.RegisterFactory(typeof(IAlphaModule), static () => new AlphaModule());
            Framework.RegisterFactory(typeof(IBetaModule), static () => new AlphaModule());

            IAlphaModule asAlpha = Framework.GetModule<IAlphaModule>();
            IBetaModule asBeta = Framework.GetModule<IBetaModule>();

            // 按具体类型去重：第二个接口创建出的实例应被丢弃，复用已注册的同类型实例。
            Assert.AreSame((object)asAlpha, (object)asBeta);
        }

        [Test]
        public void GetModule_CircularFactoryDependency_ThrowsDiagnosticFrameworkException()
        {
            int factoryCalls = 0;
            Framework.RegisterFactory(typeof(ICycleModuleA), () =>
            {
                if (++factoryCalls > 8) throw new InvalidOperationException("test recursion guard");
                Framework.GetModule<ICycleModuleB>();
                return new CycleModuleA();
            });
            Framework.RegisterFactory(typeof(ICycleModuleB), () =>
            {
                if (++factoryCalls > 8) throw new InvalidOperationException("test recursion guard");
                Framework.GetModule<ICycleModuleA>();
                return new CycleModuleB();
            });

            FrameworkException error = Assert.Throws<FrameworkException>(
                () => Framework.GetModule<ICycleModuleA>());

            StringAssert.Contains(nameof(ICycleModuleA), error.Message);
            StringAssert.Contains(nameof(ICycleModuleB), error.Message);
            Assert.LessOrEqual(factoryCalls, 2,
                "循环应在第二个工厂请求 A 时立即被识别，不能继续递归。 ");
        }
    }
}
