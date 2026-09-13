//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.IO;
using EjoyFramework.Core.Resource;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Resource
{
    /// <summary>
    /// AB 打包窗口。EjoyFramework.Core → Resource → AssetBundle Builder
    /// </summary>
    public sealed class AssetBundleBuilderWindow : EditorWindow
    {
        private AssetBundleBuildConfig m_Config;
        private BuildTarget m_Target = BuildTarget.StandaloneWindows64;
        private Vector2 m_ReportScroll;
        private AssetManifest m_LastManifest;

        [MenuItem("EjoyFramework/Core/Resource/AssetBundle Builder")]
        public static void Open()
        {
            var w = GetWindow<AssetBundleBuilderWindow>(false, "AB Builder", true);
            w.minSize = new Vector2(520, 480);
        }

        private void OnEnable()
        {
            // 默认尝试用 ActiveBuildTarget
            m_Target = EditorUserBuildSettings.activeBuildTarget;
        }

        private void OnGUI()
        {
            GUILayout.Label("EjoyFramework.Core — AssetBundle Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            m_Config = (AssetBundleBuildConfig)EditorGUILayout.ObjectField(
                "Build Config", m_Config, typeof(AssetBundleBuildConfig), false);
            m_Target = (BuildTarget)EditorGUILayout.EnumPopup("Target Platform", m_Target);

            if (m_Config == null)
            {
                EditorGUILayout.HelpBox("请拖入一个 AssetBundleBuildConfig（右键 → Create → EjoyFramework.Core → AssetBundle Build Config）", MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUI.BeginDisabledGroup(m_Config == null);
            if (GUILayout.Button("Build", GUILayout.Height(32)))
            {
                DoBuild();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            if (GUILayout.Button("Open Output Folder"))
            {
                if (m_Config != null)
                {
                    string root = Path.IsPathRooted(m_Config.OutputDirectory)
                        ? m_Config.OutputDirectory
                        : Path.Combine(Directory.GetCurrentDirectory(), m_Config.OutputDirectory);
                    string dir = Path.Combine(root, m_Target.ToString());
                    if (Directory.Exists(dir)) EditorUtility.RevealInFinder(dir);
                    else Debug.LogWarning("Output dir not found: " + dir);
                }
            }

            if (GUILayout.Button("Clear StreamingAssets Bundles"))
            {
                string dir = Path.Combine(Application.streamingAssetsPath, "AssetBundles");
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    AssetDatabase.Refresh();
                    Debug.Log("Cleared: " + dir);
                }
            }

            DrawReport();
        }

        private void DoBuild()
        {
            try
            {
                m_LastManifest = AssetBundleBuilder.BuildAll(m_Config, m_Target);
                EditorUtility.DisplayDialog("AB Build", "Build complete.\n\nBundles: " + m_LastManifest.Bundles.Count + "\nAssets: " + m_LastManifest.Assets.Count, "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
                EditorUtility.DisplayDialog("AB Build", "Build failed:\n" + ex.Message, "OK");
            }
        }

        private void DrawReport()
        {
            if (m_LastManifest == null) return;
            EditorGUILayout.Space();
            GUILayout.Label("Last Build Report", EditorStyles.boldLabel);
            GUILayout.Label("Bundles: " + m_LastManifest.Bundles.Count + "  Assets: " + m_LastManifest.Assets.Count
                            + "  Platform: " + m_LastManifest.Platform);

            m_ReportScroll = EditorGUILayout.BeginScrollView(m_ReportScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < m_LastManifest.Bundles.Count; i++)
            {
                var b = m_LastManifest.Bundles[i];
                EditorGUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(b.Name, GUILayout.Width(220));
                GUILayout.Label(FormatBytes(b.Size), GUILayout.Width(80));
                GUILayout.Label((b.AssetNames != null ? b.AssetNames.Count : 0) + " assets", GUILayout.Width(70));
                GUILayout.Label((b.Dependencies != null ? b.Dependencies.Count : 0) + " deps");
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("F2") + " MB";
        }
    }
}
