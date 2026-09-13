//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Fsm;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Fsm
{
    /// <summary>
    /// Runtime FSM Inspector：列出所有已注册 FSM 实例 + 当前 state。
    /// </summary>
    public sealed class FsmInspector : EditorWindow
    {
        private Vector2 m_Scroll;

        [MenuItem("EjoyFramework/Core/Fsm/Runtime Inspector")]
        public static void Open() => GetWindow<FsmInspector>("FSM Inspector").Show();

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect FSMs.", MessageType.Info);
                return;
            }
            if (!Framework.HasModule<IFsmManager>())
            {
                EditorGUILayout.HelpBox("FsmManager not registered.", MessageType.Warning);
                return;
            }
            var mgr = Framework.GetModule<IFsmManager>();
            var fsms = mgr.GetAllFsms();
            EditorGUILayout.LabelField("Active FSMs: " + fsms.Length, EditorStyles.boldLabel);

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var fsm in fsms)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField("Name: " + fsm.Name, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Owner type: " + (fsm.OwnerType?.Name ?? "?"));
                    EditorGUILayout.LabelField("Is running: " + fsm.IsRunning);
                    EditorGUILayout.LabelField("Is destroyed: " + fsm.IsDestroyed);
                    EditorGUILayout.LabelField("Current state: " + (fsm.CurrentStateName ?? "(none)"));
                    EditorGUILayout.LabelField("State count: " + fsm.FsmStateCount);
                    EditorGUILayout.LabelField("Current state time: " + fsm.CurrentStateTime.ToString("F2") + "s");
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
