//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using NUnit.Framework;
using EjoyFramework.Core;
using EjoyFramework.Core.DataTable;
using EjoyFramework.Tests.GeneratedRows;

namespace EjoyFramework.Tests
{
    /// <summary>
    /// WS5-M1：数据行切片解析。SpanHeroDataRow 与 DataRowGenerator 的输出逐字一致（ConfigToolchainTests 黄金文件测试保证），
    /// 因此这里的断言即是对生成代码的断言：各列类型、失败不留半状态、经 DataTableTextParser 入表，以及旧式 IDataRow 的退回路径。
    /// </summary>
    public class DataRowSpanParsingTests
    {
        [SetUp]
        public void SetUp()
        {
            Framework.MarkMainThread();
        }

        [Test]
        public void GeneratedRow_ParsesAllColumnTypes()
        {
            var row = new SpanHeroDataRow();
            Assert.IsTrue(row.ParseDataRow("7\tHero\t120\t2.5\tyes\t9000000000\t3".AsSpan(), null));
            Assert.AreEqual(7, row.Id);
            Assert.AreEqual("Hero", row.Name);
            Assert.AreEqual(120, row.Hp);
            Assert.AreEqual(2.5f, row.Speed);
            Assert.IsTrue(row.Enabled);
            Assert.AreEqual(9000000000L, row.Big);
            Assert.AreEqual(3, row.Skill);

            Assert.IsTrue(row.ParseDataRow("8\t\t0\t-1\t0\t0\t0\textra", null), "string overload; extra columns are ignored like the old Split parser");
            Assert.AreEqual(8, row.Id);
            Assert.AreSame(string.Empty, row.Name);
            Assert.IsFalse(row.Enabled);
        }

        [Test]
        public void GeneratedRow_InvalidLine_ReturnsFalseAndKeepsPreviousValues()
        {
            var row = new SpanHeroDataRow();
            Assert.IsTrue(row.ParseDataRow("7\tHero\t120\t2.5\ttrue\t1\t3".AsSpan(), null));
            string[] invalid =
            {
                "x\tHero\t120\t2.5\ttrue\t1\t3",          // Id 不是整数
                "7\tHero\t120\tfast\ttrue\t1\t3",         // Speed 不是浮点
                "7\tHero\t120\t2.5\tmaybe\t1\t3",         // Enabled 不是布尔
                "7\tHero\t120\t2.5\ttrue\t1",             // 列不足
                "7\tHero\t99999999999\t2.5\ttrue\t1\t3",  // Hp 溢出 int
            };

            foreach (string line in invalid)
            {
                Assert.IsFalse(row.ParseDataRow(line.AsSpan(), null), line);
                Assert.AreEqual(7, row.Id, line);
                Assert.AreEqual("Hero", row.Name, line);
                Assert.AreEqual(120, row.Hp, line);
                Assert.AreEqual(2.5f, row.Speed, line);
            }

            Assert.IsFalse(row.ParseDataRow((string)null, null));
        }

        [Test]
        public void TextParser_AddsSpanRows_AndSkipsBadLines()
        {
            var manager = new DataTableManager();
            IDataTable<SpanHeroDataRow> table = manager.CreateDataTable<SpanHeroDataRow>();
            string text =
                "# Id\tName\tHp\tSpeed\tEnabled\tBig\tSkill\n" +
                "1\tA\t10\t1.5\t1\t5\t0\r\n" +
                "2\tB\tbad\t1\t1\t5\t0\n" +
                "  3\tC\t30\t3\tfalse\t5\t0\n" +
                "1\tDuplicate\t10\t1.5\t1\t5\t0\n";
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Assert.IsTrue(DataTableTextParser.Parse(table, text, null));
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }

            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("A", table.GetDataRow(1).Name, "the duplicate id keeps the first row");
            Assert.AreEqual("C", table.GetDataRow(3).Name, "leading indentation is stripped before column 0");
            Assert.IsFalse(table.HasDataRow(2));
        }

        [Test]
        public void AddDataRow_Span_FallsBackToStringForLegacyRows()
        {
            var manager = new DataTableManager();
            IDataTable<LegacyRow> table = manager.CreateDataTable<LegacyRow>();
            Assert.IsTrue(table.AddDataRow("5\tFive".AsSpan(), null));
            Assert.AreEqual("Five", table.GetDataRow(5).Name);
            Assert.IsFalse(table.AddDataRow("5\tAgain".AsSpan(), null), "duplicate id");
            Assert.IsFalse(table.AddDataRow("x\tBad".AsSpan(), null), "parse failure");
            Assert.AreEqual(1, table.Count);
        }

        private sealed class LegacyRow : IDataRow
        {
            public int Id { get; private set; }

            public string Name { get; private set; }

            public bool ParseDataRow(string dataRowString, object userData)
            {
                string[] cols = dataRowString.Split('\t');
                int id;
                if (cols.Length < 2 || !int.TryParse(cols[0], out id))
                {
                    return false;
                }

                Id = id;
                Name = cols[1];
                return true;
            }
        }
    }
}
