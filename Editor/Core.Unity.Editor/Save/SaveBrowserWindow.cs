//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Save
{
    /// <summary>
    /// 存档浏览器：列出 persistentDataPath/saves/*.sav，显示元数据，支持删除。
    /// 解密需要业务侧 passphrase；本工具仅以原始字节显示文件大小 / 修改时间。
    /// </summary>
    public sealed class SaveBrowserWindow : EditorWindow
    {
        private string m_SaveDir;
        private readonly List<FileInfo> m_Files = new List<FileInfo>();
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/Save/Browse Save Slots")]
        public static void Open() => GetWindow<SaveBrowserWindow>("Save Browser").Show();

        private void OnEnable()
        {
            m_SaveDir = Path.Combine(Application.persistentDataPath, "saves");
            Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Save dir:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(m_SaveDir, EditorStyles.textField, GUILayout.Height(18));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(80))) Refresh();
                if (GUILayout.Button("Open in Explorer", GUILayout.Width(140)))
                {
                    if (!Directory.Exists(m_SaveDir)) Directory.CreateDirectory(m_SaveDir);
                    EditorUtility.RevealInFinder(m_SaveDir);
                }
                if (GUILayout.Button("Delete ALL", GUILayout.Width(100)))
                {
                    if (EditorUtility.DisplayDialog("Confirm", "Delete all save slots? This cannot be undone.", "Yes", "Cancel"))
                    {
                        foreach (var f in m_Files) { try { f.Delete(); } catch (System.Exception ex) { Debug.LogError(ex); } }
                        Refresh();
                    }
                }
            }

            EditorGUILayout.LabelField("Slots: " + m_Files.Count);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var f in m_Files)
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(f.Name, GUILayout.Width(160));
                    EditorGUILayout.LabelField(f.Length + " bytes", GUILayout.Width(100));
                    EditorGUILayout.LabelField(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), GUILayout.Width(160));
                    if (GUILayout.Button("Delete", GUILayout.Width(80)))
                    {
                        try { f.Delete(); Refresh(); }
                        catch (System.Exception ex) { Debug.LogError(ex); }
                        break;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            m_Files.Clear();
            if (!Directory.Exists(m_SaveDir)) return;
            foreach (var f in Directory.GetFiles(m_SaveDir, "*.sav"))
                m_Files.Add(new FileInfo(f));
            m_Files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        }
    }
}
