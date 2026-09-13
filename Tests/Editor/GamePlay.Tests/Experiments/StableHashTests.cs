//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Experiments;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Experiments
{
    /// <summary>
    /// 针对 <see cref="StableHash"/> 的单元测试：FNV-1a 已知向量、跨调用确定性，
    /// 以及 <see cref="StableHash.Normalized"/> 的 [0,1) 区间与稳定性。
    /// </summary>
    [TestFixture]
    public class StableHashTests
    {
        [Test]
        public void Fnv1a_EmptyString_ReturnsOffsetBasis()
        {
            // 空串仅返回偏移基 2166136261（0x811C9DC5）。
            Assert.AreEqual(2166136261u, StableHash.Fnv1a(string.Empty));
        }

        [Test]
        public void Fnv1a_KnownVector_A()
        {
            // 标准 FNV-1a 向量：Fnv1a("a") == 0xE40C292C == 3826002220。
            Assert.AreEqual(0xE40C292Cu, StableHash.Fnv1a("a"));
            Assert.AreEqual(3826002220u, StableHash.Fnv1a("a"));
        }

        [Test]
        public void Fnv1a_KnownVector_Foobar()
        {
            // 标准 FNV-1a 向量：Fnv1a("foobar") == 0xBF9CF968 == 3214735720。
            Assert.AreEqual(0xBF9CF968u, StableHash.Fnv1a("foobar"));
            Assert.AreEqual(3214735720u, StableHash.Fnv1a("foobar"));
        }

        [Test]
        public void Fnv1a_Null_TreatedAsEmpty()
        {
            Assert.AreEqual(2166136261u, StableHash.Fnv1a(null));
        }

        [Test]
        public void Fnv1a_Deterministic_SameInputSameHashEveryCall()
        {
            // 多次调用恒返回同一值（与进程随机化无关）。
            uint first = StableHash.Fnv1a("player-42:exp-color");
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(first, StableHash.Fnv1a("player-42:exp-color"));
            }
        }

        [Test]
        public void Fnv1a_DifferentInputs_DifferentHashesTypically()
        {
            // 不同输入应得到不同哈希（避免退化为常量）。
            Assert.AreNotEqual(StableHash.Fnv1a("a"), StableHash.Fnv1a("b"));
            Assert.AreNotEqual(StableHash.Fnv1a("ab"), StableHash.Fnv1a("ba"));
        }

        [Test]
        public void Fnv1a_Utf8Bytes_NonAsciiHashesConsistently()
        {
            // 非 ASCII（按 UTF-8 字节）也应确定且非平凡。
            uint a = StableHash.Fnv1a("玩家一号");
            uint b = StableHash.Fnv1a("玩家一号");
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(StableHash.Fnv1a("玩家一号"), StableHash.Fnv1a("玩家二号"));
        }

        [Test]
        public void Normalized_InRangeZeroToOne()
        {
            // 抽样大量输入，验证全部落 [0,1)。
            for (int i = 0; i < 5000; i++)
            {
                double p = StableHash.Normalized("user-" + i, "exp-bucket");
                Assert.GreaterOrEqual(p, 0.0);
                Assert.Less(p, 1.0);
            }
        }

        [Test]
        public void Normalized_Deterministic_SamePairSameValue()
        {
            double first = StableHash.Normalized("uid-7", "exp-x");
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(first, StableHash.Normalized("uid-7", "exp-x"));
            }
        }

        [Test]
        public void Normalized_MatchesFnv1aOfCombinedKey()
        {
            // Normalized(a,b) 应等于 Fnv1a(a + ":" + b) / 2^32。
            uint h = StableHash.Fnv1a("alpha:beta");
            double expected = h / 4294967296.0;
            Assert.AreEqual(expected, StableHash.Normalized("alpha", "beta"), 1e-12);
        }

        [Test]
        public void Normalized_NullArgs_TreatedAsEmpty()
        {
            // null 按空串处理，等价于 Normalized("", "")。
            Assert.AreEqual(StableHash.Normalized(string.Empty, string.Empty), StableHash.Normalized(null, null));
        }
    }
}
