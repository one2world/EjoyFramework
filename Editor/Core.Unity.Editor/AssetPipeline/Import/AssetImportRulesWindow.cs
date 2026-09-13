//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 资源导入规则总控面板：编辑规则（基于默认 Inspector 序列化）、即时测试某路径命中哪条规则、批量套用。
    /// 菜单：EjoyFramework/Core/Resource/Import Rules。
    /// </summary>
    public sealed class AssetImportRulesWindow : EditorWindow
    {
        private AssetImportRuleConfig m_Config;
        private UnityEditor.Editor m_ConfigEditor;
        private Vector2 m_Scroll;
        private string m_TestPath = "Assets/GameMain/UI/Textures/example.png";
        private AssetImportKind m_TestKind = AssetImportKind.Texture;
        private string m_TestResult = string.Empty;

        [MenuItem("EjoyFramework/Core/Resource/Import Rules", priority = 55)]
        public static void Open()
        {
            var w = GetWindow<AssetImportRulesWindow>(false, "Import Rules", true);
            w.minSize = new Vector2(460f, 480f);
        }

        private void OnEnable()
        {
            m_Config = AssetImportRuleConfig.FindExisting();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("资源导入规则", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            m_Config = (AssetImportRuleConfig)EditorGUILayout.ObjectField(
                "Config", m_Config, typeof(AssetImportRuleConfig), false);
            if (GUILayout.Button("Get/Create", GUILayout.Width(90f)))
            {
                m_Config = AssetImportRuleConfig.GetOrCreate();
                Selection.activeObject = m_Config;
            }
            EditorGUILayout.EndHorizontal();

            if (m_Config == null)
            {
                EditorGUILayout.HelpBox("拖入或 Get/Create 一个 AssetImportRuleConfig。", MessageType.Info);
                return;
            }

            DrawTestPath();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to Selection")) AssetImportRulesApplier.ApplyToSelectionMenu();
            if (GUILayout.Button("Apply to All")) AssetImportRulesApplier.ApplyToAllMenu();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("规则编辑", EditorStyles.boldLabel);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_ConfigEditor == null || m_ConfigEditor.target != m_Config)
                m_ConfigEditor = UnityEditor.Editor.CreateEditor(m_Config);
            m_ConfigEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTestPath()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("规则测试（输入资产路径 → 显示命中规则）", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            m_TestPath = EditorGUILayout.TextField("Path", m_TestPath);
            m_TestKind = (AssetImportKind)EditorGUILayout.EnumPopup(m_TestKind, GUILayout.Width(90f));
            if (GUILayout.Button("Test", GUILayout.Width(60f)))
            {
                var resolver = new AssetImportRuleResolver(m_Config);
                if (resolver.IsExcluded(m_TestPath))
                    m_TestResult = "→ 被 ExcludePatterns 排除";
                else
                {
                    AssetImportRule r = resolver.Resolve(m_TestPath, m_TestKind);
                    m_TestResult = r != null ? "→ 命中规则：" + r.Name + "（priority " + r.Priority + "）"
                                             : "→ 未命中任何规则";
                }
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(m_TestResult))
                EditorGUILayout.HelpBox(m_TestResult, MessageType.None);
        }

        private void OnDisable()
        {
            if (m_ConfigEditor != null) DestroyImmediate(m_ConfigEditor);
        }
    }
}
