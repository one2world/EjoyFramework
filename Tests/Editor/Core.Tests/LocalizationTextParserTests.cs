//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.Localization;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// LocalizationTextParser 单测（Phase 11.8）。
    /// 覆盖：空输入 / 单行 / 多行 / 注释 / 空行 / CRLF/LF/CR 兼容 /
    ///       无 TAB 分隔符 / 重复 key / Value 包含 TAB 时正确切分。
    /// </summary>
    public class LocalizationTextParserTests
    {
        private LocalizationManager m_Manager;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Manager = new LocalizationManager();
        }

        [Test]
        public void Parse_NullText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, null));
            Assert.AreEqual(0, m_Manager.DictionaryCount);
        }

        [Test]
        public void Parse_EmptyText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, string.Empty));
            Assert.AreEqual(0, m_Manager.DictionaryCount);
        }

        [Test]
        public void Parse_NullManager_Throws()
        {
            Assert.Throws<FrameworkException>(() => LocalizationTextParser.Parse(null, "k\tv"));
        }

        [Test]
        public void Parse_SingleValidLine_AddsEntry()
        {
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, "Hello\t你好"));
            Assert.AreEqual(1, m_Manager.DictionaryCount);
            Assert.AreEqual("你好", m_Manager.GetRawString("Hello"));
        }

        [Test]
        public void Parse_SkipsCommentsAndBlankLines()
        {
            string text =
                "# header\n" +
                "\n" +
                "Hello\tHi\n" +
                "# trailer\n";
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, text));
            Assert.AreEqual(1, m_Manager.DictionaryCount);
            Assert.AreEqual("Hi", m_Manager.GetRawString("Hello"));
        }

        [Test]
        public void Parse_HandlesLfCrlfAndCrLineEndings()
        {
            string text = "A\t1\r\nB\t2\nC\t3\rD\t4";
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, text));
            Assert.AreEqual(4, m_Manager.DictionaryCount);
            Assert.AreEqual("1", m_Manager.GetRawString("A"));
            Assert.AreEqual("4", m_Manager.GetRawString("D"));
        }

        [Test]
        public void Parse_SkipsLinesWithoutTab()
        {
            string text =
                "OK\tvalue\n" +
                "BadNoTab\n" +
                "AlsoOK\tvalue2\n";
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, text));
            Assert.AreEqual(2, m_Manager.DictionaryCount);
            Assert.IsFalse(m_Manager.HasRawString("BadNoTab"));
            Assert.IsTrue(m_Manager.HasRawString("OK"));
            Assert.IsTrue(m_Manager.HasRawString("AlsoOK"));
        }

        [Test]
        public void Parse_DuplicateKeySkippedSecondTime()
        {
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, "Dup\tfirst\nDup\tsecond"));
            Assert.AreEqual(1, m_Manager.DictionaryCount);
            Assert.AreEqual("first", m_Manager.GetRawString("Dup"));
        }

        [Test]
        public void Parse_ValueWithEmbeddedTab_PreservesTab()
        {
            // 仅按第一个 TAB 切分；后续 TAB 应当属于 Value 的内容。
            Assert.IsTrue(LocalizationTextParser.Parse(m_Manager, "Composite\tpart1\tpart2"));
            Assert.AreEqual("part1\tpart2", m_Manager.GetRawString("Composite"));
        }
    }
}
