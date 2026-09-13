//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.GamePlay.Tweening;

namespace EjoyFramework.GamePlay.Tests.Tweening
{
    public class EasingTests
    {
        private const float Delta = 1e-3f;

        // OutBack / OutElastic 在过程中过冲；它们在端点处仍应落在 0/1。
        private static readonly Ease[] AllEases =
        {
            Ease.Linear,
            Ease.InQuad, Ease.OutQuad, Ease.InOutQuad,
            Ease.InCubic, Ease.OutCubic, Ease.InOutCubic,
            Ease.InSine, Ease.OutSine, Ease.InOutSine,
            Ease.InExpo, Ease.OutExpo, Ease.InOutExpo,
            Ease.OutBack, Ease.OutElastic, Ease.OutBounce
        };

        [Test]
        public void Linear_Endpoints_And_Midpoint()
        {
            Assert.AreEqual(0f, Easing.Evaluate(Ease.Linear, 0f), Delta);
            Assert.AreEqual(1f, Easing.Evaluate(Ease.Linear, 1f), Delta);
            Assert.AreEqual(0.5f, Easing.Evaluate(Ease.Linear, 0.5f), Delta);
        }

        [Test]
        public void Quad_Endpoints()
        {
            Assert.AreEqual(0f, Easing.Evaluate(Ease.InQuad, 0f), Delta);
            Assert.AreEqual(1f, Easing.Evaluate(Ease.InQuad, 1f), Delta);
            Assert.AreEqual(0f, Easing.Evaluate(Ease.OutQuad, 0f), Delta);
            Assert.AreEqual(1f, Easing.Evaluate(Ease.OutQuad, 1f), Delta);
        }

        [Test]
        public void AllEases_LandAtZero_AtStart()
        {
            foreach (Ease ease in AllEases)
            {
                Assert.AreEqual(0f, Easing.Evaluate(ease, 0f), Delta,
                    "缓动 " + ease + " 在 t=0 应为 0");
            }
        }

        [Test]
        public void AllEases_LandAtOne_AtEnd()
        {
            foreach (Ease ease in AllEases)
            {
                Assert.AreEqual(1f, Easing.Evaluate(ease, 1f), Delta,
                    "缓动 " + ease + " 在 t=1 应为 1");
            }
        }

        [Test]
        public void Linear_IsStrictlyMonotonic()
        {
            float prev = float.NegativeInfinity;
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float v = Easing.Evaluate(Ease.Linear, t);
                Assert.GreaterOrEqual(v, prev, "Linear 应单调不减");
                prev = v;
            }
        }

        [Test]
        public void OutQuad_IsMonotonic()
        {
            float prev = float.NegativeInfinity;
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float v = Easing.Evaluate(Ease.OutQuad, t);
                Assert.GreaterOrEqual(v, prev, "OutQuad 应单调不减");
                prev = v;
            }
        }

        [Test]
        public void InCubic_IsMonotonic()
        {
            float prev = float.NegativeInfinity;
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float v = Easing.Evaluate(Ease.InCubic, t);
                Assert.GreaterOrEqual(v, prev, "InCubic 应单调不减");
                prev = v;
            }
        }

        [Test]
        public void OutBack_Overshoots_NearEnd()
        {
            // OutBack 在接近终点前会越过 1（过冲）。
            bool overshot = false;
            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                if (Easing.Evaluate(Ease.OutBack, t) > 1f + Delta)
                {
                    overshot = true;
                    break;
                }
            }
            Assert.IsTrue(overshot, "OutBack 应在过程中过冲越过 1");
        }
    }
}
