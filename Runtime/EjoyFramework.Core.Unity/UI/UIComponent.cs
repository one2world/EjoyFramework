//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 界面组件。完整桥接 Layer 1 UIManager + UIController。
    /// Inspector 配置：
    ///   - UIFormHelper 类型名（默认 DefaultUIFormHelper）
    ///   - 默认 UIGroup 列表（每个 group 自动生成 Canvas + Scaler + GraphicRaycaster）
    ///   - 设计参考分辨率 / matchWidthOrHeight（CanvasScaler）
    ///   - UIFormDef 注册数组（资源路径 + groupName + 标志位 → 业务 Open(id) 即可）
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/UI/Manager")]
    public sealed class UIComponent : GameFrameworkComponent
    {
        [Serializable]
        public sealed class UIGroupConfig
        {
            public string Name = "Default";
            public int Depth = 0;
            public int SortingOrder = 0;
        }

        [Serializable]
        public sealed class UIFormDefConfig
        {
            public int Id;
            public string AssetPath;
            public string GroupName = "Default";
            public int Priority = 0;
            public bool PauseCoveredUIForm = false;
            public bool Modal = false;
            public bool AllowMultiple = false;
            public bool Reusable = true;
        }

        [Header("Camera & Resolution")]
        [SerializeField] private Camera m_UICamera = null;
        [SerializeField] private Vector2 m_ReferenceResolution = new Vector2(1920, 1080);
        [SerializeField, Range(0f, 1f)] private float m_MatchWidthOrHeight = 0.5f;
        [Tooltip("启用后 UIGroupCanvasHelper 会按屏幕方向自动覆盖 matchWidthOrHeight；固定设计画布项目可关闭。")]
        [SerializeField] private bool m_AutoMatchByOrientation = true;

        [Header("Helpers")]
        [SerializeField] private string m_UIFormHelperTypeName = "EjoyFramework.Core.Unity.DefaultUIFormHelper";

        [Header("UI Groups (per Canvas)")]
        [Tooltip("场景内若已有 UIRootHelper（拖入 UIRoot.prefab），将直接复用其 group canvas，本字段忽略。" +
            "否则自动按本列表创建。默认 8 个标准 group（UIGroupKind）。")]
        [SerializeField] private UIGroupConfig[] m_UIGroups = new UIGroupConfig[]
        {
            new UIGroupConfig { Name = "Background", Depth = 0,   SortingOrder = 0   },
            new UIGroupConfig { Name = "Scene",      Depth = 100, SortingOrder = 100 },
            new UIGroupConfig { Name = "HUD",        Depth = 200, SortingOrder = 200 },
            new UIGroupConfig { Name = "Window",     Depth = 300, SortingOrder = 300 },
            new UIGroupConfig { Name = "Modal",      Depth = 400, SortingOrder = 400 },
            new UIGroupConfig { Name = "Tip",        Depth = 500, SortingOrder = 500 },
            new UIGroupConfig { Name = "System",     Depth = 600, SortingOrder = 600 },
            new UIGroupConfig { Name = "Top",        Depth = 700, SortingOrder = 700 },
        };

        [Header("UIForm Registry (data-driven)")]
        [SerializeField] private UIFormDefConfig[] m_UIFormDefs = new UIFormDefConfig[0];

        private IUIManager m_UIManager;
        private UIFormRegistry m_Registry;
        private UIController m_Controller;
        private MvvmRouteRegistry m_RouteRegistry;
        private UIRouter m_Router;

        public event EventHandler<OpenUIFormSuccessEventArgs> OpenUIFormSuccess;
        public event EventHandler<OpenUIFormFailureEventArgs> OpenUIFormFailure;
        public event EventHandler<CloseUIFormCompleteEventArgs> CloseUIFormComplete;

        protected override void Awake()
        {
            base.Awake();
            m_UIManager = Framework.GetModule<IUIManager>();
            if (m_UIManager == null) { Log.Fatal("UI manager is invalid."); return; }
            m_UIManager.OpenUIFormSuccess += OnOpenUIFormSuccess;
            m_UIManager.OpenUIFormFailure += OnOpenUIFormFailure;
            m_UIManager.CloseUIFormComplete += OnCloseUIFormComplete;

            InitializeHighLevelApi();
            m_UIManager.SetUIFormHelper(CreateUIFormHelper());
        }

        private void Start()
        {
            // UIManager 自取 ResourceManager 和 ObjectPoolManager（懒拉取）

            // 3) 注册 group canvases — 优先复用场景内 UIRootHelper（业务拖入 UIRoot.prefab 时），
            //    否则自动按 m_UIGroups 配置 inline 创建。
            BindGroupsFromUIRootOrCreate();

            m_Router.SetReady();
        }

        protected override void OnDestroy()
        {
            // base.OnDestroy()（注销自身）置于 finally：即便 controller.Dispose 抛出也必须确定性注销，
            // 否则 ComponentRegistry.s_ByType 残留已销毁实例，重注册失败、GameEntry.UI 永久返回失效旧实例。
            try
            {
                // 退订本组件 Awake 时对 UIManager 的事件订阅：UIManager 可能是跨场景常驻模块，
                // 若不退订，销毁后的本实例仍被 UIManager 事件链引用，回调会打到失效组件、无法回收。
                if (m_UIManager != null)
                {
                    m_UIManager.OpenUIFormSuccess -= OnOpenUIFormSuccess;
                    m_UIManager.OpenUIFormFailure -= OnOpenUIFormFailure;
                    m_UIManager.CloseUIFormComplete -= OnCloseUIFormComplete;
                }

                // 退订 UIController 对 UIManager 的事件订阅，避免组件销毁后 controller 因事件引用无法回收。
                m_Router?.Dispose();
                m_Router = null;
                m_RouteRegistry = null;
                m_Controller?.Dispose();
                m_Controller = null;
            }
            finally
            {
                base.OnDestroy();
            }
        }

        // ===== 高层 API =====
        public UIController Controller { get { return m_Controller; } }
        public UIFormRegistry Registry { get { return m_Registry; } }
        public MvvmRouteRegistry Routes { get { return m_RouteRegistry; } }
        public UIRouter Router { get { return m_Router; } }

        public UIStack.StackEntry Open(int formDefId, object userData = null)
            { return m_Controller.Open(formDefId, userData); }

        public bool Back(string groupName) { return m_Controller.Back(groupName); }
        public void PopAllModal() { m_Controller.PopAllModal(); }
        public bool IsAnyModalOpen { get { return m_Controller.IsAnyModalOpen; } }
        public UIStack GetStack(string groupName) { return m_Controller.GetStack(groupName); }

        // ===== 低层 pass-through =====
        public int UIGroupCount { get { return m_UIManager.UIGroupCount; } }
        public Camera UICamera { get { return m_UICamera; } }
        public bool HasUIGroup(string name) { return m_UIManager.HasUIGroup(name); }
        public IUIGroup GetUIGroup(string name) { return m_UIManager.GetUIGroup(name); }
        public bool HasUIForm(int serialId) { return m_UIManager.HasUIForm(serialId); }
        public bool HasUIForm(string assetName) { return m_UIManager.HasUIForm(assetName); }
        public IUIForm GetUIForm(int serialId) { return m_UIManager.GetUIForm(serialId); }
        public IUIForm GetUIForm(string assetName) { return m_UIManager.GetUIForm(assetName); }
        public IUIForm[] GetAllLoadedUIForms() { return m_UIManager.GetAllLoadedUIForms(); }

        public int OpenUIForm(string uiFormAssetName, string uiGroupName, int priority, bool pauseCoveredUIForm, object userData)
            { return m_UIManager.OpenUIForm(uiFormAssetName, uiGroupName, priority, pauseCoveredUIForm, userData); }
        public void CloseUIForm(int serialId) { m_Controller.CloseUIForm(serialId); }
        public void CloseUIForm(int serialId, object userData) { m_Controller.CloseUIForm(serialId, userData); }
        public void CloseAllLoadedUIForms() { m_Controller.CloseAllLoadedUIForms(); }
        public void CloseAllLoadingUIForms() { m_Controller.CloseAllLoadingUIForms(); }
        public void RefocusUIForm(IUIForm uiForm) { m_UIManager.RefocusUIForm(uiForm); }

        // ===== 内部 =====

        private void InitializeHighLevelApi()
        {
            m_Registry = new UIFormRegistry();
            if (m_UIFormDefs != null)
            {
                foreach (UIFormDefConfig cfg in m_UIFormDefs)
                {
                    if (cfg == null || cfg.Id == 0 || string.IsNullOrEmpty(cfg.AssetPath)) continue;
                    var def = new UIFormDef(cfg.Id, cfg.AssetPath, cfg.GroupName,
                        cfg.Priority, cfg.PauseCoveredUIForm, cfg.Modal, cfg.AllowMultiple, cfg.Reusable);
                    m_Registry.Register(def);
                }
            }

            m_Controller = new UIController(m_UIManager, m_Registry);
            m_RouteRegistry = new MvvmRouteRegistry(m_Registry);
            m_Router = new UIRouter(m_Controller, m_RouteRegistry, false);
        }

        /// <summary>
        /// 注册 group：优先复用场景内 UIRootHelper（业务拖入 UIRoot.prefab 模式），
        /// 否则按 m_UIGroups 配置 inline 在本 GameObject 子节点创建 Canvas。
        /// </summary>
        private void BindGroupsFromUIRootOrCreate()
        {
            var root = UIRootHelper.Instance;
            if (root != null)
            {
                // 业务把 UIRoot.prefab 拖进场景了；让 root 重建一次索引（确保 helper 已注册）
                root.IndexGroups();
                int linked = 0;
                foreach (var kind in UIGroupKindExtensions.AllStandardGroups)
                {
                    var helper = root.GetGroup(kind);
                    if (helper == null) continue;
                    helper.ConfigureResolution(m_ReferenceResolution, m_MatchWidthOrHeight, m_AutoMatchByOrientation);
                    if (m_UIManager.HasUIGroup(helper.GroupName)) continue;
                    if (m_UIManager.AddUIGroup(helper.GroupName, kind.ToSortingOrder(), helper)) linked++;
                }
                Log.Info("UIComponent: linked {0} group(s) from UIRootHelper '{1}'.", linked, root.name);
                return;
            }

            // Fallback：场景未拖入 UIRoot prefab，按 Inspector 配置 inline 创建
            if (m_UIGroups == null) return;
            foreach (var cfg in m_UIGroups)
            {
                if (cfg == null || string.IsNullOrEmpty(cfg.Name)) continue;
                if (m_UIManager.HasUIGroup(cfg.Name)) continue;

                var canvasGo = new GameObject("UIGroup_" + cfg.Name);
                canvasGo.transform.SetParent(transform, false);
                var helper = canvasGo.AddComponent<UIGroupCanvasHelper>();
                helper.Configure(cfg.Name, cfg.SortingOrder, m_ReferenceResolution, m_MatchWidthOrHeight,
                    m_AutoMatchByOrientation);

                if (!m_UIManager.AddUIGroup(cfg.Name, cfg.Depth, helper))
                {
                    Log.Warning("Add UI group '{0}' failed.", cfg.Name);
                }
            }
        }

        private IUIFormHelper CreateUIFormHelper()
        {
            IUIFormHelper formHelper = null;
            if (!string.IsNullOrEmpty(m_UIFormHelperTypeName))
            {
                // 经生成的工厂表创建（零反射）；MonoBehaviour 型 helper 由工厂在 transform 下新建子节点 AddComponent。
                // Inspector 仍存类型全名；Editor 未登记时 GeneratedHelperFactory 内部反射兜底。
                formHelper = GeneratedHelperFactory.Create(m_UIFormHelperTypeName, transform) as IUIFormHelper;
                if (formHelper == null)
                {
                    Log.Error("Can not create UI form helper '{0}'. Run EjoyFramework/Core/CodeGen/Generate Helper Factories.", m_UIFormHelperTypeName);
                }
            }
            if (formHelper == null)
            {
                Log.Warning("Falling back to DefaultUIFormHelper.");
                var helperGo = new GameObject("DefaultUIFormHelper");
                helperGo.transform.SetParent(transform, false);
                formHelper = helperGo.AddComponent<DefaultUIFormHelper>();
            }
            return formHelper;
        }

        private void OnOpenUIFormSuccess(object sender, OpenUIFormSuccessEventArgs e)
        {
            // Reparenting is owned by UIGroup → IUIGroupHelper.AttachUIForm; UIComponent does NOT
            // re-find the helper here. Forward the event to listeners and exit.
            var h = OpenUIFormSuccess;
            if (h != null) try { h(this, e); } catch (Exception ex) { Log.Error("OpenUIFormSuccess: {0}", ex); }
        }

        private void OnOpenUIFormFailure(object sender, OpenUIFormFailureEventArgs e)
        {
            Log.Warning("Open UI form '{0}' failed: {1}", e.UIFormAssetName, e.ErrorMessage);
            var h = OpenUIFormFailure;
            if (h != null) try { h(this, e); } catch (Exception ex) { Log.Error("OpenUIFormFailure: {0}", ex); }
        }

        private void OnCloseUIFormComplete(object sender, CloseUIFormCompleteEventArgs e)
        {
            var h = CloseUIFormComplete;
            if (h != null) try { h(this, e); } catch (Exception ex) { Log.Error("CloseUIFormComplete: {0}", ex); }
        }

        // GetCanvasHelperForGroup intentionally removed. Reparenting is owned by the framework
        // contract IUIGroupHelper.AttachUIForm — UIGroup calls it via the direct helper reference
        // registered with AddUIGroup. No name-based re-lookup, no naming-convention coupling.
    }
}
