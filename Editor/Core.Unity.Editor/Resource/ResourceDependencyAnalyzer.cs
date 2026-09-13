//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Tools
{
    /// <summary>
    /// 资源依赖分析窗口：选中一个 Asset，递归显示所有引用它的 Asset / 所有它引用的 Asset。
    /// 帮助定位"为什么这个 prefab 被打到这个 bundle 里"等问题。
    /// </summary>
    public sealed class ResourceDependencyAnalyzer : EditorWindow
    {
        private Object m_Target;
        private Vector2 m_DepScroll, m_RefScroll;
        private string[] m_Dependencies;
        private string[] m_References;

        [MenuItem("EjoyFramework/Core/Resource/Dependency Analyzer")]
        public static void Open()
        {
            GetWindow<ResourceDependencyAnalyzer>("Resource Dependencies").Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Select target asset:", EditorStyles.boldLabel);
            var newTarget = EditorGUILayout.ObjectField(m_Target, typeof(Object), allowSceneObjects: false);
            if (newTarget != m_Target)
            {
                m_Target = newTarget;
                Analyze();
            }

            if (m_Target == null) { EditorGUILayout.HelpBox("Drop any asset above to see its dependencies & references.", MessageType.Info); return; }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.5f - 8)))
                {
                    EditorGUILayout.LabelField("Depends on (" + (m_Dependencies?.Length ?? 0) + ")", EditorStyles.boldLabel);
                    m_DepScroll = EditorGUILayout.BeginScrollView(m_DepScroll, GUILayout.ExpandHeight(true));
                    if (m_Dependencies != null)
                        foreach (var d in m_Dependencies) DrawAssetButton(d);
                    EditorGUILayout.EndScrollView();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Referenced by (" + (m_References?.Length ?? 0) + ")", EditorStyles.boldLabel);
                    m_RefScroll = EditorGUILayout.BeginScrollView(m_RefScroll, GUILayout.ExpandHeight(true));
                    if (m_References != null)
                        foreach (var r in m_References) DrawAssetButton(r);
                    EditorGUILayout.EndScrollView();
                }
            }

            if (GUILayout.Button("Re-analyze")) Analyze();
        }

        private void DrawAssetButton(string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(System.IO.Path.GetFileName(path), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Ping", GUILayout.Width(50)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                    EditorGUIUtility.PingObject(asset);
                }
            }
        }

        private void Analyze()
        {
            if (m_Target == null) return;
            string path = AssetDatabase.GetAssetPath(m_Target);
            m_Dependencies = AssetDatabase.GetDependencies(path, recursive: true);

            // 反向引用：扫描整个工程（重；只在按 Re-analyze 时跑）
            var refs = new List<string>();
            string[] all = AssetDatabase.FindAssets(string.Empty);
            EditorUtility.DisplayProgressBar("Dependency Analyzer", "Scanning project...", 0f);
            try
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (i % 200 == 0) EditorUtility.DisplayProgressBar("Dependency Analyzer",
                        "Scanning " + i + "/" + all.Length, (float)i / all.Length);
                    string p = AssetDatabase.GUIDToAssetPath(all[i]);
                    if (p == path) continue;
                    var deps = AssetDatabase.GetDependencies(p, recursive: false);
                    foreach (var d in deps)
                    {
                        if (d == path) { refs.Add(p); break; }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            m_References = refs.ToArray();
        }
    }
}
