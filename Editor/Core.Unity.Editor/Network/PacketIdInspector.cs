//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.Network;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor.Network
{
    /// <summary>
    /// Packet ID 注册查看器：扫描所有 Packet 子类，列出 PacketId / FullName / Assembly。
    /// 检测 Id 冲突（同 Id 多个 packet）。
    /// </summary>
    public sealed class PacketIdInspector : EditorWindow
    {
        private List<Entry> m_Entries;
        private List<string> m_Conflicts;
        private Vector2 m_Scroll;
        private string m_FilterText = "";

        [MenuItem("EjoyFramework/Core/Network/Packet ID Inspector")]
        public static void Open() => GetWindow<PacketIdInspector>("Packet IDs").Show();

        private void OnEnable() => Scan();

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Filter:", GUILayout.Width(50));
                m_FilterText = EditorGUILayout.TextField(m_FilterText);
                if (GUILayout.Button("Rescan", GUILayout.Width(80))) Scan();
            }

            EditorGUILayout.LabelField("Packets: " + (m_Entries?.Count ?? 0) +
                "  Conflicts: " + (m_Conflicts?.Count ?? 0), EditorStyles.boldLabel);

            if (m_Conflicts != null && m_Conflicts.Count > 0)
            {
                EditorGUILayout.HelpBox("ID conflicts detected:\n" + string.Join("\n", m_Conflicts), MessageType.Error);
            }

            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                EditorGUILayout.LabelField("ID", EditorStyles.boldLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.MinWidth(300));
                EditorGUILayout.LabelField("Assembly", EditorStyles.boldLabel, GUILayout.MinWidth(180));
            }
            if (m_Entries != null)
            {
                foreach (var e in m_Entries)
                {
                    if (!string.IsNullOrEmpty(m_FilterText) && !e.FullName.ToLower().Contains(m_FilterText.ToLower())) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(e.Id.ToString(), GUILayout.Width(80));
                        EditorGUILayout.LabelField(e.FullName, GUILayout.MinWidth(300));
                        EditorGUILayout.LabelField(e.AssemblyName, GUILayout.MinWidth(180));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            m_Entries = new List<Entry>();
            m_Conflicts = new List<string>();
            var byId = new Dictionary<int, List<string>>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (!typeof(Packet).IsAssignableFrom(t) || t.IsAbstract) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    int id;
                    try
                    {
                        var inst = (Packet)Activator.CreateInstance(t);
                        id = inst.Id;
                    }
                    catch { continue; }
                    var e = new Entry { Id = id, FullName = t.FullName, AssemblyName = asm.GetName().Name };
                    m_Entries.Add(e);
                    if (!byId.TryGetValue(id, out var list)) byId[id] = list = new List<string>();
                    list.Add(t.FullName);
                }
            }

            foreach (var kv in byId)
            {
                if (kv.Value.Count > 1)
                    m_Conflicts.Add("Id " + kv.Key + " used by: " + string.Join(", ", kv.Value));
            }
            m_Entries.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private sealed class Entry
        {
            public int Id;
            public string FullName;
            public string AssemblyName;
        }
    }
}
