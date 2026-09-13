//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Dialogue;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Dialogue
{
    /// <summary>
    /// 针对 <see cref="DialogueEffect"/> 的 Set/Add（int、float）、SetBool、SetString 应用语义的单元测试。
    /// </summary>
    [TestFixture]
    public class DialogueEffectTests
    {
        private const float Delta = 1e-5f;

        [Test]
        public void SetInt_WritesValue()
        {
            DialogueVariables vars = new DialogueVariables();
            DialogueEffect.SetInt("gold", 100).Apply(vars);

            Assert.AreEqual(100, vars.GetInt("gold"));
        }

        [Test]
        public void AddInt_AccumulatesFromExisting()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetInt("gold", 100);
            DialogueEffect.AddInt("gold", 50).Apply(vars);

            Assert.AreEqual(150, vars.GetInt("gold"));
        }

        [Test]
        public void AddInt_OnMissingKey_TreatsBaseAsZero()
        {
            DialogueVariables vars = new DialogueVariables();
            DialogueEffect.AddInt("score", 7).Apply(vars);

            Assert.AreEqual(7, vars.GetInt("score"));
        }

        [Test]
        public void SetFloat_WritesValue()
        {
            DialogueVariables vars = new DialogueVariables();
            DialogueEffect.SetFloat("aff", 1.25f).Apply(vars);

            Assert.AreEqual(1.25f, vars.GetFloat("aff"), Delta);
        }

        [Test]
        public void AddFloat_Accumulates()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetFloat("aff", 1.0f);
            DialogueEffect.AddFloat("aff", 0.5f).Apply(vars);

            Assert.AreEqual(1.5f, vars.GetFloat("aff"), Delta);
        }

        [Test]
        public void SetBool_WritesValue()
        {
            DialogueVariables vars = new DialogueVariables();
            DialogueEffect.SetBool("met", true).Apply(vars);

            Assert.IsTrue(vars.GetBool("met"));
        }

        [Test]
        public void SetString_WritesValue()
        {
            DialogueVariables vars = new DialogueVariables();
            DialogueEffect.SetString("route", "haru").Apply(vars);

            Assert.AreEqual("haru", vars.GetString("route"));
        }
    }
}
