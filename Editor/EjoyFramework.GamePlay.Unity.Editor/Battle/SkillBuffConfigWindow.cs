//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using EjoyFramework.GamePlay.Battle;
using UnityEditor;
using UnityEngine;

using EjoyFramework.Core;
namespace EjoyFramework.GamePlay.Unity.Editor.Battle
{
    /// <summary>
    /// Skill / Buff 配置编辑器：业务侧用 ScriptableObject 集中配置 SkillDef + BuffDef，本工具便捷增/删/改。
    /// 数据存为 Assets/GameMain/Configs/Battle/SkillSet.asset / BuffSet.asset。
    /// </summary>
    public sealed class SkillBuffConfigWindow : EditorWindow
    {
        private SkillSetAsset m_SkillSet;
        private BuffSetAsset m_BuffSet;
        private Vector2 m_Scroll;
        private int m_Tab;

        [MenuItem("EjoyFramework/GamePlay/Battle/Skill & Buff Config")]
        public static void Open() => GetWindow<SkillBuffConfigWindow>("Skill / Buff").Show();

        private void OnEnable()
        {
            m_SkillSet = LoadOrCreate<SkillSetAsset>("Assets/GameMain/Configs/Battle/SkillSet.asset");
            m_BuffSet = LoadOrCreate<BuffSetAsset>("Assets/GameMain/Configs/Battle/BuffSet.asset");
        }

        private void OnGUI()
        {
            m_Tab = GUILayout.Toolbar(m_Tab, new[] { "Skills (" + m_SkillSet.Skills.Count + ")", "Buffs (" + m_BuffSet.Buffs.Count + ")" });
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            if (m_Tab == 0) DrawSkills(); else DrawBuffs();
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save")) { EditorUtility.SetDirty(m_SkillSet); EditorUtility.SetDirty(m_BuffSet); AssetDatabase.SaveAssets(); }
                if (GUILayout.Button("Reveal SkillSet")) EditorGUIUtility.PingObject(m_SkillSet);
                if (GUILayout.Button("Reveal BuffSet")) EditorGUIUtility.PingObject(m_BuffSet);
            }
        }

        private void DrawSkills()
        {
            for (int i = 0; i < m_SkillSet.Skills.Count; i++)
            {
                var s = m_SkillSet.Skills[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    s.SkillId = EditorGUILayout.IntField("Id", s.SkillId);
                    s.Name = EditorGUILayout.TextField("Name", s.Name);
                    s.CooldownSeconds = EditorGUILayout.FloatField("Cooldown (s)", s.CooldownSeconds);
                    s.CastTimeSeconds = EditorGUILayout.FloatField("Cast time (s)", s.CastTimeSeconds);
                    s.BaseDamage = EditorGUILayout.IntField("Base damage", s.BaseDamage);
                    s.DamageScaling = EditorGUILayout.FloatField("Damage scaling × Atk", s.DamageScaling);
                    s.ManaCost = EditorGUILayout.IntField("Mana cost", s.ManaCost);
                    s.Targeting = (TargetingMode)EditorGUILayout.EnumPopup("Targeting", s.Targeting);
                    if (GUILayout.Button("Remove")) { m_SkillSet.Skills.RemoveAt(i); break; }
                }
            }
            if (GUILayout.Button("+ Add Skill")) m_SkillSet.Skills.Add(new SkillDef { SkillId = NextId(m_SkillSet), Name = "NewSkill" });
        }

        private void DrawBuffs()
        {
            for (int i = 0; i < m_BuffSet.Buffs.Count; i++)
            {
                var b = m_BuffSet.Buffs[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    b.BuffId = EditorGUILayout.IntField("Id", b.BuffId);
                    b.Name = EditorGUILayout.TextField("Name", b.Name);
                    b.Kind = (BuffKind)EditorGUILayout.EnumPopup("Kind", b.Kind);
                    b.DurationSeconds = EditorGUILayout.FloatField("Duration (s)", b.DurationSeconds);
                    b.TickIntervalSeconds = EditorGUILayout.FloatField("Tick interval (s)", b.TickIntervalSeconds);
                    b.TickAmount = EditorGUILayout.IntField("Tick amount", b.TickAmount);
                    b.Stacking = (StackRule)EditorGUILayout.EnumPopup("Stack rule", b.Stacking);
                    b.MaxStacks = EditorGUILayout.IntField("Max stacks", b.MaxStacks);
                    if (GUILayout.Button("Remove")) { m_BuffSet.Buffs.RemoveAt(i); break; }
                }
            }
            if (GUILayout.Button("+ Add Buff")) m_BuffSet.Buffs.Add(new BuffDef { BuffId = NextId(m_BuffSet), Name = "NewBuff" });
        }

        private static int NextId(SkillSetAsset s) { int n = 1; foreach (var d in s.Skills) n = System.Math.Max(n, d.SkillId + 1); return n; }
        private static int NextId(BuffSetAsset s) { int n = 1; foreach (var d in s.Buffs) n = System.Math.Max(n, d.BuffId + 1); return n; }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }

    /// <summary>容器 ScriptableObject。</summary>
    public sealed class SkillSetAsset : ScriptableObject { public List<SkillDef> Skills = new List<SkillDef>(); }
    public sealed class BuffSetAsset : ScriptableObject { public List<BuffDef> Buffs = new List<BuffDef>(); }
}
