//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Tools
{
    /// <summary>
    /// 多语言翻译进度报告：扫描 Localization/<lang>.txt 文件计算每语言覆盖率。
    /// 输出每个 key 是否在每语言都存在 + 总覆盖百分比。
    /// </summary>
    public sealed class LocalizationCoverageReport : EditorWindow
    {
        private string m_LocDir = "Assets/Localization";
        private Dictionary<string, Dictionary<string, string>> m_LangKv;   // lang → key→value
        private List<string> m_AllKeys = new List<string>();
        private List<string> m_Langs = new List<string>();
        private Vector2 m_Scroll;
        private string m_Summary;

        [MenuItem("EjoyFramework/Core/Localization/Coverage Report")]
        public static void Open() => GetWindow<LocalizationCoverageReport>("Loc Coverage").Show();

        private void OnGUI()
        {
            m_LocDir = EditorGUILayout.TextField("Localization dir:", m_LocDir);
            if (GUILayout.Button("Scan"))
            {
                Scan();
            }

            if (m_LangKv == null) { EditorGUILayout.HelpBox("Each language is one .txt file: key\\tvalue per line. Place under Localization dir.", MessageType.Info); return; }

            EditorGUILayout.LabelField(m_Summary, EditorStyles.boldLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            // Header row
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Key", GUILayout.Width(240));
                foreach (var lang in m_Langs) EditorGUILayout.LabelField(lang, GUILayout.Width(80));
            }
            foreach (var key in m_AllKeys)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(key, GUILayout.Width(240));
                    foreach (var lang in m_Langs)
                    {
                        bool has = m_LangKv[lang].ContainsKey(key);
                        var prev = GUI.color;
                        GUI.color = has ? Color.green : Color.red;
                        EditorGUILayout.LabelField(has ? "✓" : "✗", GUILayout.Width(80));
                        GUI.color = prev;
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Export missing keys → CSV"))
            {
                ExportMissingKeysCsv();
            }
        }

        private void Scan()
        {
            m_LangKv = new Dictionary<string, Dictionary<string, string>>();
            m_AllKeys.Clear();
            m_Langs.Clear();
            if (!Directory.Exists(m_LocDir))
            {
                EditorUtility.DisplayDialog("Loc Coverage", "Directory does not exist: " + m_LocDir, "OK");
                return;
            }

            var keysSet = new HashSet<string>();
            foreach (var path in Directory.GetFiles(m_LocDir, "*.txt"))
            {
                string lang = Path.GetFileNameWithoutExtension(path);
                m_Langs.Add(lang);
                var kv = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string k = line.Substring(0, tab);
                    string v = line.Substring(tab + 1);
                    kv[k] = v;
                    keysSet.Add(k);
                }
                m_LangKv[lang] = kv;
            }
            foreach (var k in keysSet) m_AllKeys.Add(k);
            m_AllKeys.Sort();

            int totalKv = m_AllKeys.Count * m_Langs.Count;
            int presentKv = 0;
            foreach (var lang in m_Langs)
                foreach (var key in m_AllKeys)
                    if (m_LangKv[lang].ContainsKey(key)) presentKv++;
            float pct = totalKv == 0 ? 0f : 100f * presentKv / totalKv;
            m_Summary = string.Format("{0} languages × {1} keys = {2} entries; {3} present ({4:F1}%)",
                m_Langs.Count, m_AllKeys.Count, totalKv, presentKv, pct);
        }

        private void ExportMissingKeysCsv()
        {
            string path = EditorUtility.SaveFilePanel("Save missing keys CSV", m_LocDir, "missing_keys.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            var sb = new StringBuilder();
            sb.Append("Key");
            foreach (var lang in m_Langs) { sb.Append(','); sb.Append(lang); }
            sb.AppendLine();
            foreach (var key in m_AllKeys)
            {
                bool anyMissing = false;
                foreach (var lang in m_Langs) if (!m_LangKv[lang].ContainsKey(key)) { anyMissing = true; break; }
                if (!anyMissing) continue;
                sb.Append(key);
                foreach (var lang in m_Langs)
                {
                    sb.Append(',');
                    sb.Append(m_LangKv[lang].ContainsKey(key) ? "OK" : "MISSING");
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }
    }
}
