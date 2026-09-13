//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.Streaming;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Streaming
{
    /// <summary>
    /// Scene 视图内绘制 chunk 边界 + 玩家位置 + LoadRadius/UnloadRadius。
    /// 业务通过 ChunkVisualizationSource 实现告诉本工具：当前 player 在哪 / 哪些 chunk 注册了。
    /// </summary>
    public sealed class ChunkVisualizer : EditorWindow
    {
        private static ChunkVisualizer s_Instance;

        private bool m_Enabled = true;
        private float m_LoadRadius = 100f;
        private float m_UnloadRadius = 130f;
        private List<ChunkInfo> m_Chunks = new List<ChunkInfo>();
        private Vector3 m_PlayerPos;

        [MenuItem("EjoyFramework/Core/Streaming/Chunk Visualizer")]
        public static void Open() => GetWindow<ChunkVisualizer>("Chunk Visualizer").Show();

        private void OnEnable()
        {
            s_Instance = this;
            SceneView.duringSceneGui += OnSceneGUI;
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            s_Instance = null;
        }

        private void OnGUI()
        {
            m_Enabled = EditorGUILayout.Toggle("Draw in Scene View", m_Enabled);
            m_LoadRadius = EditorGUILayout.FloatField("Load Radius", m_LoadRadius);
            m_UnloadRadius = EditorGUILayout.FloatField("Unload Radius", m_UnloadRadius);
            EditorGUILayout.Vector3Field("Player position", m_PlayerPos);

            EditorGUILayout.LabelField("Registered chunks: " + m_Chunks.Count);
            if (GUILayout.Button("Auto-detect chunks from scene (ChunkAuthoring components)"))
            {
                AutoDetectFromScene();
            }
            if (GUILayout.Button("Clear chunks")) { m_Chunks.Clear(); SceneView.RepaintAll(); }
            EditorGUILayout.HelpBox(
                "Chunks can also be populated programmatically via ChunkVisualizer.AddChunk(...) for editor playtests.",
                MessageType.Info);
        }

        private void OnSceneGUI(SceneView sv)
        {
            if (!m_Enabled) return;

            // 玩家
            Handles.color = Color.cyan;
            Handles.DrawWireDisc(m_PlayerPos, Vector3.up, 1f);
            Handles.color = new Color(0, 1, 1, 0.15f);
            Handles.DrawWireDisc(m_PlayerPos, Vector3.up, m_LoadRadius);
            Handles.color = new Color(1, 1, 0, 0.15f);
            Handles.DrawWireDisc(m_PlayerPos, Vector3.up, m_UnloadRadius);

            // chunk
            foreach (var c in m_Chunks)
            {
                var center = new Vector3(c.Center.X, c.Center.Y, c.Center.Z);
                float r = c.Radius > 0 ? c.Radius : 5f;
                float dist = Vector3.Distance(center, m_PlayerPos);
                Color col;
                if (dist - r <= m_LoadRadius) col = Color.green;
                else if (dist - r <= m_UnloadRadius) col = Color.yellow;
                else col = Color.gray;
                Handles.color = col;
                Handles.DrawWireDisc(center, Vector3.up, r);
                Handles.Label(center + Vector3.up * r, c.ChunkId);
            }
        }

        private void AutoDetectFromScene()
        {
            m_Chunks.Clear();
            var authors = UnityEngine.Object.FindObjectsByType<ChunkAuthoring>(FindObjectsSortMode.None);
            foreach (var a in authors)
            {
                m_Chunks.Add(new ChunkInfo
                {
                    ChunkId = string.IsNullOrEmpty(a.ChunkId) ? a.gameObject.name : a.ChunkId,
                    Center = new Vector3Lite(a.transform.position.x, a.transform.position.y, a.transform.position.z),
                    Radius = a.Radius,
                });
            }
            SceneView.RepaintAll();
        }

        public static void AddChunk(ChunkInfo info)
        {
            if (s_Instance != null) { s_Instance.m_Chunks.Add(info); SceneView.RepaintAll(); }
        }
    }

    /// <summary>业务侧场景内 chunk 标记 component；放在场景 GameObject 上让 ChunkVisualizer 自动发现。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/ChunkAuthoring")]
    public sealed class ChunkAuthoring : MonoBehaviour
    {
        public string ChunkId;
        public float Radius = 10f;
    }
}
