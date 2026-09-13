//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.AssetPipeline
{
    /// <summary>
    /// 静态资源检查总控面板：选配置 → Run → 按严重度过滤的可滚动结果（点击定位资产）。
    /// 菜单：EjoyFramework/Core/Resource/Asset Checker。
    /// </summary>
    public sealed class AssetCheckWindow : EditorWindow
    {
        private AssetCheckConfig m_Config;
        private AssetCheckReport m_Report;
        private Vector2 m_Scroll;
        private bool m_ShowError = true, m_ShowWarning = true, m_ShowInfo = true;

        [MenuItem("EjoyFramework/Core/Resource/Asset Checker", priority = 50)]
        public static void Open()
        {
            var w = GetWindow<AssetCheckWindow>(false, "Asset Checker", true);
            w.minSize = new Vector2(560f, 480f);
        }

        private void OnEnable()
        {
            m_Config = AssetCheckConfig.FindExisting();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("静态资源检查", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            m_Config = (AssetCheckConfig)EditorGUILayout.ObjectField("Config", m_Config, typeof(AssetCheckConfig), false);
            if (GUILayout.Button("Get/Create", GUILayout.Width(90f)))
            {
                m_Config = AssetCheckConfig.GetOrCreate();
                Selection.activeObject = m_Config;
            }
            EditorGUILayout.EndHorizontal();

            if (m_Config == null)
            {
                EditorGUILayout.HelpBox("拖入或 Get/Create 一个 AssetCheckConfig。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run Check", GUILayout.Height(28f)))
            {
                m_Report = AssetChecker.RunAll(m_Config);
                AssetCheckCi.LogReport(m_Report);
            }
            EditorGUILayout.EndHorizontal();

            if (m_Report == null)
            {
                EditorGUILayout.HelpBox("点击 Run Check 运行检查。", MessageType.None);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(m_Report.ToString(), EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            m_ShowError = GUILayout.Toggle(m_ShowError, "Error (" + m_Report.ErrorCount + ")", "Button");
            m_ShowWarning = GUILayout.Toggle(m_ShowWarning, "Warning (" + m_Report.WarningCount + ")", "Button");
            m_ShowInfo = GUILayout.Toggle(m_ShowInfo, "Info (" + m_Report.InfoCount + ")", "Button");
            EditorGUILayout.EndHorizontal();

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (AssetIssue issue in m_Report.Issues)
            {
                if (!PassFilter(issue.Severity)) continue;
                DrawIssue(issue);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool PassFilter(AssetCheckSeverity s)
        {
            return (s == AssetCheckSeverity.Error && m_ShowError)
                || (s == AssetCheckSeverity.Warning && m_ShowWarning)
                || (s == AssetCheckSeverity.Info && m_ShowInfo);
        }

        private void DrawIssue(AssetIssue issue)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            Color prev = GUI.color;
            GUI.color = issue.Severity == AssetCheckSeverity.Error ? new Color(1f, 0.5f, 0.5f)
                      : issue.Severity == AssetCheckSeverity.Warning ? new Color(1f, 0.9f, 0.5f)
                      : Color.white;
            EditorGUILayout.LabelField("[" + issue.Severity + "][" + issue.RuleId + "]", GUILayout.Width(150f));
            GUI.color = prev;

            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrEmpty(issue.AssetPath) && GUILayout.Button("Ping", GUILayout.Width(48f)))
            {
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(issue.AssetPath);
                if (asset != null) { EditorGUIUtility.PingObject(asset); Selection.activeObject = asset; }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
