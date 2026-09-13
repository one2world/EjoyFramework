//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Loot;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Loot
{
    /// <summary>
    /// 针对 <see cref="SystemRandomSource"/> 的确定性与边界行为的单元测试。
    /// </summary>
    [TestFixture]
    public class SystemRandomSourceTests
    {
        [Test]
        public void SeededSources_WithSameSeed_ProduceIdenticalSequences()
        {
            SystemRandomSource a = new SystemRandomSource(12345);
            SystemRandomSource b = new SystemRandomSource(12345);

            for (int i = 0; i < 32; i++)
            {
                Assert.AreEqual(a.NextDouble(), b.NextDouble(), 0d, "第 " + i + " 个 NextDouble 不一致。");
                Assert.AreEqual(a.NextInt(1000), b.NextInt(1000), "第 " + i + " 个 NextInt 不一致。");
            }
        }

        [Test]
        public void SeededSources_WithDifferentSeed_DivergeAtSomePoint()
        {
            SystemRandomSource a = new SystemRandomSource(1);
            SystemRandomSource b = new SystemRandomSource(2);

            bool diverged = false;
            for (int i = 0; i < 32; i++)
            {
                if (a.NextDouble() != b.NextDouble())
                {
                    diverged = true;
                    break;
                }
            }

            Assert.IsTrue(diverged, "不同种子的随机序列应当在若干次取值内出现差异。");
        }

        [Test]
        public void NextDouble_StaysInUnitInterval()
        {
            SystemRandomSource rng = new SystemRandomSource(777);

            for (int i = 0; i < 100; i++)
            {
                double v = rng.NextDouble();
                Assert.GreaterOrEqual(v, 0d);
                Assert.Less(v, 1d);
            }
        }

        [Test]
        public void NextInt_StaysInRange()
        {
            SystemRandomSource rng = new SystemRandomSource(888);

            for (int i = 0; i < 100; i++)
            {
                int v = rng.NextInt(10);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 10);
            }
        }

        [Test]
        public void NextInt_NonPositiveBound_ReturnsZero()
        {
            SystemRandomSource rng = new SystemRandomSource(1);

            Assert.AreEqual(0, rng.NextInt(0));
            Assert.AreEqual(0, rng.NextInt(-5));
        }
    }
}
