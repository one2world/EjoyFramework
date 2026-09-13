//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.GamePlay.Tweening;

namespace EjoyFramework.GamePlay.Tests.Tweening
{
    public class ValueTweenTests
    {
        private const float Delta = 1e-3f;

        // 用一个简单的二维向量替身验证注入式 lerp（不依赖 UnityEngine）。
        private struct Vec2
        {
            public float X;
            public float Y;

            public Vec2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
            {
                return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }

        [Test]
        public void InjectedLerp_InterpolatesBothComponents()
        {
            Vec2 captured = default;
            var tween = new ValueTween<Vec2>(
                new Vec2(0f, 0f), new Vec2(10f, 20f), 1f,
                Vec2.Lerp,
                v => captured = v);

            tween.Tick(0.5f);
            Assert.AreEqual(5f, captured.X, Delta);
            Assert.AreEqual(10f, captured.Y, Delta);
            Assert.AreEqual(5f, tween.CurrentValue.X, Delta);
            Assert.AreEqual(10f, tween.CurrentValue.Y, Delta);
        }

        [Test]
        public void ReachesEndValue_OnComplete()
        {
            var tween = new ValueTween<Vec2>(
                new Vec2(1f, 2f), new Vec2(3f, 4f), 1f,
                Vec2.Lerp,
                v => { });

            tween.Tick(1f);
            Assert.IsTrue(tween.IsComplete);
            Assert.AreEqual(3f, tween.CurrentValue.X, Delta);
            Assert.AreEqual(4f, tween.CurrentValue.Y, Delta);
        }

        [Test]
        public void Yoyo_ReversesInjectedLerp()
        {
            var tween = new ValueTween<Vec2>(
                new Vec2(0f, 0f), new Vec2(10f, 10f), 1f,
                Vec2.Lerp,
                v => { });
            tween.SetLoops(2, LoopType.Yoyo);

            tween.Tick(1f); // 到终点
            Assert.AreEqual(10f, tween.CurrentValue.X, Delta);

            tween.Tick(0.5f); // 反向一半
            Assert.AreEqual(5f, tween.CurrentValue.X, Delta);

            tween.Tick(0.5f); // 回到起点
            Assert.AreEqual(0f, tween.CurrentValue.X, Delta);
        }

        [Test]
        public void Constructor_NullLerp_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new ValueTween<float>(0f, 1f, 1f, null, v => { }));
        }

        [Test]
        public void Constructor_NullSetter_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new ValueTween<float>(0f, 1f, 1f, (a, b, t) => a, null));
        }
    }
}
