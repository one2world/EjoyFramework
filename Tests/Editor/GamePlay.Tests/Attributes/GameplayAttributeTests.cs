//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Attributes;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Attributes
{
    /// <summary>
    /// 针对 <see cref="GameplayAttribute"/> 聚合公式与事件行为的单元测试。
    /// </summary>
    [TestFixture]
    public class GameplayAttributeTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void BaseValueOnly_CurrentValueEqualsBase()
        {
            GameplayAttribute attr = new GameplayAttribute("hp", 100f);
            Assert.AreEqual(100f, attr.CurrentValue, Delta);
        }

        [Test]
        public void SingleFlat_AddsToBase()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 10f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 5f));
            Assert.AreEqual(15f, attr.CurrentValue, Delta);
        }

        [Test]
        public void MultipleFlat_SumsAllFlat()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 10f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 5f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 3f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 2f));
            Assert.AreEqual(20f, attr.CurrentValue, Delta);
        }

        [Test]
        public void PercentAdd_StacksAdditively_TwoTenPercentEqualsTimes120()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentAdd, 0.10f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentAdd, 0.10f));
            // 100 * (1 + 0.20) = 120
            Assert.AreEqual(120f, attr.CurrentValue, Delta);
        }

        [Test]
        public void PercentMult_StacksMultiplicatively_TwoTenPercentEqualsTimes121()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentMult, 0.10f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentMult, 0.10f));
            // 100 * 1.1 * 1.1 = 121
            Assert.AreEqual(121f, attr.CurrentValue, Delta);
        }

        [Test]
        public void Mixed_FlatPercentAddPercentMult_AppliesInDocumentedOrder()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 50f));        // base+flat = 150
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentAdd, 0.20f)); // *1.20 = 180
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentMult, 0.50f)); // *1.50 = 270
            // (100 + 50) * (1 + 0.20) * (1 + 0.50) = 270
            Assert.AreEqual(270f, attr.CurrentValue, Delta);
        }

        [Test]
        public void Override_WinsOverEverythingElse()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 50f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentMult, 1.00f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 7f));
            Assert.AreEqual(7f, attr.CurrentValue, Delta);
        }

        [Test]
        public void Override_HighestPriorityWins()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 10f, priority: 1));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 20f, priority: 5));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 30f, priority: 3));
            Assert.AreEqual(20f, attr.CurrentValue, Delta);
        }

        [Test]
        public void Override_PriorityTie_LastAddedWins()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 10f, priority: 2));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Override, 99f, priority: 2));
            Assert.AreEqual(99f, attr.CurrentValue, Delta);
        }

        [Test]
        public void RemoveModifier_RemovesMatchingAndRecomputes()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 10f);
            AttributeModifier flat = new AttributeModifier("atk", ModifierOp.Flat, 5f);
            attr.AddModifier(flat);
            Assert.AreEqual(15f, attr.CurrentValue, Delta);

            bool removed = attr.RemoveModifier(flat);
            Assert.IsTrue(removed);
            Assert.AreEqual(10f, attr.CurrentValue, Delta);
            Assert.AreEqual(0, attr.Modifiers.Count);
        }

        [Test]
        public void RemoveModifier_NotFound_ReturnsFalse()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 10f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 5f));
            bool removed = attr.RemoveModifier(new AttributeModifier("atk", ModifierOp.Flat, 999f));
            Assert.IsFalse(removed);
            Assert.AreEqual(15f, attr.CurrentValue, Delta);
        }

        [Test]
        public void RemoveModifiersFromSource_RemovesOnlyThatSource()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            object buffA = new object();
            object buffB = new object();

            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 10f, buffA));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 20f, buffB));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 30f, buffA));
            // base 100 + 10 + 20 + 30 = 160
            Assert.AreEqual(160f, attr.CurrentValue, Delta);

            int removed = attr.RemoveModifiersFromSource(buffA);
            Assert.AreEqual(2, removed);
            // remaining: base 100 + 20 (buffB) = 120
            Assert.AreEqual(120f, attr.CurrentValue, Delta);
            Assert.AreEqual(1, attr.Modifiers.Count);
        }

        [Test]
        public void ClearModifiers_ResetsToBase()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 10f));
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentMult, 0.5f));
            attr.ClearModifiers();
            Assert.AreEqual(100f, attr.CurrentValue, Delta);
            Assert.AreEqual(0, attr.Modifiers.Count);
        }

        [Test]
        public void OnValueChanged_FiresWithCorrectOldAndNew()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            float capturedOld = float.NaN;
            float capturedNew = float.NaN;
            int callCount = 0;

            attr.OnValueChanged += (a, oldV, newV) =>
            {
                capturedOld = oldV;
                capturedNew = newV;
                callCount++;
            };

            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 50f));

            Assert.AreEqual(1, callCount);
            Assert.AreEqual(100f, capturedOld, Delta);
            Assert.AreEqual(150f, capturedNew, Delta);
        }

        [Test]
        public void OnValueChanged_DoesNotFire_WhenValueUnchanged()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            int callCount = 0;
            attr.OnValueChanged += (a, oldV, newV) => callCount++;

            // 加入一个等价于无变化的修饰器：base 100 -> still 100
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 0f));
            Assert.AreEqual(0, callCount, "Flat 0 不改变最终值，不应触发事件。");

            // PercentAdd 0 同样不改变最终值
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentAdd, 0f));
            Assert.AreEqual(0, callCount, "PercentAdd 0 不改变最终值，不应触发事件。");
        }

        [Test]
        public void BaseValueSetter_RecomputesAndFires()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            attr.AddModifier(new AttributeModifier("atk", ModifierOp.PercentAdd, 1.0f)); // *2 = 200

            float capturedNew = float.NaN;
            attr.OnValueChanged += (a, oldV, newV) => capturedNew = newV;

            attr.BaseValue = 50f;
            // (50) * (1 + 1.0) = 100
            Assert.AreEqual(100f, attr.CurrentValue, Delta);
            Assert.AreEqual(100f, capturedNew, Delta);
        }

        [Test]
        public void BaseValueSetter_SameValue_DoesNotFire()
        {
            GameplayAttribute attr = new GameplayAttribute("atk", 100f);
            int callCount = 0;
            attr.OnValueChanged += (a, oldV, newV) => callCount++;

            attr.BaseValue = 100f;
            Assert.AreEqual(0, callCount);
        }
    }
}
