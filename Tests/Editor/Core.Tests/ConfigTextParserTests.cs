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

        // ===== WS5-M1：切片解析、宽松布尔、StringHash 查找 =====

        [Test]
        public void Parse_BoolColumn_AcceptsLenientForms()
        {
            string text = "A\tv\t1\t0\t0\nB\tv\tyes\t0\t0\nC\tv\tNO\t0\t0\nD\tv\tTrue\t0\t0\nE\tv\tmaybe\t0\t0";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(5, m_Manager.Count);
            Assert.IsTrue(m_Manager.GetBool("A"));
            Assert.IsTrue(m_Manager.GetBool("B"));
            Assert.IsFalse(m_Manager.GetBool("C"));
            Assert.IsTrue(m_Manager.GetBool("D"));
            Assert.IsFalse(m_Manager.GetBool("E"), "unrecognized text is false");
        }

        [Test]
        public void Parse_NumericColumns_AreInvariantAndDefaultToZero()
        {
            string text = "N\tv\tfalse\t-12\t-0.5\nBad\tv\tfalse\tx\ty";
            Assert.IsTrue(ConfigTextParser.Parse(m_Manager, text));
            Assert.AreEqual(-12, m_Manager.GetInt("N"));
            Assert.AreEqual(-0.5f, m_Manager.GetFloat("N"));
            Assert.AreEqual(0, m_Manager.GetInt("Bad"));
            Assert.AreEqual(0f, m_Manager.GetFloat("Bad"));
            Assert.AreEqual("v", m_Manager.GetString("Bad"));
        }

        [Test]
        public void HashLookups_MatchStringLookups()
        {
            ConfigTextParser.Parse(m_Manager, "Speed\t12.5\ttrue\t10\t12.5");
            StringHash speed = new StringHash(StringHash.Compute("Speed"));
            Assert.IsTrue(m_Manager.HasConfig(speed));
            Assert.IsTrue(m_Manager.GetBool(speed));
            Assert.AreEqual(10, m_Manager.GetInt(speed));
            Assert.AreEqual(12.5f, m_Manager.GetFloat(speed));
            Assert.AreEqual("12.5", m_Manager.GetString(speed));

            bool b;
            int i;
            float f;
            string s;
            Assert.IsTrue(m_Manager.TryGetBool(speed, out b) && b);
            Assert.IsTrue(m_Manager.TryGetInt(speed, out i) && i == 10);
            Assert.IsTrue(m_Manager.TryGetFloat(speed, out f) && f == 12.5f);
            Assert.IsTrue(m_Manager.TryGetString(speed, out s) && s == "12.5");

            StringHash missing = new StringHash(StringHash.Compute("Missing"));
            Assert.IsFalse(m_Manager.HasConfig(missing));
            Assert.IsFalse(m_Manager.GetBool(missing));
            Assert.AreEqual(0, m_Manager.GetInt(missing));
            Assert.AreEqual(0f, m_Manager.GetFloat(missing));
            Assert.IsNull(m_Manager.GetString(missing));
            Assert.IsFalse(m_Manager.TryGetInt(missing, out i));
            Assert.AreEqual(0, i);
            Assert.IsFalse(m_Manager.TryGetString(missing, out s));
            Assert.IsNull(s);
        }

        [Test]
        public void AddConfig_RejectsNameWithCollidingHash()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                // costarring / liquid 是已知的 FNV-1a-32 碰撞对：第二个名字入库会让哈希查找产生歧义，必须拒绝。
                Assert.IsTrue(m_Manager.AddConfig("costarring", "1", true, 1, 1f));
                Assert.IsFalse(m_Manager.AddConfig("liquid", "2", false, 2, 2f));
                Assert.AreEqual(1, m_Manager.Count);
                Assert.IsFalse(m_Manager.HasConfig("liquid"));
                Assert.AreEqual(1, m_Manager.GetInt(new StringHash(StringHash.Compute("liquid"))), "the hash resolves only to the first name");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }

        [Test]
        public void HashLookups_AreAllocationFree()
        {
            ConfigTextParser.Parse(m_Manager, "Speed\t12.5\ttrue\t10\t12.5\nName\tHero\tfalse\t0\t0");
            StringHash speed = new StringHash(StringHash.Compute("Speed"));
            StringHash name = new StringHash(StringHash.Compute("Name"));
            float sink = 0f;
            ZeroAlloc.Assert(() =>
            {
                bool b;
                sink += m_Manager.GetInt(speed) + m_Manager.GetFloat(speed);
                sink += m_Manager.TryGetBool(name, out b) && !b ? 1f : 0f;
                sink += m_Manager.GetString(name).Length;
            });
            Assert.Greater(sink, 0f);
        }
    }
}
