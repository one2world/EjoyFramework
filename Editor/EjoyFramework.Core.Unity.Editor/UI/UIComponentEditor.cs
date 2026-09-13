//------------------------------------------------------------
// EjoyGame Framework - Editor Tools
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.Unity;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor
{
    /// <summary>
    /// UIComponent 自定义 Inspector：
    ///   - 折叠各 Header
    ///   - 展示按 group 分组后的 form 列表
    ///   - 提供"生成 UIFormId" / "重复 ID 检查"按钮
    /// </summary>
    [CustomEditor(typeof(UIComponent))]
    public sealed class UIComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Generate UIFormId.cs"))
            {
                UIFormIdGenerator.Generate();
            }
            if (GUILayout.Button("Check Duplicate IDs"))
            {
                CheckDuplicateIds();
            }
        }

        private void CheckDuplicateIds()
        {
            var so = serializedObject;
            var defsProp = so.FindProperty("m_UIFormDefs");
            if (defsProp == null || !defsProp.isArray) return;

            var seen = new System.Collections.Generic.Dictionary<int, string>();
            int dups = 0;
            for (int i = 0; i < defsProp.arraySize; i++)
            {
                var elem = defsProp.GetArrayElementAtIndex(i);
                int id = elem.FindPropertyRelative("Id").intValue;
                string asset = elem.FindPropertyRelative("AssetPath").stringValue;
                if (id == 0) continue;
                if (seen.TryGetValue(id, out string existingAsset))
                {
                    dups++;
                    Debug.LogError(string.Format("Duplicate UIFormDef id={0}: '{1}' vs '{2}'", id, existingAsset, asset), target);
                }
                else
                {
                    seen[id] = asset;
                }
            }
            if (dups == 0)
            {
                EditorUtility.DisplayDialog("UI Form Def Check", "All IDs are unique. ✓", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("UI Form Def Check",
                    string.Format("Found {0} duplicate IDs. See Console.", dups), "OK");
            }
        }
    }
}
