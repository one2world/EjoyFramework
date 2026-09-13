//------------------------------------------------------------
// EjoyGame Framework - Editor Tools
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using System.Text;
using EjoyFramework.Core.Unity;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor
{
    /// <summary>
    /// 从场景内 UIComponent 的 UIFormDefConfig 数组生成强类型 UIFormId 静态类。
    /// 解决"业务调 OpenUIForm 时硬编码字符串"的问题：
    ///
    ///   GameEntry.UI.Open(UIFormId.MainMenu);
    ///
    /// Inspector 在 UIComponent 上配置 (Id=1001, AssetPath="UI/MainMenu", GroupName="Default", ...) 后，
    /// 菜单 → EjoyFramework.Core → UI → Generate UIFormId 一键生成 Assets/GameMain/Scripts/UI/UIFormId.cs。
    /// </summary>
    public static class UIFormIdGenerator
    {
        private const string DefaultOutputPath = "Assets/GameMain/Scripts/UI/UIFormId.cs";
        private const string DefaultNamespace = "EjoyGame";

        [MenuItem("EjoyFramework/Core/UI/Generate UIFormId")]
        public static void Generate()
        {
            UIComponent[] uiComponents = Object.FindObjectsByType<UIComponent>(FindObjectsSortMode.None);
            if (uiComponents == null || uiComponents.Length == 0)
            {
                EditorUtility.DisplayDialog("Generate UIFormId",
                    "No UIComponent found in any open scene.\n\n" +
                    "Open the scene that contains your UIComponent first.", "OK");
                return;
            }

            // 收集所有 form def
            var sb = new StringBuilder();
            sb.AppendLine("//------------------------------------------------------------");
            sb.AppendLine("// EjoyGame - Auto-generated. Do NOT edit manually.");
            sb.AppendLine("// Source: UIComponent inspector → UIFormDefs.");
            sb.AppendLine("// Regenerate via menu: EjoyFramework.Core → UI → Generate UIFormId.");
            sb.AppendLine("//------------------------------------------------------------");
            sb.AppendLine();
            sb.AppendLine("namespace " + DefaultNamespace);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// UIForm 强类型 ID。业务用 GameEntry.UI.Open(UIFormId.Xxx) 替代字符串路径。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    public static class UIFormId");
            sb.AppendLine("    {");

            int formCount = 0;
            var seenIds = new System.Collections.Generic.HashSet<int>();

            foreach (var ui in uiComponents)
            {
                var so = new SerializedObject(ui);
                var defsProp = so.FindProperty("m_UIFormDefs");
                if (defsProp == null || !defsProp.isArray) continue;
                for (int i = 0; i < defsProp.arraySize; i++)
                {
                    var elem = defsProp.GetArrayElementAtIndex(i);
                    int id = elem.FindPropertyRelative("Id").intValue;
                    string assetPath = elem.FindPropertyRelative("AssetPath").stringValue;
                    string groupName = elem.FindPropertyRelative("GroupName").stringValue;
                    if (id == 0 || string.IsNullOrEmpty(assetPath)) continue;
                    if (!seenIds.Add(id))
                    {
                        Debug.LogWarning(string.Format("Duplicate UIFormDef id={0} skipped.", id));
                        continue;
                    }

                    string name = DeriveIdentifier(assetPath);
                    sb.AppendFormat("        /// <summary>{0} (group: {1})</summary>", assetPath, groupName).AppendLine();
                    sb.AppendFormat("        public const int {0} = {1};", name, id).AppendLine();
                    formCount++;
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            string dir = Path.GetDirectoryName(DefaultOutputPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(DefaultOutputPath, sb.ToString());
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Generate UIFormId",
                string.Format("Generated {0} UIFormId entries to:\n{1}", formCount, DefaultOutputPath), "OK");
        }

        /// <summary>
        /// 资源路径 → C# 标识符。
        /// "UI/MainMenu/MainMenuForm" → "MainMenuForm"
        /// "UI/Hud/HpBar" → "HpBar"
        /// 已存在重名则附加上层目录。
        /// </summary>
        private static string DeriveIdentifier(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "Unknown";
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            // 替换非法字符
            var sb = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            string id = sb.ToString();
            if (id.Length == 0 || char.IsDigit(id[0])) id = "_" + id;
            return id;
        }
    }
}
