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
    /// 针对 <see cref="ExperimentManager"/> 的单元测试：注册/重复/查询、稳定加权分桶、
    /// 禁用与缺失返回 null、大样本分布逼近权重占比、QA 强制覆盖与撤销、类型化载荷读取、变体归属判定。
    /// </summary>
    [TestFixture]
    public class ExperimentManagerTests
    {
        private static Experiment TwoWayEven(string id = "exp-even", bool enabled = true)
        {
            return new Experiment(id, new[]
            {
                new Variant("control", 1, "control-payload"),
                new Variant("treatment", 1, "treatment-payload")
            }, enabled);
        }

        [Test]
        public void Define_ThenHasAndGet()
        {
            ExperimentManager mgr = new ExperimentManager();
            Experiment exp = TwoWayEven();
            mgr.Define(exp);

            Assert.IsTrue(mgr.Has("exp-even"));
            Assert.AreSame(exp, mgr.Get("exp-even"));
            Assert.AreEqual(1, mgr.Count);
        }

        [Test]
        public void Define_Null_Throws()
        {
            ExperimentManager mgr = new ExperimentManager();
            Assert.Throws<ArgumentNullException>(() => mgr.Define(null));
        }

        [Test]
        public void Define_DuplicateId_Throws()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven("dup"));
            Assert.Throws<ArgumentException>(() => mgr.Define(TwoWayEven("dup")));
        }

        [Test]
        public void Has_And_Get_Missing()
        {
            ExperimentManager mgr = new ExperimentManager();
            Assert.IsFalse(mgr.Has("nope"));
            Assert.IsFalse(mgr.Has(null));
            Assert.IsNull(mgr.Get("nope"));
            Assert.IsNull(mgr.Get(null));
        }

        [Test]
        public void GetVariant_MissingExperiment_ReturnsNull()
        {
            ExperimentManager mgr = new ExperimentManager();
            Assert.IsNull(mgr.GetVariant("missing", "user-1"));
            Assert.IsNull(mgr.GetVariantId("missing", "user-1"));
        }

        [Test]
        public void GetVariant_DisabledExperiment_ReturnsNull()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven("off", enabled: false));

            Assert.IsNull(mgr.GetVariant("off", "user-1"));
            Assert.IsNull(mgr.GetVariantId("off", "user-1"));
        }

        [Test]
        public void GetVariant_Stable_SameUserExperimentAlwaysSameVariant()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            string first = mgr.GetVariantId("exp-even", "stable-user");
            Assert.IsNotNull(first);

            // 同一 (用户, 实验) 多次调用恒返回同一变体。
            for (int i = 0; i < 2000; i++)
            {
                Assert.AreEqual(first, mgr.GetVariantId("exp-even", "stable-user"));
            }
        }

        [Test]
        public void GetVariant_Stable_AcrossFreshManagers()
        {
            // 稳定性不依赖管理器实例：新建管理器、相同定义，分配结果一致（模拟跨运行/跨设备）。
            string[] users = { "u1", "u2", "u3", "alpha", "玩家999" };
            foreach (string u in users)
            {
                ExperimentManager m1 = new ExperimentManager();
                m1.Define(TwoWayEven());
                ExperimentManager m2 = new ExperimentManager();
                m2.Define(TwoWayEven());

                Assert.AreEqual(m1.GetVariantId("exp-even", u), m2.GetVariantId("exp-even", u));
            }
        }

        [Test]
        public void Distribution_TwoWayEven_RoughlyFiftyFifty()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            const int n = 10000;
            int control = 0;
            int treatment = 0;
            for (int i = 0; i < n; i++)
            {
                string id = mgr.GetVariantId("exp-even", "synthetic-user-" + i);
                if (id == "control")
                {
                    control++;
                }
                else if (id == "treatment")
                {
                    treatment++;
                }
                else
                {
                    Assert.Fail("意外的变体 id：" + id);
                }
            }

            Assert.AreEqual(n, control + treatment);

            // 1:1 拆分应各约 50%，容差 ±3%。
            double controlFraction = control / (double)n;
            Assert.That(controlFraction, Is.EqualTo(0.5).Within(0.03),
                $"control 占比 {controlFraction:P2} 偏离 50% 超过容差。");
        }

        [Test]
        public void Distribution_ThreeToOne_RoughlySeventyFiveTwentyFive()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(new Experiment("exp-weighted", new[]
            {
                new Variant("big", 3),
                new Variant("small", 1)
            }));

            const int n = 10000;
            int big = 0;
            int small = 0;
            for (int i = 0; i < n; i++)
            {
                string id = mgr.GetVariantId("exp-weighted", "wuser-" + i);
                if (id == "big")
                {
                    big++;
                }
                else
                {
                    small++;
                }
            }

            double bigFraction = big / (double)n;
            double smallFraction = small / (double)n;
            Assert.That(bigFraction, Is.EqualTo(0.75).Within(0.03),
                $"big 占比 {bigFraction:P2} 偏离 75% 超过容差。");
            Assert.That(smallFraction, Is.EqualTo(0.25).Within(0.03),
                $"small 占比 {smallFraction:P2} 偏离 25% 超过容差。");
        }

        [Test]
        public void ForceVariant_Overrides_ThenClearForceRestores()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            string natural = mgr.GetVariantId("exp-even", "qa-user");
            string other = natural == "control" ? "treatment" : "control";

            // 强制到另一变体，覆盖哈希分桶。
            mgr.ForceVariant("exp-even", "qa-user", other);
            Assert.AreEqual(other, mgr.GetVariantId("exp-even", "qa-user"));
            Assert.AreEqual(other, mgr.GetVariant("exp-even", "qa-user").Id);

            // 撤销后回落自然分桶。
            Assert.IsTrue(mgr.ClearForce("exp-even", "qa-user"));
            Assert.AreEqual(natural, mgr.GetVariantId("exp-even", "qa-user"));

            // 重复撤销返回 false。
            Assert.IsFalse(mgr.ClearForce("exp-even", "qa-user"));
        }

        [Test]
        public void ForceVariant_DoesNotLeakToOtherUsers()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            string otherUserNatural = mgr.GetVariantId("exp-even", "other-user");
            mgr.ForceVariant("exp-even", "qa-user", "treatment");

            // 强制仅作用于 qa-user，不影响 other-user。
            Assert.AreEqual(otherUserNatural, mgr.GetVariantId("exp-even", "other-user"));
        }

        [Test]
        public void ForceVariant_DisabledExperiment_StillReturnsNull()
        {
            // 禁用优先级高于强制：禁用实验即便有强制覆盖也返回 null。
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven("off", enabled: false));
            mgr.ForceVariant("off", "qa-user", "treatment");

            Assert.IsNull(mgr.GetVariant("off", "qa-user"));
        }

        [Test]
        public void ForceVariant_UnknownVariantId_FallsBackToHash()
        {
            // 强制指向不存在的变体时自动失效，回落哈希分桶（不返回 null、不抛异常）。
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            string natural = mgr.GetVariantId("exp-even", "qa-user");
            mgr.ForceVariant("exp-even", "qa-user", "ghost-variant");

            Assert.AreEqual(natural, mgr.GetVariantId("exp-even", "qa-user"));
        }

        [Test]
        public void ForceVariant_InvalidArgs_Throws()
        {
            ExperimentManager mgr = new ExperimentManager();
            Assert.Throws<ArgumentNullException>(() => mgr.ForceVariant(null, "u", "v"));
            Assert.Throws<ArgumentException>(() => mgr.ForceVariant("exp", "u", null));
            Assert.Throws<ArgumentException>(() => mgr.ForceVariant("exp", "u", string.Empty));
        }

        [Test]
        public void ClearForce_MissingOrNull_ReturnsFalse()
        {
            ExperimentManager mgr = new ExperimentManager();
            Assert.IsFalse(mgr.ClearForce("exp", "u"));
            Assert.IsFalse(mgr.ClearForce(null, "u"));
        }

        [Test]
        public void IsInVariant_MatchesAssignment()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(TwoWayEven());

            string assigned = mgr.GetVariantId("exp-even", "ix-user");
            string other = assigned == "control" ? "treatment" : "control";

            Assert.IsTrue(mgr.IsInVariant("exp-even", "ix-user", assigned));
            Assert.IsFalse(mgr.IsInVariant("exp-even", "ix-user", other));
            // 缺失/禁用实验下任何变体判定均为 false。
            Assert.IsFalse(mgr.IsInVariant("missing", "ix-user", "control"));
        }

        [Test]
        public void GetPayload_TypedAndFallback()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(new Experiment("exp-payload", new[]
            {
                new Variant("a", 1, 42),
                new Variant("b", 1, 42)
            }));

            // 两变体载荷均为 int 42，故无论分到哪个，类型化读取都得 42。
            Assert.AreEqual(42, mgr.GetPayload("exp-payload", "puser", -1));

            // 类型不匹配回落兜底。
            Assert.AreEqual("fb", mgr.GetPayload("exp-payload", "puser", "fb"));

            // 缺失实验回落兜底。
            Assert.AreEqual(999, mgr.GetPayload("missing", "puser", 999));
        }

        [Test]
        public void GetPayload_NullPayload_ReturnsFallback()
        {
            ExperimentManager mgr = new ExperimentManager();
            mgr.Define(new Experiment("exp-nullpay", new[]
            {
                new Variant("a", 1),
                new Variant("b", 1)
            }));

            // 载荷为 null（值类型 T），应回落兜底而非 null。
            Assert.AreEqual(7, mgr.GetPayload("exp-nullpay", "u", 7));
        }

        [Test]
        public void GetVariant_ReturnedVariantBelongsToExperiment()
        {
            ExperimentManager mgr = new ExperimentManager();
            Experiment exp = TwoWayEven();
            mgr.Define(exp);

            Variant v = mgr.GetVariant("exp-even", "belong-user");
            Assert.IsNotNull(v);
            Assert.Contains(v, new List<Variant>(exp.Variants));
        }
    }
}
