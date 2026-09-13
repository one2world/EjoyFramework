//------------------------------------------------------------
// EjoyGame Framework Editor
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.IO;
using EjoyFramework.Core.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity.Editor.UI
{
    /// <summary>
    /// UI Root 一键生成器。两个工作流：
    ///
    ///   A. 场景内直接创建：菜单 EjoyFramework/Core/UI/Create UI Root in Scene
    ///      → 当前打开场景内生成完整 UIRoot 层级结构（Camera + 8 group canvas + EventSystem）
    ///
    ///   B. 导出为 Prefab：菜单 EjoyFramework/Core/UI/Export UI Root Prefab
    ///      → 在 Assets/GameMain/UI/Prefabs/UIRoot.prefab 生成 prefab；业务可拖入任意场景
    ///
    /// 标准 group 集合：Background / Scene / HUD / Window / Modal / Tip / System / Top
    /// 对应 UIGroupKind 枚举，sortingOrder 自动按枚举值（步长 100，业务可在中间插自定义 group）。
    /// </summary>
    public static class UIRootSetupMenu
    {
        private const string DefaultPrefabPath = "Assets/GameMain/UI/Prefabs/UIRoot.prefab";
        private static readonly Vector2 s_DefaultReferenceResolution = new Vector2(1920f, 1080f);

        [MenuItem("EjoyFramework/Core/UI/Create UI Root in Scene")]
        public static void CreateInScene()
        {
            var existing = Object.FindFirstObjectByType<UIRootHelper>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                EditorUtility.DisplayDialog("UI Root",
                    "Scene already has a UIRootHelper. Highlighted in Hierarchy.\n\n" +
                    "Delete the existing one first if you want to recreate.", "OK");
                return;
            }

            var root = BuildUIRootHierarchy();
            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("[EjoyFramework.Core] UI Root created in scene with 8 default groups + UICamera.");
        }

        [MenuItem("EjoyFramework/Core/UI/Export UI Root Prefab")]
        public static void ExportPrefab()
        {
            if (ExportPrefabSilent(out string path))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Debug.Log("[EjoyFramework.Core] UIRoot prefab exported: " + path);
                if (prefab != null) EditorGUIUtility.PingObject(prefab);
                EditorUtility.DisplayDialog("UIRoot Prefab",
                    "Prefab saved to:\n  " + path +
                    "\n\nDrop this prefab into any scene to enable the full UI stack.", "OK");
            }
        }

        /// <summary>
        /// 程序化导出 UIRoot prefab（无弹窗，供 FrameworkSetupMenu 的 ExportAll 复用）。
        /// </summary>
        internal static bool ExportPrefabSilent(out string path)
        {
            path = DefaultPrefabPath;
            string dir = Path.GetDirectoryName(DefaultPrefabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var root = BuildUIRootHierarchy();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, DefaultPrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError("[EjoyFramework.Core] Failed to save UIRoot prefab to " + DefaultPrefabPath);
                    return false;
                }
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("EjoyFramework/Core/UI/Add Viewport Region to Selection")]
        public static void AddViewportRegionToSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) throw new System.InvalidOperationException("Select a GameObject with a RectTransform.");
            if (go.GetComponent<RectTransform>() == null) throw new System.InvalidOperationException("Selected GameObject requires a RectTransform.");
            if (go.GetComponent<UIViewportRegion>() != null) throw new System.InvalidOperationException("Selected GameObject already has UIViewportRegion.");
            go.AddComponent<UIViewportRegion>().Configure(ViewportRegionMode.SafeArea);
            EditorUtility.SetDirty(go);
        }

        // ===== 构建逻辑 =====

        private static GameObject BuildUIRootHierarchy()
        {
            EnsureUILayer();

            var root = new GameObject("[UIRoot]");
            root.AddComponent<UIRootHelper>();

            PopulateUIStackUnder(root.transform);
            return root;
        }

        /// <summary>
        /// 在指定 GameObject 上挂 UIRootHelper + UIResolutionAdapter，并在其下生成完整 UI 子层级：
        ///   - [UICamera]（UI 专用正交相机）
        ///   - [EventSystem]（含 StandaloneInputModule）
        ///   - Canvas:Background / Scene / HUD / Window / Modal / Tip / System / Top（8 个标准 group）
        ///
        /// 提供给 <c>FrameworkSetupMenu</c> 复用，使 EjoyFramework.Core.prefab 的 UI 子节点可以
        /// 在同一个 prefab 里携带完整 UI 栈（无需再单独拖 UIRoot.prefab）。
        /// </summary>
        internal static void AttachFullUIStackTo(GameObject host)
        {
            if (host == null) return;
            EnsureUILayer();
            if (host.GetComponent<UIRootHelper>() == null) host.AddComponent<UIRootHelper>();
            if (host.GetComponent<UIResolutionAdapter>() == null) host.AddComponent<UIResolutionAdapter>();
            PopulateUIStackUnder(host.transform);
        }

        private static void PopulateUIStackUnder(Transform parent)
        {
            int uiLayer = LayerMask.NameToLayer("UI");

            // UI Camera 子节点 —— 必须显式 Configure，否则 prefab 会持久化 Unity 默认值
            // (Perspective / cullingMask=Everything / clearFlags=Skybox / depth=0 / HDR+MSAA=on)。
            var camGo = new GameObject("[UICamera]");
            camGo.transform.SetParent(parent, false);
            if (uiLayer >= 0) camGo.layer = uiLayer;
            camGo.AddComponent<Camera>();
            var camHelper = camGo.AddComponent<UICameraHelper>();
            camHelper.Configure();   // 关键：把正确的 camera 状态写入 prefab 而非依赖运行时 Awake

            // EventSystem 子节点（业务也可不放这里；UIRootHelper.Awake 会自动确保）
            var esGo = new GameObject("[EventSystem]");
            esGo.transform.SetParent(parent, false);
            esGo.AddComponent<EventSystem>();
            AttachUIInputModule(esGo);

            // 8 个标准 group canvas
            foreach (var kind in UIGroupKindExtensions.AllStandardGroups)
            {
                CreateGroupCanvas(parent, kind);
            }

            // 把 serialized 字段指向 [UICamera]，使 Inspector 显示完整连线。
            // 这些字段都是 private + [SerializeField]，必须走 SerializedObject 才能跨程序集赋值。
            //   - UIRootHelper.m_UICamera : UICameraHelper（业务通过 UIRootHelper.UICamera 获取）
            //   - UIComponent.m_UICamera  : Camera          （UI Inspector 显示 / ScreenSpaceCamera 模式参考）
            var rootHelper = parent.GetComponent<UIRootHelper>();
            if (rootHelper != null)
            {
                var so = new SerializedObject(rootHelper);
                var prop = so.FindProperty("m_UICamera");
                if (prop != null)
                {
                    prop.objectReferenceValue = camHelper;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            var uiComp = parent.GetComponent<UIComponent>();
            if (uiComp != null)
            {
                var so = new SerializedObject(uiComp);
                var prop = so.FindProperty("m_UICamera");
                if (prop != null)
                {
                    prop.objectReferenceValue = camHelper.Camera != null ? camHelper.Camera : camHelper.GetComponent<Camera>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static void CreateGroupCanvas(Transform parent, UIGroupKind kind)
        {
            var go = new GameObject("Canvas:" + kind.ToGroupName());
            go.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("UI");
            if (layer >= 0) go.layer = layer;
            go.AddComponent<Canvas>();
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            var helper = go.AddComponent<UIGroupCanvasHelper>();
            helper.Configure(kind.ToGroupName(), kind.ToSortingOrder(), s_DefaultReferenceResolution, 0.5f);
        }

        private static void EnsureUILayer()
        {
            if (LayerMask.NameToLayer("UI") >= 0) return;
            Debug.LogWarning("[EjoyFramework.Core] 'UI' layer not found. Unity built-in UI layer is layer 5 by default; " +
                "if you removed it, restore it or change UICameraHelper.m_RenderLayerName.");
        }

        /// <summary>
        /// 给 EventSystem 挂合适的 UI input module。
        /// 优先 InputSystemUIInputModule（新 Input System）—— 项目若启用了 com.unity.inputsystem 包，
        /// 旧 StandaloneInputModule 在 "New Input System Only" 模式下会被 Unity 自动禁用。
        /// 反射查找避免对包的硬依赖；找不到则 fallback 到 StandaloneInputModule。
        /// </summary>
        private static void AttachUIInputModule(GameObject esGo)
        {
            var newModuleType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem", throwOnError: false);
            if (newModuleType != null)
            {
                esGo.AddComponent(newModuleType);
                return;
            }
            esGo.AddComponent<StandaloneInputModule>();
        }
    }
}
