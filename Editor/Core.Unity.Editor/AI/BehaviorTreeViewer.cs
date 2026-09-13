//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EjoyFramework.Core.AI;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AI
{
    /// <summary>
    /// 行为树查看器 (Phase 26 baseline)：runtime 树状结构显示 + 当前 tick 状态高亮。
    /// 不是完整 UI Toolkit 可视化编辑器（那是更大工程），但已能 debug 大部分 BT 问题。
    /// </summary>
    public sealed class BehaviorTreeViewer : EditorWindow
    {
        [MenuItem("EjoyFramework/Core/AI/Behavior Tree Viewer")]
        public static void Open() => GetWindow<BehaviorTreeViewer>("BT Viewer").Show();

        private object m_Tree;   // BehaviorTree<TContext>，业务侧通过反射检视
        private Vector2 m_Scroll;
        private string m_Status = "Drop a BT-owning component into the field via reflection.";

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Runtime Behavior Tree Inspector", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to attach a runtime BehaviorTree instance.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(m_Status, EditorStyles.miniLabel);

            // 业务侧绑定：通过 reflection 在场景里搜任意持 BehaviorTree<T> 字段的 MonoBehaviour
            if (GUILayout.Button("Scan scene for BehaviorTree owners"))
            {
                ScanScene();
            }

            if (m_Tree == null) return;

            // 通过 reflection 调 Root 属性
            var rootProp = m_Tree.GetType().GetProperty("Root");
            var lastStatusProp = m_Tree.GetType().GetProperty("LastStatus");
            object root = rootProp?.GetValue(m_Tree);
            object lastStatus = lastStatusProp?.GetValue(m_Tree);

            EditorGUILayout.LabelField("LastStatus: " + lastStatus, EditorStyles.boldLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (root != null) DrawNode(root, 0);
            EditorGUILayout.EndScrollView();
        }

        private void ScanScene()
        {
            m_Tree = null;
            m_Status = "Scanning scene MonoBehaviours...";
            var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in behaviours)
            {
                if (mb == null) continue;
                var fields = mb.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (var f in fields)
                {
                    if (f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(BehaviorTree<>))
                    {
                        var tree = f.GetValue(mb);
                        if (tree != null)
                        {
                            m_Tree = tree;
                            m_Status = "Attached to " + mb.GetType().Name + " on " + mb.gameObject.name;
                            return;
                        }
                    }
                }
            }
            m_Status = "No BehaviorTree<T> instance found in scene.";
        }

        private void DrawNode(object node, int depth)
        {
            string prefix = new string(' ', depth * 2);
            string typeName = node.GetType().Name;
            EditorGUILayout.LabelField(prefix + "● " + typeName);

            // 递归显示子节点
            var childField = node.GetType().GetField("m_Children", BindingFlags.Instance | BindingFlags.NonPublic);
            if (childField != null)
            {
                var list = childField.GetValue(node) as System.Collections.IList;
                if (list != null)
                {
                    foreach (var c in list) DrawNode(c, depth + 1);
                }
            }
            var childField2 = node.GetType().GetField("Child", BindingFlags.Instance | BindingFlags.Public);
            if (childField2 != null)
            {
                var c = childField2.GetValue(node);
                if (c != null) DrawNode(c, depth + 1);
            }
        }
    }
}
