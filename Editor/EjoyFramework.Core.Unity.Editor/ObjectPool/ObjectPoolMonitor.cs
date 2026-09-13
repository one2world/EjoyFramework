//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.ObjectPool;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.ObjectPool
{
    /// <summary>
    /// Runtime ObjectPool 监视器：PlayMode 下显示所有池的 Count/CanReleaseCount/AutoReleaseInterval/Capacity。
    /// </summary>
    public sealed class ObjectPoolMonitor : EditorWindow
    {
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/ObjectPool/Runtime Monitor")]
        public static void Open() => GetWindow<ObjectPoolMonitor>("Pool Monitor").Show();

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect object pools.", MessageType.Info);
                return;
            }
            if (!Framework.HasModule<IObjectPoolManager>())
            {
                EditorGUILayout.HelpBox("ObjectPoolManager not registered yet.", MessageType.Warning);
                return;
            }

            var mgr = Framework.GetModule<IObjectPoolManager>();
            var pools = mgr.GetAllObjectPools();
            EditorGUILayout.LabelField("Pools: " + pools.Length, EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope("box"))
            {
                EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.MinWidth(180));
                EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.MinWidth(180));
                EditorGUILayout.LabelField("Count", EditorStyles.boldLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("Releasable", EditorStyles.boldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("Capacity", EditorStyles.boldLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("AutoRelease", EditorStyles.boldLabel, GUILayout.Width(90));
            }
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var p in pools)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(p.Name, GUILayout.MinWidth(180));
                    EditorGUILayout.LabelField(p.ObjectType?.Name ?? "?", GUILayout.MinWidth(180));
                    EditorGUILayout.LabelField(p.Count.ToString(), GUILayout.Width(60));
                    EditorGUILayout.LabelField(p.CanReleaseCount.ToString(), GUILayout.Width(80));
                    EditorGUILayout.LabelField(p.Capacity.ToString(), GUILayout.Width(70));
                    EditorGUILayout.LabelField(p.AutoReleaseInterval.ToString("F1") + "s", GUILayout.Width(90));
                }
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Release all unused"))
            {
                mgr.ReleaseAllUnused();
            }
        }
    }
}
