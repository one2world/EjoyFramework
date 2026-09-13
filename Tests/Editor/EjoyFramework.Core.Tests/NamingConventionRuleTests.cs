//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Unity.Editor.AssetPipeline;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// NamingConventionRule 纯逻辑判定测试（不依赖 AssetDatabase）。
    /// </summary>
    public class NamingConventionRuleTests
    {
        [TestCase("cg_home_lantern", false)]
        [TestCase("HeroCard", false)]
        [TestCase("ChatGPT Image 2026", true)]      // 空格
        [TestCase("勇者图标", true)]                  // 非 ASCII
        [TestCase("a_b-c1", false)]
        [TestCase("", false)]
        public void HasSpaceOrNonAscii_Detects(string name, bool expected)
        {
            Assert.AreEqual(expected, NamingConventionRule.HasSpaceOrNonAscii(name));
        }

        [TestCase(".png", true)]
        [TestCase(".jpg", true)]
        [TestCase(".tga", true)]
        [TestCase(".psd", true)]
        [TestCase(".prefab", false)]
        [TestCase(".wav", false)]
        [TestCase(".cs", false)]
        public void IsTexture_Detects(string ext, bool expected)
        {
            Assert.AreEqual(expected, NamingConventionRule.IsTexture(ext));
        }
    }
}
