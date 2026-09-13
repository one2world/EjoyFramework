//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.RemoteConfig;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// RemoteConfigManager 单测。
    /// 验证：本地默认值取值、缺失回退 def、Provider 覆盖本地默认值、解析失败回退 def、HasKey。
    /// </summary>
    public class RemoteConfigManagerTests
    {
        /// <summary>测试用 Provider：内存字典。</summary>
        private sealed class FakeProvider : IRemoteConfigProvider
        {
            private readonly Dictionary<string, string> m_Values;
            public FakeProvider(Dictionary<string, string> values) { m_Values = values; }
            public bool TryGet(string key, out string rawValue) { return m_Values.TryGetValue(key, out rawValue); }
        }

        [SetUp]
        public void Setup()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void LocalDefault_Get_ReturnsParsedValues()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("name", "hello");
            rc.SetLocalDefault("count", "42");
            rc.SetLocalDefault("enabled", "true");
            rc.SetLocalDefault("ratio", "3.5");

            Assert.AreEqual("hello", rc.GetString("name"));
            Assert.AreEqual(42, rc.GetInt("count"));
            Assert.IsTrue(rc.GetBool("enabled"));
            Assert.AreEqual(3.5f, rc.GetFloat("ratio"), 1e-6f);
        }

        [Test]
        public void Miss_ReturnsDefault()
        {
            var rc = new RemoteConfigManager();

            Assert.AreEqual("fallback", rc.GetString("absent", "fallback"));
            Assert.AreEqual(7, rc.GetInt("absent", 7));
            Assert.IsTrue(rc.GetBool("absent", true));
            Assert.AreEqual(1.25f, rc.GetFloat("absent", 1.25f), 1e-6f);
        }

        [Test]
        public void Provider_OverridesLocalDefault()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("flag", "false");
            rc.SetLocalDefault("max", "10");

            rc.SetProvider(new FakeProvider(new Dictionary<string, string>
            {
                { "flag", "true" },
                { "max", "99" },
            }));

            Assert.IsTrue(rc.GetBool("flag"), "Provider 命中应覆盖本地默认值");
            Assert.AreEqual(99, rc.GetInt("max"));
        }

        [Test]
        public void Provider_Miss_FallsBackToLocalDefault()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("only_local", "local_value");

            // Provider 不含该键 → 回退本地默认值。
            rc.SetProvider(new FakeProvider(new Dictionary<string, string>()));

            Assert.AreEqual("local_value", rc.GetString("only_local"));
        }

        [Test]
        public void ParseFailure_FallsBackToDefault()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("bad_int", "not_a_number");
            rc.SetLocalDefault("bad_bool", "maybe");
            rc.SetLocalDefault("bad_float", "xyz");

            Assert.AreEqual(-1, rc.GetInt("bad_int", -1));
            Assert.IsTrue(rc.GetBool("bad_bool", true));
            Assert.AreEqual(9.9f, rc.GetFloat("bad_float", 9.9f), 1e-6f);
        }

        [Test]
        public void GetBool_AcceptsNumericForm()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("one", "1");
            rc.SetLocalDefault("zero", "0");

            Assert.IsTrue(rc.GetBool("one"));
            Assert.IsFalse(rc.GetBool("zero"));
        }

        [Test]
        public void HasKey_ReflectsLocalAndProvider()
        {
            var rc = new RemoteConfigManager();
            rc.SetLocalDefault("local_key", "v");

            Assert.IsTrue(rc.HasKey("local_key"));
            Assert.IsFalse(rc.HasKey("missing"));

            rc.SetProvider(new FakeProvider(new Dictionary<string, string> { { "remote_key", "v" } }));
            Assert.IsTrue(rc.HasKey("remote_key"));
        }
    }
}
