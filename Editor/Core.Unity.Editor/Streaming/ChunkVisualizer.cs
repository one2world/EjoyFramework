//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Streaming;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Streaming
{
    /// <summary>
    /// 世界分区流送可视化（Scene 视图叠加）。
    ///
    /// 编辑态：按 CellSize 画观察点周围的单元网格与加载/卸载半径，用于规划分区尺寸。
    /// 播放态：从运行中的 <see cref="IWorldStreamingManager"/> 读取指定层的单元状态着色
    /// （绿 = Loaded，青 = Loading/Queued，黄 = Unloading/Cancelling，灰 = Unloaded，未注册不画）。
    /// </summary>
    public sealed class ChunkVisualizer : EditorWindow
    {
        private bool m_Enabled = true;
        private float m_CellSize = 64f;
        private float m_LoadRadius = 200f;
        private float m_UnloadRadius = 260f;
        private int m_Layer;
        private int m_ViewCells = 8;
        private Vector3 m_ObserverPos;
        private Transform m_ObserverTransform;

        [MenuItem("EjoyFramework/Core/Streaming/World Streaming Visualizer")]
        public static void Open()
        {
            GetWindow<ChunkVisualizer>("World Streaming").Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            m_Enabled = EditorGUILayout.Toggle("Draw in Scene View", m_Enabled);
            m_ObserverTransform = (Transform)EditorGUILayout.ObjectField("Observer (optional)", m_ObserverTransform, typeof(Transform), true);
            if (m_ObserverTransform == null) m_ObserverPos = EditorGUILayout.Vector3Field("Observer position", m_ObserverPos);
            m_Layer = EditorGUILayout.IntSlider("Layer", m_Layer, 0, 15);
            m_ViewCells = EditorGUILayout.IntSlider("View cells (radius)", m_ViewCells, 1, 32);

            IWorldStreamingManager live = GetLiveManager();
            if (live != null)
            {
                EditorGUILayout.HelpBox("Play mode: reading live manager state.", MessageType.Info);
                EditorGUILayout.LabelField("CellSize", live.CellSize.ToString("F1"));
                EditorGUILayout.LabelField("Registered / Loaded / Loading / Queued",
                    live.RegisteredCellCount + " / " + live.LoadedCellCount + " / " + live.LoadingCellCount + " / " + live.QueuedLoadCount);
                EditorGUILayout.LabelField("Totals started / completed / cancelled / failed / unloads",
                    live.TotalLoadsStarted + " / " + live.TotalLoadsCompleted + " / " + live.TotalLoadsCancelled + " / " + live.TotalLoadFailures + " / " + live.TotalUnloads);
            }
            else
            {
                m_CellSize = Mathf.Max(1f, EditorGUILayout.FloatField("Cell Size (preview)", m_CellSize));
                m_LoadRadius = EditorGUILayout.FloatField("Load Radius (preview)", m_LoadRadius);
                m_UnloadRadius = EditorGUILayout.FloatField("Unload Radius (preview)", m_UnloadRadius);
            }

            if (GUI.changed) SceneView.RepaintAll();
        }

        private static IWorldStreamingManager GetLiveManager()
        {
            if (!Application.isPlaying) return null;
            return Framework.HasModule<IWorldStreamingManager>() ? Framework.GetModule<IWorldStreamingManager>() : null;
        }

        private void OnSceneGUI(SceneView sv)
        {
            if (!m_Enabled) return;
            Vector3 observer = m_ObserverTransform != null ? m_ObserverTransform.position : m_ObserverPos;
            IWorldStreamingManager live = GetLiveManager();
            float cellSize = live != null ? live.CellSize : m_CellSize;

            Handles.color = Color.cyan;
            Handles.DrawWireDisc(observer, Vector3.up, 1f);
            if (live == null)
            {
                Handles.color = new Color(0f, 1f, 1f, 0.25f);
                Handles.DrawWireDisc(observer, Vector3.up, m_LoadRadius);
                Handles.color = new Color(1f, 1f, 0f, 0.25f);
                Handles.DrawWireDisc(observer, Vector3.up, m_UnloadRadius);
            }

            int cx0 = Mathf.FloorToInt(observer.x / cellSize);
            int cz0 = Mathf.FloorToInt(observer.z / cellSize);
            for (int cx = cx0 - m_ViewCells; cx <= cx0 + m_ViewCells; cx++)
            {
                for (int cz = cz0 - m_ViewCells; cz <= cz0 + m_ViewCells; cz++)
                {
                    Color color = new Color(1f, 1f, 1f, 0.08f);
                    if (live != null)
                    {
                        int id = live.FindCell(m_Layer, cx, cz);
                        if (id < 0) continue;
                        switch (live.GetCellState(id))
                        {
                            case StreamingCellState.Loaded: color = new Color(0f, 1f, 0f, 0.5f); break;
                            case StreamingCellState.Loading:
                            case StreamingCellState.Queued: color = new Color(0f, 1f, 1f, 0.5f); break;
                            case StreamingCellState.Unloading:
                            case StreamingCellState.Cancelling: color = new Color(1f, 1f, 0f, 0.5f); break;
                            default: color = new Color(0.5f, 0.5f, 0.5f, 0.25f); break;
                        }
                    }

                    Handles.color = color;
                    Vector3 min = new Vector3(cx * cellSize, observer.y, cz * cellSize);
                    Vector3 size = new Vector3(cellSize, 0f, cellSize);
                    Handles.DrawWireCube(min + size * 0.5f, size);
                }
            }
        }
    }
}
