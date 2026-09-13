//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Config
{
    /// <summary>
    /// CSV → DataTable 文本 + IDataRow 代码生成 一键工具。
    /// 菜单：EjoyFramework.Core / Config / Export from CSV
    ///
    /// 输入：Assets/Configs/Source/*.csv  (策划维护)
    /// 输出：
    ///   - Assets/Configs/Generated/*.txt (TAB 分隔运行时数据表)
    ///   - Assets/GameMain/Scripts/Data/*DataRow.cs (生成的 IDataRow 子类)
    /// </summary>
    public static class ConfigExporter
    {
        private const string DefaultSourceDir = "Assets/Configs/Source";
        private const string DefaultOutputDir = "Assets/Configs/Generated";
        private const string DefaultCodeDir = "Assets/GameMain/Scripts/Data";
        private const string DefaultNamespace = "EjoyGame.Data";

        [MenuItem("EjoyFramework/Core/Config/Export from CSV")]
        public static void ExportAll()
        {
            if (!Directory.Exists(DefaultSourceDir))
            {
                EditorUtility.DisplayDialog("ConfigExporter",
                    "Source directory not found: " + DefaultSourceDir +
                    "\n\nCreate it and put *.csv files inside, then re-run.", "OK");
                return;
            }
            Directory.CreateDirectory(DefaultOutputDir);
            Directory.CreateDirectory(DefaultCodeDir);

            var ctx = new ConfigValidator.ValidationContext();
            var allErrors = new List<string>();
            var summary = new StringBuilder();
            int success = 0, failed = 0;

            // Pass 1：解析所有表并收集 Id 集合到 ctx（保证跨表外键引用在第二遍能查到，无论字母序）。
            var parsedTables = new List<(string tableName, ConfigTableSchema schema, List<string[]> dataRows)>();
            foreach (var csvPath in Directory.GetFiles(DefaultSourceDir, "*.csv"))
            {
                string tableName = Path.GetFileNameWithoutExtension(csvPath);
                try
                {
                    var rows = CsvParser.ParseFile(csvPath);
                    var schema = ConfigTableSchema.Parse(tableName, rows);
                    var dataRows = new List<string[]>();
                    for (int i = 2; i < rows.Count; i++) dataRows.Add(rows[i]);

                    ConfigValidator.CollectIds(schema, dataRows, ctx);
                    parsedTables.Add((tableName, schema, dataRows));
                }
                catch (System.Exception ex)
                {
                    allErrors.Add("[" + tableName + "] " + ex.Message);
                    failed++;
                }
            }

            // Pass 2：在 ctx 已含全部表 Id 的前提下校验，并生成 .txt + .cs。
            foreach (var (tableName, schema, dataRows) in parsedTables)
            {
                try
                {
                    var errors = ConfigValidator.Validate(schema, dataRows, ctx);
                    if (errors.Count > 0)
                    {
                        allErrors.AddRange(errors);
                        failed++;
                        continue;
                    }

                    // 生成 .txt
                    string txtPath = Path.Combine(DefaultOutputDir, tableName + ".txt");
                    WriteDataTable(txtPath, dataRows);

                    // 生成 .cs
                    string code = DataRowGenerator.Generate(schema, DefaultNamespace);
                    string csPath = Path.Combine(DefaultCodeDir, tableName + "DataRow.cs");
                    File.WriteAllText(csPath, code, Encoding.UTF8);

                    summary.AppendLine("✓ " + tableName + ": " + dataRows.Count + " rows");
                    success++;
                }
                catch (System.Exception ex)
                {
                    allErrors.Add("[" + tableName + "] " + ex.Message);
                    failed++;
                }
            }

            AssetDatabase.Refresh();

            if (allErrors.Count > 0)
            {
                Debug.LogError("ConfigExporter found " + allErrors.Count + " error(s):\n" + string.Join("\n", allErrors));
                EditorUtility.DisplayDialog("ConfigExporter — FAILURES",
                    "Failed tables: " + failed + " / " + (success + failed) +
                    "\nSee Console for details.\n\nSuccessful tables: " + success, "OK");
            }
            else
            {
                Debug.Log("ConfigExporter OK:\n" + summary);
                EditorUtility.DisplayDialog("ConfigExporter — OK",
                    "Exported " + success + " tables.\n\n" + summary, "OK");
            }
        }

        private static void WriteDataTable(string path, List<string[]> dataRows)
        {
            using (var sw = new StreamWriter(path, /*append*/ false, Encoding.UTF8))
            {
                foreach (var row in dataRows)
                {
                    sw.WriteLine(string.Join("\t", row));
                }
            }
        }
    }
}
