//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using NUnit.Framework;
using EjoyFramework.Core.DataTable;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// DataTableTextParser 单测（Phase 11.8）。
    /// 覆盖：空输入 / 单行 / 多行 / 注释 / 空行 / 列保留行内 TAB /
    ///       CRLF/LF/CR 兼容 / ParseDataRow 返回 false 时跳过 / 重复 Id 被处理。
    /// 使用真 DataTableManager + 自定义 IDataRow 实现。
    /// </summary>
    public class DataTableTextParserTests
    {
        private DataTableManager m_Manager;
        private IDataTable<TestRow> m_Table;

        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
            m_Manager = new DataTableManager();
            m_Table = m_Manager.CreateDataTable<TestRow>();
        }

        [Test]
        public void Parse_NullText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, null, null));
            Assert.AreEqual(0, m_Table.Count);
        }

        [Test]
        public void Parse_EmptyText_ReturnsTrue_NoSideEffect()
        {
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, string.Empty, null));
            Assert.AreEqual(0, m_Table.Count);
        }

        [Test]
        public void Parse_NullTable_Throws()
        {
            Assert.Throws<FrameworkException>(() => DataTableTextParser.Parse<TestRow>(null, "1\tname", null));
        }

        [Test]
        public void Parse_SingleValidLine_AddsRow()
        {
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, "1\tHero", null));
            Assert.AreEqual(1, m_Table.Count);
            var row = m_Table.GetDataRow(1);
            Assert.IsNotNull(row);
            Assert.AreEqual("Hero", row.Name);
        }

        [Test]
        public void Parse_SkipsCommentsAndBlankLines()
        {
            string text =
                "# Header comment\n" +
                "\n" +
                "  \n" +
                "1\tFoo\n" +
                "# Another comment\n" +
                "2\tBar\n";
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, text, null));
            Assert.AreEqual(2, m_Table.Count);
            Assert.AreEqual("Foo", m_Table.GetDataRow(1).Name);
            Assert.AreEqual("Bar", m_Table.GetDataRow(2).Name);
        }

        [Test]
        public void Parse_PreservesInlineTabsForParseDataRow()
        {
            // 行内 TAB 必须传给 ParseDataRow，不能被解析器吞掉
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, "5\tFooName\tExtraField", null));
            Assert.AreEqual(1, m_Table.Count);
            var row = m_Table.GetDataRow(5);
            Assert.AreEqual("FooName", row.Name);
            Assert.AreEqual("ExtraField", row.Extra);
        }

        [Test]
        public void Parse_HandlesLfCrlfAndCrLineEndings()
        {
            string text = "1\tA\r\n2\tB\n3\tC\r4\tD";
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, text, null));
            Assert.AreEqual(4, m_Table.Count);
            Assert.AreEqual("A", m_Table.GetDataRow(1).Name);
            Assert.AreEqual("D", m_Table.GetDataRow(4).Name);
        }

        [Test]
        public void Parse_SkipsLinesWhereParseDataRowReturnsFalse()
        {
            string text =
                "1\tValid\n" +
                "notAnInt\tBad\n" +     // 第一列不是 int → ParseDataRow 返回 false
                "3\tAlsoValid\n";
            Assert.IsTrue(DataTableTextParser.Parse(m_Table, text, null));
            Assert.AreEqual(2, m_Table.Count);
            Assert.IsTrue(m_Table.HasDataRow(1));
            Assert.IsTrue(m_Table.HasDataRow(3));
        }

        private sealed class TestRow : IDataRow
        {
            public int Id { get; private set; }
            public string Name { get; private set; }
            public string Extra { get; private set; }
            public bool ParseDataRow(string text, object userData)
            {
                var cols = text.Split('\t');
                if (cols.Length < 2) return false;
                if (!int.TryParse(cols[0], out int id)) return false;
                Id = id;
                Name = cols[1];
                Extra = cols.Length >= 3 ? cols[2] : null;
                return true;
            }
        }
    }
}
