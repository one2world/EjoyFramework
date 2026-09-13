//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Dialogue;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Dialogue
{
    /// <summary>
    /// 针对 <see cref="DialogueCondition"/> 的数值 6 运算符、布尔/字符串相等判定及缺失/类型不符降级的单元测试。
    /// </summary>
    [TestFixture]
    public class DialogueConditionTests
    {
        private static DialogueVariables WithInt(string key, int value)
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetInt(key, value);
            return vars;
        }

        private static DialogueVariables WithFloat(string key, float value)
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetFloat(key, value);
            return vars;
        }

        [Test]
        public void Int_AllSixOperators()
        {
            DialogueVariables vars = WithInt("hp", 5);

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.Equal, 5).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.Equal, 4).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.NotEqual, 4).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.NotEqual, 5).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.Less, 6).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.Less, 5).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.LessEqual, 5).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.LessEqual, 4).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.Greater, 4).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.Greater, 5).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Int("hp", CompareOp.GreaterEqual, 5).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Int("hp", CompareOp.GreaterEqual, 6).Evaluate(vars));
        }

        [Test]
        public void Float_AllSixOperators()
        {
            DialogueVariables vars = WithFloat("aff", 2.5f);

            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.Equal, 2.5f).Evaluate(vars));
            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.NotEqual, 2.4f).Evaluate(vars));
            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.Less, 2.6f).Evaluate(vars));
            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.LessEqual, 2.5f).Evaluate(vars));
            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.Greater, 2.4f).Evaluate(vars));
            Assert.IsTrue(DialogueCondition.Float("aff", CompareOp.GreaterEqual, 2.5f).Evaluate(vars));

            Assert.IsFalse(DialogueCondition.Float("aff", CompareOp.Greater, 2.5f).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Float("aff", CompareOp.Less, 2.5f).Evaluate(vars));
        }

        [Test]
        public void Bool_EqualAndNotEqual()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetBool("met", true);

            Assert.IsTrue(DialogueCondition.Bool("met", true).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("met", false).Evaluate(vars));

            Assert.IsTrue(DialogueCondition.Bool("met", false, CompareOp.NotEqual).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("met", true, CompareOp.NotEqual).Evaluate(vars));
        }

        [Test]
        public void Bool_FalseValue_Matches()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetBool("flag", false);

            Assert.IsTrue(DialogueCondition.Bool("flag", false).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("flag", true).Evaluate(vars));
        }

        [Test]
        public void String_EqualAndNotEqual()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetString("route", "haru");

            Assert.IsTrue(DialogueCondition.String("route", "haru").Evaluate(vars));
            Assert.IsFalse(DialogueCondition.String("route", "rin").Evaluate(vars));

            Assert.IsTrue(DialogueCondition.String("route", "rin", CompareOp.NotEqual).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.String("route", "haru", CompareOp.NotEqual).Evaluate(vars));
        }

        [Test]
        public void MissingVariable_EvaluatesFalse()
        {
            DialogueVariables vars = new DialogueVariables();

            Assert.IsFalse(DialogueCondition.Int("absent", CompareOp.Equal, 0).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("absent", false).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("absent", false, CompareOp.NotEqual).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.String("absent", null).Evaluate(vars));
        }

        [Test]
        public void WrongType_EvaluatesFalse()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetString("x", "text");

            // 用数值条件读字符串变量 => 类型不符 => false。
            Assert.IsFalse(DialogueCondition.Int("x", CompareOp.Equal, 0).Evaluate(vars));
            // 用布尔条件读字符串变量 => 类型不符 => false（含 NotEqual）。
            Assert.IsFalse(DialogueCondition.Bool("x", false).Evaluate(vars));
            Assert.IsFalse(DialogueCondition.Bool("x", true, CompareOp.NotEqual).Evaluate(vars));
        }

        [Test]
        public void Bool_NonEqualOperatorDowngradesToNotEqual()
        {
            DialogueVariables vars = new DialogueVariables();
            vars.SetBool("b", true);

            // Bool 上的 Less 等运算符按 NotEqual 处理：value=false 与 actual=true 不相等 => NotEqual 通过。
            Assert.IsTrue(DialogueCondition.Bool("b", false, CompareOp.Less).Evaluate(vars));
            // value=true 与 actual=true 相等 => 非 Equal 语义下不通过。
            Assert.IsFalse(DialogueCondition.Bool("b", true, CompareOp.Less).Evaluate(vars));
        }
    }
}
