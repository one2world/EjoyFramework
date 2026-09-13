//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using EjoyFramework.Core.Patch;
using EjoyFramework.Core.Resource;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Patch
{
    /// <summary>
    /// Manifest 对比查看器：本地 manifest.json vs 远程 manifest.json，显示差量 bundle 列表。
    /// 业务在打补丁前先用本工具确认差量是否符合预期。
    /// </summary>
    public sealed class PatchManifestViewer : EditorWindow
    {
        private string m_LocalPath = "";
        private string m_RemotePath = "";
        private AssetManifest m_Local;
        private AssetManifest m_Remote;
        private PatchCheckResult m_Result;
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/Patch/Manifest Diff Viewer")]
        public static void Open() => GetWindow<PatchManifestViewer>("Manifest Diff").Show();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Local manifest:", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                m_LocalPath = EditorGUILayout.TextField(m_LocalPath);
                if (GUILayout.Button("...", GUILayout.Width(40)))
                {
                    var p = EditorUtility.OpenFilePanel("Local manifest", "", "json");
                    if (!string.IsNullOrEmpty(p)) m_LocalPath = p;
                }
            }
            EditorGUILayout.LabelField("Remote manifest:", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                m_RemotePath = EditorGUILayout.TextField(m_RemotePath);
                if (GUILayout.Button("...", GUILayout.Width(40)))
                {
                    var p = EditorUtility.OpenFilePanel("Remote manifest", "", "json");
                    if (!string.IsNullOrEmpty(p)) m_RemotePath = p;
                }
            }

            if (GUILayout.Button("Compare"))
            {
                m_Local = Load(m_LocalPath);
                m_Remote = Load(m_RemotePath);
                m_Result = ManifestComparer.Compare(m_Local, m_Remote);
            }

            if (m_Result == null) return;
            EditorGUILayout.LabelField("Local v" + m_Result.LocalVersion + " → Remote v" + m_Result.RemoteVersion,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Has update: " + m_Result.HasUpdate);
            EditorGUILayout.LabelField("Pending bundles: " + m_Result.PendingBundles.Length +
                "  Total size: " + (m_Result.TotalSizeBytes / 1024f / 1024f).ToString("F2") + " MB");

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var b in m_Result.PendingBundles)
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(b.BundleName, GUILayout.MinWidth(200));
                    EditorGUILayout.LabelField(b.SizeBytes + " bytes", GUILayout.Width(120));
                    EditorGUILayout.LabelField(b.Md5 ?? "(no md5)", GUILayout.Width(200));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static AssetManifest Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<AssetManifest>(json);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("Failed to load manifest: " + ex);
                return null;
            }
        }
    }
}
