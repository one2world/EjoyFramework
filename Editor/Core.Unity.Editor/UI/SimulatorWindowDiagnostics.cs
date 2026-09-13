//------------------------------------------------------------
// EjoyGame Framework Editor — diagnostic (delete after capture bug fixed)
//------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace EjoyFramework.Core.Unity.Editor.UI
{
    public static class SimulatorWindowDiagnostics
    {
        [MenuItem("EjoyFramework/Core/UI/Debug Dump SimulatorWindow Tree")]
        public static void Dump()
        {
            var sb = new StringBuilder();
            Type baseType = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            if (baseType == null) { Debug.LogError("PlayModeView type not found"); return; }

            var instances = UnityEngine.Resources.FindObjectsOfTypeAll(baseType);
            sb.AppendLine($"PlayModeView instances: {instances.Length}");
            foreach (var obj in instances)
            {
                if (!(obj is EditorWindow w)) continue;
                sb.AppendLine();
                sb.AppendLine($"=== {w.GetType().FullName} ===");
                sb.AppendLine($"position={w.position}");

                // Visual tree dump
                var root = w.rootVisualElement;
                sb.AppendLine($"rootVisualElement: {(root != null ? root.GetType().FullName : "null")}");
                if (root != null)
                {
                    DumpElement(sb, root, 0);
                }

                // Reflectable fields
                sb.AppendLine("Fields:");
                var t = w.GetType();
                while (t != null && t != typeof(EditorWindow))
                {
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public))
                    {
                        if (f.Name.StartsWith("m_")) sb.AppendLine($"  {t.Name}.{f.Name} : {f.FieldType.FullName}");
                    }
                    t = t.BaseType;
                }
            }

            string outPath = "Assets/Screenshots/simwindow-tree.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            AssetDatabase.ImportAsset(outPath);
            Debug.Log("Written tree to " + outPath);
        }

        private static void DumpElement(StringBuilder sb, VisualElement el, int depth)
        {
            if (el == null) return;
            string indent = new string(' ', depth * 2);
            string classes = el.GetClasses() != null ? string.Join(",", el.GetClasses()) : "";
            sb.AppendLine($"{indent}- [{el.GetType().Name}] name='{el.name}' classes='{classes}' worldBound={el.worldBound} layout={el.layout}");
            if (depth > 10) { sb.AppendLine($"{indent}  (truncated)"); return; }
            for (int i = 0; i < el.childCount; i++)
            {
                DumpElement(sb, el[i], depth + 1);
            }
        }
    }
}
