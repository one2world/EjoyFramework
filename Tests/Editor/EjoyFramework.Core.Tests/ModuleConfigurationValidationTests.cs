//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// FrameworkModule 配置契约回归测试（ADR 0002：把"Helper 注入是约定而非契约"变为可声明的契约）。
    ///
    /// Covers both the declaration contract and the explicit startup validation behavior.
    /// </summary>
    public class ModuleConfigurationValidationTests
    {
        // 声明需要配置、且由可切换的内部状态决定是否已配置的假模块。
        private sealed class FakeConfigurableModule : FrameworkModule
        {
            private bool m_Configured;

            public override int Priority => 0;
            public override bool RequiresConfiguration => true;
            public override bool IsModuleConfigured => m_Configured;
            public override string ConfigurationHint => "Call Configure() before use.";

            public void Configure() { m_Configured = true; }

            public override void Update(float a, float b) { }
            public override void Shutdown() { }
        }

        // 不覆写契约的默认模块：应视为"无需配置且已配置"。
        private sealed class FakeDefaultModule : FrameworkModule
        {
            public override int Priority => 0;
            public override void Update(float a, float b) { }
            public override void Shutdown() { }
        }

        [SetUp]
        public void SetUp()
        {
            Framework.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            Framework.Shutdown();
        }

        [Test]
        public void DefaultModule_DoesNotRequireConfiguration_AndReportsConfigured()
        {
            var module = new FakeDefaultModule();

            Assert.IsFalse(module.RequiresConfiguration);
            Assert.IsTrue(module.IsModuleConfigured);
            Assert.AreEqual(string.Empty, module.ConfigurationHint);
        }

        [Test]
        public void RequiredModule_Unconfigured_ReportsNotConfigured()
        {
            var module = new FakeConfigurableModule();

            Assert.IsTrue(module.RequiresConfiguration);
            Assert.IsFalse(module.IsModuleConfigured);
            Assert.IsNotEmpty(module.ConfigurationHint);
        }

        [Test]
        public void RequiredModule_AfterConfigure_ReportsConfigured()
        {
            var module = new FakeConfigurableModule();
            module.Configure();

            Assert.IsTrue(module.RequiresConfiguration);
            Assert.IsTrue(module.IsModuleConfigured);
        }

        [Test]
        public void ValidateModuleConfigurations_UnconfiguredModule_Throws()
        {
            var module = new FakeConfigurableModule();
            Framework.RegisterModule(module);

            FrameworkException exception = Assert.Throws<FrameworkException>(
                () => Framework.ValidateModuleConfigurations());

            StringAssert.Contains(typeof(FakeConfigurableModule).FullName, exception.Message);
            StringAssert.Contains(module.ConfigurationHint, exception.Message);
        }

        [Test]
        public void ValidateModuleConfigurations_ConfiguredModule_DoesNotThrow()
        {
            var module = new FakeConfigurableModule();
            module.Configure();
            Framework.RegisterModule(module);

            Assert.DoesNotThrow(() => Framework.ValidateModuleConfigurations());
        }
    }
}
