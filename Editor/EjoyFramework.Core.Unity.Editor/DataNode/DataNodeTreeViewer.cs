//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.DataNode;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.DataNode
{
    /// <summary>Runtime DataNode 树查看：PlayMode 下浏览 DataNodeManager 内部树结构。</summary>
    public sealed class DataNodeTreeViewer : EditorWindow
    {
        private Vector2 m_Scroll;
        private readonly HashSet<string> m_Expanded = new HashSet<string>();

        [MenuItem("EjoyFramework/Core/DataNode/Tree Viewer")]
        public static void Open() => GetWindow<DataNodeTreeViewer>("DataNode Tree").Show();

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect DataNode tree.", MessageType.Info);
                return;
            }
            if (!Framework.HasModule<IDataNodeManager>())
            {
                EditorGUILayout.HelpBox("DataNodeManager not registered.", MessageType.Warning);
                return;
            }
            var mgr = Framework.GetModule<IDataNodeManager>();
            var root = mgr.Root;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            DrawNode(root, 0, "");
            EditorGUILayout.EndScrollView();
        }

        private void DrawNode(IDataNode node, int depth, string parentPath)
        {
            if (node == null) return;
            string fullPath = string.IsNullOrEmpty(parentPath) ? node.Name : parentPath + "." + node.Name;
            bool expanded = m_Expanded.Contains(fullPath);
            string prefix = new string(' ', depth * 2);
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = prefix + (node.ChildCount > 0 ? (expanded ? "▼" : "▶") : "●") + " " + node.Name;
                if (GUILayout.Button(label, EditorStyles.label, GUILayout.Width(280)))
                {
                    if (expanded) m_Expanded.Remove(fullPath);
                    else m_Expanded.Add(fullPath);
                }
                var val = node.GetData<Variable>();
                EditorGUILayout.LabelField(val != null ? val.ToString() : "(no data)", EditorStyles.miniLabel);
            }
            if (expanded)
            {
                foreach (var child in node.GetAllChild())
                    DrawNode(child, depth + 1, fullPath);
            }
        }
    }
}
