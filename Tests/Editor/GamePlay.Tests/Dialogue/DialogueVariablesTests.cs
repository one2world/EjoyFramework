//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Dialogue;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Dialogue
{
    /// <summary>
    /// 针对 <see cref="DialogueVariables"/> 的类型化读写、回退、Has 与变更通知的单元测试。
    /// </summary>
    [TestFixture]
    public class DialogueVariablesTests
    {
        private const float Delta = 1e-5f;

        [Test]
        public void TypedGetSet_RoundTrips()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetBool("flag", true);
            vars.SetInt("count", 7);
            vars.SetFloat("ratio", 1.5f);
            vars.SetString("name", "Aria");

            Assert.IsTrue(vars.GetBool("flag"));
            Assert.AreEqual(7, vars.GetInt("count"));
            Assert.AreEqual(1.5f, vars.GetFloat("ratio"), Delta);
            Assert.AreEqual("Aria", vars.GetString("name"));
        }

        [Test]
        public void Get_MissingKey_ReturnsFallback()
        {
            DialogueVariables vars = new DialogueVariables();

            Assert.IsTrue(vars.GetBool("missing", true));
            Assert.AreEqual(99, vars.GetInt("missing", 99));
            Assert.AreEqual(3.14f, vars.GetFloat("missing", 3.14f), Delta);
            Assert.AreEqual("fallback", vars.GetString("missing", "fallback"));
        }

        [Test]
        public void Get_WrongType_ReturnsFallback()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetInt("x", 5);

            // 以错误类型读取时回退到 fallback。
            Assert.IsTrue(vars.GetBool("x", true));
            Assert.AreEqual("none", vars.GetString("x", "none"));
        }

        [Test]
        public void Has_ReflectsPresence()
        {
            DialogueVariables vars = new DialogueVariables();
            Assert.IsFalse(vars.Has("k"));

            vars.SetInt("k", 1);
            Assert.IsTrue(vars.Has("k"));
        }

        [Test]
        public void OnChanged_FiresOnRealChange()
        {
            DialogueVariables vars = new DialogueVariables();
            List<string> changed = new List<string>();
            vars.OnChanged += (store, key) => changed.Add(key);

            vars.SetInt("a", 1);
            vars.SetInt("a", 1); // 相同值，不应再次触发
            vars.SetInt("a", 2); // 变化，应触发

            Assert.AreEqual(2, changed.Count);
            Assert.AreEqual("a", changed[0]);
            Assert.AreEqual("a", changed[1]);
        }

        [Test]
        public void SetString_AllowsNull()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetString("s", null);

            Assert.IsTrue(vars.Has("s"));
            Assert.IsNull(vars.GetString("s", "fallback"));
        }

        [Test]
        public void TypeOverwrite_LastWriteWins()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetInt("v", 10);
            vars.SetString("v", "hello");

            Assert.AreEqual("hello", vars.GetString("v"));
            Assert.AreEqual(0, vars.GetInt("v", 0)); // 已不再是 Int 类型
        }
    }
}
