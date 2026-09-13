//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.CodeGen
{
    /// <summary>
    /// 代码生成总控面板：列出所有 <see cref="ICodeGenerator"/>，支持单独运行 / 一键全部 / 查看上次报告。
    /// 菜单：EjoyFramework/Core/CodeGen/Code Generation Window。
    /// </summary>
    public sealed class CodeGenWindow : EditorWindow
    {
        private readonly Dictionary<string, CodeGenResult> m_LastResults = new Dictionary<string, CodeGenResult>();
        private readonly HashSet<string> m_Expanded = new HashSet<string>();
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/CodeGen/Code Generation Window", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<CodeGenWindow>("Code Generation");
            window.minSize = new Vector2(420f, 320f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("代码生成器", EditorStyles.boldLabel);

            var generators = CodeGenHub.Generators;
            if (generators.Count == 0)
            {
                EditorGUILayout.HelpBox("未发现任何 ICodeGenerator 实现。", MessageType.Info);
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < generators.Count; i++)
            {
                DrawGenerator(generators[i]);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "外部生成器（源驱动，不在此统一管线）：\n" +
                "• Config：EjoyFramework/Core/Config/Export from CSV\n" +
                "• UIFormId：EjoyFramework/Core/UI/Generate UIFormId",
                MessageType.None);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Generate All", EditorStyles.toolbarButton, GUILayout.Width(100f)))
            {
                CodeGenResult all = CodeGenHub.RunAll();
                // 拆分回各生成器无意义，这里只刷新各自单独运行的展示——直接逐个重跑代价低且确定
                RefreshAllResults();
                m_LastResults["__all__"] = all;
            }
            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                CodeGenHub.Refresh();
                m_LastResults.Clear();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void RefreshAllResults()
        {
            foreach (ICodeGenerator g in CodeGenHub.Generators)
            {
                m_LastResults[g.Id] = g.Run();
            }
            AssetDatabase.Refresh();
        }

        private void DrawGenerator(ICodeGenerator generator)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(generator.DisplayName, EditorStyles.boldLabel);
            if (GUILayout.Button("Run", GUILayout.Width(60f)))
            {
                m_LastResults[generator.Id] = CodeGenHub.RunOne(generator);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(generator.Description))
            {
                EditorGUILayout.LabelField(generator.Description, EditorStyles.wordWrappedMiniLabel);
            }

            if (m_LastResults.TryGetValue(generator.Id, out CodeGenResult result))
            {
                EditorGUILayout.LabelField("上次：" + result, EditorStyles.miniLabel);
                if (result.Messages != null && result.Messages.Length > 0)
                {
                    bool expanded = m_Expanded.Contains(generator.Id);
                    bool newExpanded = EditorGUILayout.Foldout(expanded, "消息 (" + result.Messages.Length + ")");
                    if (newExpanded != expanded)
                    {
                        if (newExpanded) m_Expanded.Add(generator.Id);
                        else m_Expanded.Remove(generator.Id);
                    }
                    if (newExpanded)
                    {
                        EditorGUI.indentLevel++;
                        for (int i = 0; i < result.Messages.Length; i++)
                            EditorGUILayout.LabelField(result.Messages[i], EditorStyles.wordWrappedMiniLabel);
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }
    }
}
