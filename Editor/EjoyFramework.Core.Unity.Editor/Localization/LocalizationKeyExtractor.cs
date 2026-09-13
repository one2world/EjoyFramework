//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Localization
{
    /// <summary>
    /// Localization Key 提取：扫描 Assets/ 下所有 .cs 文件，找出 GetString / Format / FormatNamed / GetPlural 调用，
    /// 输出 keys 列表 — 便于补全翻译表或检测多余条目。
    ///
    /// 支持模式（需 key 是字符串字面量；动态变量无法静态提取）：
    ///   loc.GetRawString("key")
    ///   loc.Format("key", ...)
    ///   loc.FormatNamed("key", ...)
    ///   loc.GetPlural("key", ...)
    ///   t("key")
    /// </summary>
    public sealed class LocalizationKeyExtractor : EditorWindow
    {
        private string m_ScanRoot = "Assets/GameMain";
        private readonly HashSet<string> m_FoundKeys = new HashSet<string>();
        private Vector2 m_Scroll;
        private string m_Status;

        private static readonly Regex KeyCallRegex = new Regex(
            @"\.(GetRawString|Format|FormatNamed|GetPlural)\s*\(\s*""([^""\\]+)""",
            RegexOptions.Compiled);

        private static readonly Regex TCallRegex = new Regex(
            @"\bt\s*\(\s*""([^""\\]+)""",
            RegexOptions.Compiled);

        [MenuItem("EjoyFramework/Core/Localization/Extract Keys from Code")]
        public static void Open() => GetWindow<LocalizationKeyExtractor>("Loc Key Extractor").Show();

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Scan root:", GUILayout.Width(80));
                m_ScanRoot = EditorGUILayout.TextField(m_ScanRoot);
                if (GUILayout.Button("Scan", GUILayout.Width(80))) Scan();
            }
            if (!string.IsNullOrEmpty(m_Status)) EditorGUILayout.LabelField(m_Status, EditorStyles.boldLabel);

            if (m_FoundKeys.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy to Clipboard")) EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_FoundKeys);
                if (GUILayout.Button("Export → CSV"))
                {
                    string path = EditorUtility.SaveFilePanel("Save keys CSV", m_ScanRoot, "extracted_keys.csv", "csv");
                    if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, "Key\n" + string.Join("\n", m_FoundKeys));
                }
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var k in m_FoundKeys)
            {
                EditorGUILayout.LabelField(k);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            m_FoundKeys.Clear();
            if (!Directory.Exists(m_ScanRoot))
            {
                m_Status = "Directory not found: " + m_ScanRoot;
                return;
            }
            int filesScanned = 0;
            foreach (var path in Directory.GetFiles(m_ScanRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                foreach (Match m in KeyCallRegex.Matches(text)) m_FoundKeys.Add(m.Groups[2].Value);
                foreach (Match m in TCallRegex.Matches(text)) m_FoundKeys.Add(m.Groups[1].Value);
                filesScanned++;
            }
            m_Status = "Scanned " + filesScanned + " files; found " + m_FoundKeys.Count + " unique keys.";
        }
    }
}
