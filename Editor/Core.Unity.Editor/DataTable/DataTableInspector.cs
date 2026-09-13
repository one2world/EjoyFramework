//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.DataTable
{
    /// <summary>
    /// DataTable Inspector：选择一个 .txt 数据表（Configs/Generated/*.txt），表格化预览所有 row。
    /// 不修改源文件（只读浏览），策划改数据请回 CSV 重导。
    /// </summary>
    public sealed class DataTableInspector : EditorWindow
    {
        private string m_DefaultDir = "Assets/Configs/Generated";
        private List<string> m_Files = new List<string>();
        private int m_SelectedIdx = -1;
        private string[][] m_Rows;
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/DataTable/Inspector")]
        public static void Open() => GetWindow<DataTableInspector>("DataTable Inspector").Show();

        private void OnEnable() => Refresh();

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Dir:", GUILayout.Width(30));
                m_DefaultDir = EditorGUILayout.TextField(m_DefaultDir);
                if (GUILayout.Button("Refresh", GUILayout.Width(80))) Refresh();
            }

            if (m_Files.Count == 0)
            {
                EditorGUILayout.HelpBox("No .txt data tables found in " + m_DefaultDir, MessageType.Info);
                return;
            }

            int newSel = EditorGUILayout.Popup("Table:", m_SelectedIdx, m_Files.ToArray());
            if (newSel != m_SelectedIdx)
            {
                m_SelectedIdx = newSel;
                LoadTable(m_Files[m_SelectedIdx]);
            }

            if (m_Rows == null) return;
            EditorGUILayout.LabelField("Rows: " + m_Rows.Length, EditorStyles.miniLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int r = 0; r < m_Rows.Length; r++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField((r + 1).ToString(), GUILayout.Width(40));
                    foreach (var cell in m_Rows[r])
                    {
                        EditorGUILayout.LabelField(cell, EditorStyles.textField, GUILayout.MinWidth(80));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            m_Files.Clear();
            m_SelectedIdx = -1;
            m_Rows = null;
            if (!Directory.Exists(m_DefaultDir)) return;
            foreach (var f in Directory.GetFiles(m_DefaultDir, "*.txt"))
                m_Files.Add(f);
        }

        private void LoadTable(string path)
        {
            var rows = new List<string[]>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                rows.Add(line.Split('\t'));
            }
            m_Rows = rows.ToArray();
        }
    }
}
