//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Unity.Editor.Resource;

namespace EjoyFramework.Tests
{
    public class GlobMatcherTests
    {
        [Test]
        public void LiteralMatch_OK()
        {
            Assert.IsTrue(GlobMatcher.Match("Assets/foo.prefab", "Assets/foo.prefab"));
            Assert.IsFalse(GlobMatcher.Match("Assets/foo.prefab", "Assets/bar.prefab"));
        }

        [Test]
        public void CaseInsensitive()
        {
            Assert.IsTrue(GlobMatcher.Match("Assets/Foo.PREFAB", "assets/foo.prefab"));
        }

        [Test]
        public void SingleStarMatchesWithinSegment()
        {
            Assert.IsTrue(GlobMatcher.Match("Assets/*.prefab", "Assets/foo.prefab"));
            Assert.IsTrue(GlobMatcher.Match("Assets/*.prefab", "Assets/bar.prefab"));
            // 单星不跨 /
            Assert.IsFalse(GlobMatcher.Match("Assets/*.prefab", "Assets/sub/foo.prefab"));
        }

        [Test]
        public void DoubleStarMatchesAcrossSegments()
        {
            Assert.IsTrue(GlobMatcher.Match("Assets/**/*.prefab", "Assets/foo.prefab"));
            Assert.IsTrue(GlobMatcher.Match("Assets/**/*.prefab", "Assets/sub/foo.prefab"));
            Assert.IsTrue(GlobMatcher.Match("Assets/**/*.prefab", "Assets/a/b/c/foo.prefab"));
        }

        [Test]
        public void DoubleStarAtTail()
        {
            Assert.IsTrue(GlobMatcher.Match("Assets/GameMain/UI/**", "Assets/GameMain/UI/MainMenu/MainMenu.prefab"));
            Assert.IsFalse(GlobMatcher.Match("Assets/GameMain/UI/**", "Assets/GameMain/Effect/X.prefab"));
        }

        [Test]
        public void QuestionMarkMatchesSingleChar()
        {
            Assert.IsTrue(GlobMatcher.Match("foo?.txt", "fooa.txt"));
            Assert.IsFalse(GlobMatcher.Match("foo?.txt", "foo.txt"));
            Assert.IsFalse(GlobMatcher.Match("foo?.txt", "fooab.txt"));
        }

        [Test]
        public void ExcludeEditorTests_CommonPatterns()
        {
            Assert.IsTrue(GlobMatcher.Match("**/Editor/**", "Assets/Foo/Editor/Bar.cs"));
            Assert.IsTrue(GlobMatcher.Match("**/Tests/**", "Assets/Foo/Tests/X.cs"));
            Assert.IsFalse(GlobMatcher.Match("**/Editor/**", "Assets/Foo/Bar.cs"));
        }

        [Test]
        public void Sanitize_LowerSnakeCase()
        {
            Assert.AreEqual("gamemain_ui_mainmenu", GlobMatcher.SanitizeBundleName("GameMain/UI/MainMenu"));
            Assert.AreEqual("foo_bar", GlobMatcher.SanitizeBundleName("Foo.Bar"));
            Assert.AreEqual("default", GlobMatcher.SanitizeBundleName(""));
            Assert.AreEqual("a_b", GlobMatcher.SanitizeBundleName("A__B"));
        }

        [Test]
        public void MatchAny_OrSemantics()
        {
            var patterns = new System.Collections.Generic.List<string> { "**/Editor/**", "**/.git/**" };
            Assert.IsTrue(GlobMatcher.MatchAny(patterns, "Assets/Foo/Editor/Bar.cs"));
            Assert.IsTrue(GlobMatcher.MatchAny(patterns, "Project/.git/HEAD"));
            Assert.IsFalse(GlobMatcher.MatchAny(patterns, "Assets/Foo/Bar.cs"));
        }
    }
}
