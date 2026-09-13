//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.GamePlay.Abilities;
using NUnit.Framework;

namespace EjoyFramework.GamePlay.Tests.Abilities
{
    /// <summary>
    /// 针对 <see cref="GameplayTagSet"/> 层级标签匹配的单元测试。
    /// </summary>
    [TestFixture]
    public class GameplayTagSetTests
    {
        [Test]
        public void HasTag_ExactMatch()
        {
            GameplayTagSet tags = new GameplayTagSet();
            tags.Add("state.stunned");

            Assert.IsTrue(tags.HasTag("state.stunned"));
            Assert.IsFalse(tags.HasTag("state.rooted"));
        }

        [Test]
        public void HasTag_ParentPrefixMatch()
        {
            GameplayTagSet tags = new GameplayTagSet();
            tags.Add("state.stunned");

            // 查询父级 "state" 命中所持子级 "state.stunned"。
            Assert.IsTrue(tags.HasTag("state"));
            // "stat" 不是 "state" 的合法父前缀（缺少 '.' 边界）。
            Assert.IsFalse(tags.HasTag("stat"));
        }

        [Test]
        public void HasTag_DoesNotMatchSiblingPrefix()
        {
            GameplayTagSet tags = new GameplayTagSet();
            tags.Add("ability.fireball");

            Assert.IsFalse(tags.HasTag("ability.fire"));
            Assert.IsTrue(tags.HasTag("ability"));
        }

        [Test]
        public void Remove_RemovesTag()
        {
            GameplayTagSet tags = new GameplayTagSet();
            tags.Add("state.burning");

            Assert.IsTrue(tags.HasTag("state.burning"));
            Assert.IsTrue(tags.Remove("state.burning"));
            Assert.IsFalse(tags.HasTag("state.burning"));
            Assert.IsFalse(tags.Remove("state.burning"));
        }

        [Test]
        public void HasAny_And_HasAll()
        {
            GameplayTagSet tags = new GameplayTagSet();
            tags.Add("state.stunned");
            tags.Add("buff.haste");

            Assert.IsTrue(tags.HasAny(new[] { "state.stunned", "missing" }));
            Assert.IsFalse(tags.HasAny(new[] { "missing.a", "missing.b" }));

            Assert.IsTrue(tags.HasAll(new[] { "state.stunned", "buff.haste" }));
            Assert.IsFalse(tags.HasAll(new[] { "state.stunned", "missing" }));
        }

        [Test]
        public void HasAll_EmptyIsTrue_HasAnyEmptyIsFalse()
        {
            GameplayTagSet tags = new GameplayTagSet();
            Assert.IsTrue(tags.HasAll(new string[0]));
            Assert.IsFalse(tags.HasAny(new string[0]));
        }
    }
}
