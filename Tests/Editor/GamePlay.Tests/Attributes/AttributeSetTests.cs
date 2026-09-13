//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.GamePlay.Attributes;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Attributes
{
    /// <summary>
    /// 针对 <see cref="AttributeSet"/> 容器行为、路由与事件转发的单元测试。
    /// </summary>
    [TestFixture]
    public class AttributeSetTests
    {
        private const float Delta = 1e-4f;

        [Test]
        public void Add_CreatesAttributeWithBaseValue()
        {
            AttributeSet set = new AttributeSet();
            GameplayAttribute attr = set.Add("hp", 100f);

            Assert.IsNotNull(attr);
            Assert.AreEqual("hp", attr.Id);
            Assert.AreEqual(100f, attr.CurrentValue, Delta);
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Has("hp"));
        }

        [Test]
        public void Add_DuplicateId_Throws()
        {
            AttributeSet set = new AttributeSet();
            set.Add("hp", 100f);
            Assert.Throws<System.ArgumentException>(() => set.Add("hp", 50f));
        }

        [Test]
        public void Get_Missing_ReturnsNull()
        {
            AttributeSet set = new AttributeSet();
            Assert.IsNull(set.Get("missing"));
        }

        [Test]
        public void Get_Existing_ReturnsAttribute()
        {
            AttributeSet set = new AttributeSet();
            GameplayAttribute added = set.Add("hp", 100f);
            Assert.AreSame(added, set.Get("hp"));
        }

        [Test]
        public void TryGet_ReportsPresenceCorrectly()
        {
            AttributeSet set = new AttributeSet();
            set.Add("hp", 100f);

            Assert.IsTrue(set.TryGet("hp", out GameplayAttribute hp));
            Assert.IsNotNull(hp);

            Assert.IsFalse(set.TryGet("nope", out GameplayAttribute nope));
            Assert.IsNull(nope);
        }

        [Test]
        public void Has_ReturnsFalseForMissing()
        {
            AttributeSet set = new AttributeSet();
            set.Add("hp", 100f);
            Assert.IsTrue(set.Has("hp"));
            Assert.IsFalse(set.Has("mp"));
        }

        [Test]
        public void GetValue_ReturnsCurrentValue()
        {
            AttributeSet set = new AttributeSet();
            set.Add("atk", 10f);
            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 5f));
            Assert.AreEqual(15f, set.GetValue("atk"), Delta);
        }

        [Test]
        public void GetValue_Missing_ReturnsFallback()
        {
            AttributeSet set = new AttributeSet();
            Assert.AreEqual(0f, set.GetValue("missing"), Delta);
            Assert.AreEqual(-1f, set.GetValue("missing", -1f), Delta);
        }

        [Test]
        public void AddModifier_RoutesToNamedAttribute()
        {
            AttributeSet set = new AttributeSet();
            set.Add("atk", 100f);
            set.Add("def", 50f);

            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 25f));

            Assert.AreEqual(125f, set.GetValue("atk"), Delta);
            Assert.AreEqual(50f, set.GetValue("def"), Delta);
        }

        [Test]
        public void AddModifier_MissingAttribute_IsNoOp()
        {
            AttributeSet set = new AttributeSet();
            set.Add("atk", 100f);

            // 目标属性不存在：静默忽略，不抛异常、不影响其它属性。
            Assert.DoesNotThrow(() => set.AddModifier(new AttributeModifier("ghost", ModifierOp.Flat, 999f)));
            Assert.AreEqual(100f, set.GetValue("atk"), Delta);
        }

        [Test]
        public void RemoveModifiersFromSource_RemovesAcrossAllAttributes()
        {
            AttributeSet set = new AttributeSet();
            set.Add("atk", 100f);
            set.Add("def", 100f);
            object buff = new object();
            object other = new object();

            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 10f, buff));
            set.AddModifier(new AttributeModifier("def", ModifierOp.Flat, 20f, buff));
            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 5f, other));

            Assert.AreEqual(115f, set.GetValue("atk"), Delta);
            Assert.AreEqual(120f, set.GetValue("def"), Delta);

            int removed = set.RemoveModifiersFromSource(buff);
            Assert.AreEqual(2, removed);
            Assert.AreEqual(105f, set.GetValue("atk"), Delta); // only 'other' remains
            Assert.AreEqual(100f, set.GetValue("def"), Delta);
        }

        [Test]
        public void OnAttributeChanged_FiresWithRoutingInfo()
        {
            AttributeSet set = new AttributeSet();
            GameplayAttribute atk = set.Add("atk", 100f);

            GameplayAttribute capturedAttr = null;
            float capturedOld = float.NaN;
            float capturedNew = float.NaN;
            int callCount = 0;

            set.OnAttributeChanged += (s, a, oldV, newV) =>
            {
                Assert.AreSame(set, s);
                capturedAttr = a;
                capturedOld = oldV;
                capturedNew = newV;
                callCount++;
            };

            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 50f));

            Assert.AreEqual(1, callCount);
            Assert.AreSame(atk, capturedAttr);
            Assert.AreEqual(100f, capturedOld, Delta);
            Assert.AreEqual(150f, capturedNew, Delta);
        }

        [Test]
        public void OnAttributeChanged_DoesNotFire_WhenValueUnchanged()
        {
            AttributeSet set = new AttributeSet();
            set.Add("atk", 100f);
            int callCount = 0;
            set.OnAttributeChanged += (s, a, oldV, newV) => callCount++;

            set.AddModifier(new AttributeModifier("atk", ModifierOp.Flat, 0f));
            Assert.AreEqual(0, callCount);
        }

        [Test]
        public void Attributes_EnumeratesAll()
        {
            AttributeSet set = new AttributeSet();
            set.Add("hp", 100f);
            set.Add("mp", 50f);
            set.Add("atk", 10f);

            HashSet<string> ids = new HashSet<string>();
            foreach (GameplayAttribute a in set.Attributes)
            {
                ids.Add(a.Id);
            }

            Assert.AreEqual(3, set.Count);
            Assert.IsTrue(ids.Contains("hp"));
            Assert.IsTrue(ids.Contains("mp"));
            Assert.IsTrue(ids.Contains("atk"));
        }
    }
}
