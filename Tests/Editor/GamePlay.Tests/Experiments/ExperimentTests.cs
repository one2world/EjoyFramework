//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Experiments;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Experiments
{
    /// <summary>
    /// 针对 <see cref="Variant"/> 与 <see cref="Experiment"/> 不可变定义的构造与校验测试。
    /// </summary>
    [TestFixture]
    public class ExperimentTests
    {
        [Test]
        public void Variant_Constructor_ExposesProperties()
        {
            object payload = new { Multiplier = 1.5f };
            Variant v = new Variant("treatment", 3, payload);

            Assert.AreEqual("treatment", v.Id);
            Assert.AreEqual(3, v.Weight);
            Assert.AreSame(payload, v.Payload);
        }

        [Test]
        public void Variant_NullPayload_Allowed()
        {
            Variant v = new Variant("control", 1);
            Assert.IsNull(v.Payload);
        }

        [Test]
        public void Variant_EmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Variant(string.Empty, 1));
            Assert.Throws<ArgumentException>(() => new Variant(null, 1));
        }

        [Test]
        public void Variant_NonPositiveWeight_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Variant("v", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Variant("v", -5));
        }

        [Test]
        public void Experiment_Constructor_ExposesProperties()
        {
            Variant a = new Variant("a", 1);
            Variant b = new Variant("b", 1);
            Experiment exp = new Experiment("exp", new[] { a, b });

            Assert.AreEqual("exp", exp.Id);
            Assert.IsTrue(exp.Enabled);
            Assert.AreEqual(2, exp.Variants.Count);
            Assert.AreSame(a, exp.Variants[0]);
            Assert.AreSame(b, exp.Variants[1]);
        }

        [Test]
        public void Experiment_DisabledFlag_Honored()
        {
            Experiment exp = new Experiment("exp", new[] { new Variant("a", 1) }, enabled: false);
            Assert.IsFalse(exp.Enabled);
        }

        [Test]
        public void Experiment_EmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Experiment(string.Empty, new[] { new Variant("a", 1) }));
        }

        [Test]
        public void Experiment_NullVariants_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Experiment("exp", null));
        }

        [Test]
        public void Experiment_EmptyVariants_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Experiment("exp", new Variant[0]));
        }

        [Test]
        public void Experiment_DuplicateVariantIds_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new Experiment("exp", new[] { new Variant("a", 1), new Variant("a", 2) }));
        }

        [Test]
        public void Experiment_NullVariantElement_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                new Experiment("exp", new Variant[] { new Variant("a", 1), null }));
        }

        [Test]
        public void Experiment_VariantsSnapshot_IsImmutableToCallerMutation()
        {
            // 传入可变列表后再修改，不应影响已构造实验的变体快照。
            List<Variant> source = new List<Variant> { new Variant("a", 1) };
            Experiment exp = new Experiment("exp", source);
            source.Add(new Variant("b", 1));

            Assert.AreEqual(1, exp.Variants.Count);
        }
    }
}
