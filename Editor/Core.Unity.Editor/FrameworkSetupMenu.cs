//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EjoyFramework.Core.Unity.Editor.UI;
using EjoyFramework.Core.Unity;
using UnityEditor;
using UnityEngine;

namespace EjoyFramework.Core.Unity.Editor
{
    /// <summary>
    /// Framework 根 GameObject 一键生成器。
    ///
    /// 业务只需两步即可获得完整 Framework：
    ///   1. EjoyFramework/Core/Framework/Export Framework Prefab → 导出 EjoyFramework.Core.prefab（自带完整 UI 栈）
    ///   2. 把 EjoyFramework.Core.prefab 拖入启动场景 → 完成
    ///
    /// 备注：UIRoot.prefab 仍可通过 EjoyFramework/Core/UI/Export UI Root Prefab 单独导出，
    /// 主要给 UI-only 工作流（不想引入全套 Framework 的纯 UI 调试 / 制作场景）。
    /// 但默认 drop-in 体验只需要 EjoyFramework.Core.prefab —— 不要把两者同时拖进同一场景，
    /// 会重复产生 UICamera / EventSystem。
    ///
    /// EjoyFramework.Core.prefab 结构（21 个 *Component + 完整 UI 栈，按 Priority 顺序）：
    ///   [EjoyFramework.Core]                    (BaseComponent — drives Framework.Update, DontDestroyOnLoad=true)
    ///   ├─ Event                           (EventComponent)
    ///   ├─ Fsm                             (FsmComponent)
    ///   ├─ Procedure                       (ProcedureComponent)
    ///   ├─ Coroutine                       (CoroutineComponent)
    ///   ├─ ObjectPool                      (ObjectPoolComponent)
    ///   ├─ Resource                        (ResourceComponent)
    ///   ├─ Entity                          (EntityComponent)
    ///   ├─ UI                              (UIComponent + UIRootHelper + UIResolutionAdapter)
    ///   │  ├─ [UICamera]                   (Camera + UICameraHelper — UI-layer only, orthographic, depth=10)
    ///   │  ├─ [EventSystem]                (EventSystem + StandaloneInputModule)
    ///   │  ├─ Canvas:Background            (Canvas + Scaler + Raycaster + UIGroupCanvasHelper, order=0)
    ///   │  ├─ Canvas:Scene                 (order=100)
    ///   │  ├─ Canvas:HUD                   (order=200)
    ///   │  ├─ Canvas:Window                (order=300)
    ///   │  ├─ Canvas:Modal                 (order=400)
    ///   │  ├─ Canvas:Tip                   (order=500)
    ///   │  ├─ Canvas:System                (order=600)
    ///   │  └─ Canvas:Top                   (order=700)
    ///   ├─ Scene                           (SceneComponent)
    ///   ├─ Sound                           (SoundComponent)
    ///   ├─ Config                          (ConfigComponent)
    ///   ├─ Localization                    (LocalizationComponent)
    ///   ├─ DataTable                       (DataTableComponent)
    ///   ├─ DataNode                        (DataNodeComponent)
    ///   ├─ Setting                         (SettingComponent)
    ///   ├─ Save                            (SaveComponent)
    ///   ├─ Input                           (InputComponent)
    ///   ├─ Network                         (NetworkComponent)
    ///   └─ Debugger                        (DebuggerComponent — F1 toggles IMGUI debug windows)
    /// </summary>
    public static class FrameworkSetupMenu
    {
        private const string FrameworkPrefabPath = "Assets/GameMain/Framework/Prefabs/EjoyFramework.Core.prefab";

        // 子节点顺序与 Framework Module Priority 一致：高 Priority 在前（先 Update / 后 Shutdown）。
        // 让 Inspector 顺序与运行期执行顺序一致，便于排查。
        private static readonly Type[] s_ComponentTypes = new Type[]
        {
            typeof(EventComponent),
            typeof(FsmComponent),
            typeof(ProcedureComponent),
            typeof(CoroutineComponent),
            typeof(ObjectPoolComponent),
            typeof(ResourceComponent),
            typeof(EntityComponent),
            typeof(UIComponent),
            typeof(SceneComponent),
            typeof(SoundComponent),
            typeof(ConfigComponent),
            typeof(LocalizationComponent),
            typeof(DataTableComponent),
            typeof(DataNodeComponent),
            typeof(SettingComponent),
            typeof(SaveComponent),
            typeof(InputComponent),
            typeof(NetworkComponent),
            typeof(DebuggerComponent),
        };

