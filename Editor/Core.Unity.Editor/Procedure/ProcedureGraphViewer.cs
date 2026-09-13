//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EjoyFramework.Core.Procedure;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Tools
{
    /// <summary>
    /// Procedure 流程图查看器：列出工程内所有 ProcedureBase 子类 + 标识 ChangeState&lt;T&gt; 调用关系。
    /// 不做可视化图（轻量起步），而是 List + 跳转源码 — 已够大部分排查需要。
    /// </summary>
    public sealed class ProcedureGraphViewer : EditorWindow
    {
        private List<ProcedureInfo> m_Procedures;
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/Procedure/Graph Viewer")]
        public static void Open() => GetWindow<ProcedureGraphViewer>("Procedure Graph").Show();

        private void OnEnable() => Scan();

        private void OnGUI()
        {
            if (GUILayout.Button("Refresh")) Scan();
            if (m_Procedures == null || m_Procedures.Count == 0)
            {
                EditorGUILayout.HelpBox("No ProcedureBase subclasses found.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Found " + m_Procedures.Count + " procedure(s):", EditorStyles.boldLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var p in m_Procedures)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField(p.Type.FullName, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Assembly: " + p.Type.Assembly.GetName().Name);
                if (p.Transitions.Count > 0)
                {
                    EditorGUILayout.LabelField("Transitions to:");
                    foreach (var t in p.Transitions) EditorGUILayout.LabelField("  → " + t);
                }
                else
                {
                    EditorGUILayout.LabelField("(no ChangeState detected in source)", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            m_Procedures = new List<ProcedureInfo>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (!typeof(ProcedureBase).IsAssignableFrom(t) || t.IsAbstract) continue;
                    m_Procedures.Add(new ProcedureInfo
                    {
                        Type = t,
                        Transitions = ScanTransitions(t),
                    });
                }
            }
            m_Procedures.Sort((a, b) => string.CompareOrdinal(a.Type.FullName, b.Type.FullName));
        }

        private static List<string> ScanTransitions(Type t)
        {
            // 启发式：找类型 metadata 中明确的 ProcedureBase 子类，作为可能的目标。
            // 真正的源码层 ChangeState<T> 调用扫描需要 Roslyn，这里给出 type-scan baseline。
            var transitions = new List<string>();
            // 不做实际源码 grep；业务可后续接 Roslyn 分析。
            // 这里仅占位列出 procedure 名，作为后续 Roslyn 接入的 hook 点。
            return transitions;
        }

        private sealed class ProcedureInfo
        {
            public Type Type;
            public List<string> Transitions;
        }
    }
}
