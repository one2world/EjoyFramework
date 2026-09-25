//------------------------------------------------------------
// EjoyGame Framework Tests
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using NUnit.Framework;
using EjoyFramework.Core.Unity.Editor.Config;

using EjoyFramework.Core;
namespace EjoyFramework.Tests
{
    /// <summary>
    /// 配置工具链单测（Phase 15）。
    /// 覆盖：CSV 解析（含引号转义）/ Schema 解析（含 reference 类型）/
    ///       Validator 重复 Id / 类型 mismatch / 外键引用 / DataRowGenerator 代码片段。
    /// </summary>
    public class ConfigToolchainTests
    {
        [Test]
        public void CsvParser_SimpleRow()
        {
            var rows = CsvParser.Parse("a,b,c\n1,2,3");
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, rows[1]);
        }

        [Test]
        public void CsvParser_HandlesQuotedFields_AndEscapedQuotes()
        {
            var rows = CsvParser.Parse("Id,Name\n1,\"Hello, World\"\n2,\"She said \"\"hi\"\"\"");
            Assert.AreEqual(3, rows.Count);
            Assert.AreEqual("Hello, World", rows[1][1]);
            Assert.AreEqual("She said \"hi\"", rows[2][1]);
        }

        [Test]
        public void Schema_RequiresIdColumn()
        {
            var rows = new List<string[]>
            {
                new[] { "Name", "Hp" },
                new[] { "string", "int" },
            };
            Assert.Throws<FrameworkException>(() => ConfigTableSchema.Parse("NoId", rows));
        }

        [Test]
        public void Schema_ParsesReferenceType()
        {
            var rows = new List<string[]>
            {
                new[] { "Id", "SkillId" },
                new[] { "int", "reference:Skill" },
            };
            var s = ConfigTableSchema.Parse("Hero", rows);
            Assert.AreEqual(2, s.Columns.Count);
            Assert.AreEqual("reference", s.Columns[1].Type);
            Assert.AreEqual("Skill", s.Columns[1].ReferenceTarget);
        }

        [Test]
        public void Validator_DuplicateId_ReportsError()
        {
            var schema = ConfigTableSchema.Parse("T", new List<string[]>
            {
                new[] { "Id", "Name" },
                new[] { "int", "string" },
            });
            var data = new List<string[]>
            {
                new[] { "1", "A" },
                new[] { "1", "B" },   // dup
            };
            var errs = ConfigValidator.Validate(schema, data, new ConfigValidator.ValidationContext());
            Assert.AreEqual(1, errs.Count);
            StringAssert.Contains("duplicate Id 1", errs[0]);
        }

        [Test]
        public void Validator_TypeMismatch_ReportsError()
        {
            var schema = ConfigTableSchema.Parse("T", new List<string[]>
            {
                new[] { "Id", "Hp" },
                new[] { "int", "int" },
            });
            var data = new List<string[]>
            {
                new[] { "1", "abc" },   // Hp not int
            };
            var errs = ConfigValidator.Validate(schema, data, new ConfigValidator.ValidationContext());
            Assert.GreaterOrEqual(errs.Count, 1);
            StringAssert.Contains("not int", errs[0]);
        }

        [Test]
        public void Validator_ReferenceTarget_Missing_ReportsError()
        {
            var ctx = new ConfigValidator.ValidationContext();
            // 先加载 Skill 表（仅 Id=10）
            var skillSchema = ConfigTableSchema.Parse("Skill", new List<string[]>
            {
                new[] { "Id", "Name" },
                new[] { "int", "string" },
            });
            ConfigValidator.Validate(skillSchema, new List<string[]> { new[] { "10", "Fireball" } }, ctx);

            // 再加载 Hero 表引用 Skill
            var heroSchema = ConfigTableSchema.Parse("Hero", new List<string[]>
            {
                new[] { "Id", "SkillId" },
                new[] { "int", "reference:Skill" },
            });
            var heroData = new List<string[]>
            {
                new[] { "1", "10" },   // OK
                new[] { "2", "999" },  // 不存在
            };
            var errs = ConfigValidator.Validate(heroSchema, heroData, ctx);
            Assert.AreEqual(1, errs.Count);
            StringAssert.Contains("Skill.999", errs[0]);
        }

        [Test]
        public void DataRowGenerator_ProducesCompilableSkeleton()
        {
            var schema = ConfigTableSchema.Parse("Hero", new List<string[]>
            {
                new[] { "Id", "Name", "Hp", "Speed" },
                new[] { "int", "string", "int", "float" },
            });
            string code = DataRowGenerator.Generate(schema, "Game.Data");
            StringAssert.Contains("public sealed class HeroDataRow : ISpanDataRow", code);
            StringAssert.Contains("public int Id", code);
            StringAssert.Contains("public string Name", code);
            StringAssert.Contains("public float Speed", code);
            StringAssert.Contains("public bool ParseDataRow", code);
        }

        /// <summary>
        /// 生成器输出与已编入测试程序集的 Generated/DataRow/SpanHeroDataRow.g.cs 逐字一致（忽略换行风格），
        /// 因此 DataRowSpanParsingTests 对该类的运行期断言即是对生成代码的断言。
        /// </summary>
        [Test]
        public void DataRowGenerator_MatchesCompiledGoldenFile()
        {
            var schema = ConfigTableSchema.Parse("SpanHero", new List<string[]>
            {
                new[] { "Id", "Name", "Hp", "Speed", "Enabled", "Big", "Skill" },
                new[] { "int", "string", "int", "float", "bool", "long", "reference:Skill" },
            });
            string code = DataRowGenerator.Generate(schema, "EjoyFramework.Tests.GeneratedRows");
            string goldenPath = System.IO.Path.Combine(
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ConfigToolchainTests).Assembly).resolvedPath,
                "Tests/Editor/Core.Tests/Generated/DataRow/SpanHeroDataRow.g.cs");
            string golden = System.IO.File.ReadAllText(goldenPath, System.Text.Encoding.UTF8);
            Assert.AreEqual(Normalize(golden), Normalize(code));
        }

        private static string Normalize(string text)
        {
            return text.TrimStart('\uFEFF').Replace("\r\n", "\n").TrimEnd();
        }
    }
}
