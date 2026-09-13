//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Config;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// ConfigTextParser 单测（Phase 11.8）。
    /// 覆盖：空输入 / 单行 / 多行 / 注释 / 空行 / CRLF/LF/CR 兼容 / 列数不足 / 重复 key。
    /// 通过真 ConfigManager 实例验证副作用，不依赖 Unity 层。
    /// </summary>
    public class ConfigTextParserTests
    {
        private ConfigManager m_Manager;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Manager = new ConfigManager();
        }

        [Test]
        public void Parse_NullText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, null));
            Assert.AreEqual(0, m_Manager.Count);
        }

        [Test]
        public void Parse_EmptyText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, string.Empty));
            Assert.AreEqual(0, m_Manager.Count);
        }

        [Test]
        public void Parse_NullManager_Throws()
        {
            Assert.Throws<FrameworkException>(() => ConfigTextParser.Parse(null, "anything"));
        }

        [Test]
        public void Parse_SingleValidLine_AddsConfig()
        {
            string text = "HeroSpeed\t12.5\ttrue\t10\t12.5";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(1, m_Manager.Count);
            Assert.IsTrue(m_Manager.HasConfig("HeroSpeed"));
            Assert.IsTrue(m_Manager.GetBool("HeroSpeed"));
            Assert.AreEqual(10, m_Manager.GetInt("HeroSpeed"));
            Assert.AreEqual("12.5", m_Manager.GetString("HeroSpeed"));
        }

        [Test]
        public void Parse_SkipsCommentsAndBlankLines()
        {
            string text =
                "# Comment header\n" +
                "\n" +
                "  \n" +
                "Score\t100\tfalse\t100\t0\n" +
                "# Another comment\n";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(1, m_Manager.Count);
            Assert.IsTrue(m_Manager.HasConfig("Score"));
        }

        [Test]
        public void Parse_HandlesLfCrlfAndCrLineEndings()
        {
            string text = "A\t1\tfalse\t1\t1\r\nB\t2\tfalse\t2\t2\nC\t3\tfalse\t3\t3\rD\t4\tfalse\t4\t4";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(4, m_Manager.Count);
            Assert.IsTrue(m_Manager.HasConfig("A"));
            Assert.IsTrue(m_Manager.HasConfig("B"));
            Assert.IsTrue(m_Manager.HasConfig("C"));
            Assert.IsTrue(m_Manager.HasConfig("D"));
        }

        [Test]
        public void Parse_SkipsLinesWithTooFewColumns()
        {
            string text =
                "OK\tv\tfalse\t1\t1.0\n" +
                "Bad\tonly2cols\n" +
                "Bad2\tv\tfalse\n";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(1, m_Manager.Count);
            Assert.IsTrue(m_Manager.HasConfig("OK"));
            Assert.IsFalse(m_Manager.HasConfig("Bad"));
            Assert.IsFalse(m_Manager.HasConfig("Bad2"));
        }

        [Test]
        public void Parse_DuplicateKeySkippedSecondTime()
        {
            string text =
                "Dup\tfirst\tfalse\t1\t1\n" +
                "Dup\tsecond\tfalse\t2\t2\n";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(1, m_Manager.Count);
            Assert.AreEqual("first", m_Manager.GetString("Dup"));
        }
    }
}