        // ===== 菜单 =====

        [MenuItem("EjoyFramework/Core/Framework/Create Framework in Scene", priority = 0)]
        public static void CreateInScene()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<BaseComponent>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                EditorUtility.DisplayDialog("EjoyFramework.Core",
                    "Scene already has a BaseComponent. Highlighted in Hierarchy.\n\n" +
                    "Delete the existing root first if you want to recreate.", "OK");
                return;
            }

            var root = BuildFrameworkHierarchy();
            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("[EjoyFramework.Core] Framework root created in scene with " + s_ComponentTypes.Length +
                " sub-components under [EjoyFramework.Core].");
        }

        [MenuItem("EjoyFramework/Core/Framework/Export Framework Prefab", priority = 1)]
        public static void ExportFrameworkPrefab()
        {
            ExportFrameworkPrefabInternal(silent: false);
        }

        [MenuItem("EjoyFramework/Core/Framework/Export Framework + Standalone UIRoot", priority = 2)]
        public static void ExportFrameworkAndStandaloneUIRoot()
        {
            // 高级用法：业务想保留一个 UI-only prefab（例如 UI 调试场景），一次性两个 prefab 都生成。
            // 注意：不要在同一场景同时拖两者，否则 UICamera / EventSystem 会重复。
            string frameworkPath = ExportFrameworkPrefabInternal(silent: true);
            UIRootSetupMenu.ExportPrefabSilent(out string uiRootPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var summary = "Exported:\n" +
                "  " + frameworkPath + "  (drop this into your startup scene — UI included)\n" +
                "  " + uiRootPath + "  (standalone UI-only prefab, optional)\n\n" +
                "DO NOT drop both into the same scene — UICamera / EventSystem will duplicate.";
            Debug.Log("[EjoyFramework.Core] " + summary.Replace("\n", " "));
            EditorUtility.DisplayDialog("Framework Prefabs", summary, "OK");
        }

        // ===== 内部 =====

        internal static string ExportFrameworkPrefabInternal(bool silent)
        {
            string dir = Path.GetDirectoryName(FrameworkPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var root = BuildFrameworkHierarchy();
            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, FrameworkPrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError("[EjoyFramework.Core] Failed to save framework prefab to " + FrameworkPrefabPath);
                    return null;
                }
                if (!silent)
                {
                    Debug.Log("[EjoyFramework.Core] Framework prefab exported: " + FrameworkPrefabPath);
                    EditorGUIUtility.PingObject(prefab);
                    EditorUtility.DisplayDialog("EjoyFramework.Core Prefab",
                        "Prefab saved to:\n  " + FrameworkPrefabPath +
                        "\n\nDrop into your startup scene to bring up the full framework.", "OK");
                }
                return FrameworkPrefabPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject BuildFrameworkHierarchy()
        {
            var root = new GameObject("[EjoyFramework.Core]");
            root.AddComponent<BaseComponent>();

            var seen = new HashSet<Type>();
            foreach (var t in s_ComponentTypes)
            {
                if (t == null) continue;
                if (!seen.Add(t)) continue;

                var child = new GameObject(StripComponentSuffix(t.Name));
                child.transform.SetParent(root.transform, false);
                child.AddComponent(t);

                // UI 子节点：除了 UIComponent，还要在同一节点挂 UIRootHelper + UIResolutionAdapter，
                // 并在其下生成 UICamera + EventSystem + 8 个标准 Canvas group。
                // 这样 EjoyFramework.Core.prefab 单独就是完整 drop-in，不需要再单独拖 UIRoot.prefab。
                if (t == typeof(UIComponent))
                {
                    UIRootSetupMenu.AttachFullUIStackTo(child);
                }
            }
            return root;
        }

        private static string StripComponentSuffix(string typeName)
        {
            const string suffix = "Component";
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - suffix.Length);
            return typeName;
        }
    }
}
