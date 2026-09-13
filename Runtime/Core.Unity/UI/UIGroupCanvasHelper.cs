//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// UI Group 的 Canvas 容器（默认 helper）。
    /// 一个 UIGroupGameObject 对应一个 Canvas + GraphicRaycaster + CanvasScaler，
    /// 业务 prefab 实例化后挂到此 Canvas 下，自动 sortingOrder 隔离。
    ///
    /// 升级（v1.1）：
    ///   - 自动响应屏幕方向 / 分辨率变化，重新计算 matchWidthOrHeight（横屏倾向高，竖屏倾向宽）
    ///   - 支持 Sub-Canvas 自动拆分（每个 UIForm 独立 Canvas，避免组内重建相互影响）
    ///   - 暴露完整 CanvasScaler 配置（ScaleMode + ReferencePixelsPerUnit + ScreenMatchMode）
    ///   - 业务可通过 ConfigureResolution() 统一覆盖设计分辨率 / match 策略
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    [AddComponentMenu("EjoyFramework/Core/UI/Group Canvas")]
    public sealed class UIGroupCanvasHelper : MonoBehaviour, EjoyFramework.Core.UI.IUIGroupHelper
    {
        [SerializeField]
        [Tooltip("Group 名（Background/Scene/HUD/Window/Modal/Tip/System/Top 或业务自定义）。" +
                 "保存在 prefab/scene 上，UIRootHelper.IndexGroups 据此建立 group 索引。" +
                 "Configure() 在运行时 inline-create 路径下会覆写本字段。")]
        private string m_GroupName = string.Empty;

        [SerializeField]
        [Tooltip("设计参考分辨率（横屏 1920x1080 / 竖屏 1080x1920）。")]
        private Vector2 m_ReferenceResolution = new Vector2(1920f, 1080f);

        [SerializeField]
        [Tooltip("0=匹配宽度，1=匹配高度，中间值=插值。AutoMatch 启用时此值由本组件运行时覆盖。")]
        [Range(0f, 1f)]
        private float m_MatchWidthOrHeight = 0.5f;

        [SerializeField]
        [Tooltip("启用后根据当前屏幕方向自动取 matchWidthOrHeight（横屏=1，竖屏=0）；关闭时尊重 Inspector 值。")]
        private bool m_AutoMatchByOrientation = true;

        [SerializeField]
        [Tooltip("启用后给本 group 每个 UIForm 单独加 Canvas（Sub-Canvas），避免 group 内 mesh 重建相互影响。")]
        private bool m_AutoSubCanvasPerForm = false;

        public string GroupName => m_GroupName;
        public Canvas Canvas { get; private set; }
        public CanvasScaler Scaler { get; private set; }
        public GraphicRaycaster Raycaster { get; private set; }
        public Vector2 ReferenceResolution => m_ReferenceResolution;
        public float MatchWidthOrHeight => m_MatchWidthOrHeight;
        public bool AutoMatchByOrientation => m_AutoMatchByOrientation;

        private IViewportManager m_ViewportManager;

        private void Awake()
        {
            EnsureCachedComponents();
            m_ViewportManager = EjoyFramework.Core.Framework.GetModule<IViewportManager>();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                m_ViewportManager.Changed += OnViewportChanged;
            }

            ApplyScalerMatch();
        }

        private void OnDisable()
        {
            if (m_ViewportManager != null)
            {
                m_ViewportManager.Changed -= OnViewportChanged;
            }
        }

        private void OnViewportChanged(in ViewportMetrics current, in ViewportMetrics previous)
        {
            ApplyScalerMatch(in current);
        }

        /// <summary>
        /// 由 UIComponent 创建时调用：设置 sortingOrder、参考分辨率、缩放策略。
        /// 后续屏幕方向变化 / 分辨率变化由本组件自动响应。
        /// </summary>
        public void Configure(string groupName, int sortingOrder, Vector2 referenceResolution, float matchWidthOrHeight)
        {
            Configure(groupName, sortingOrder, referenceResolution, matchWidthOrHeight, true);
        }

        /// <summary>
        /// 由 UIComponent 创建时调用：设置 sortingOrder、参考分辨率、缩放策略。
        /// autoMatchByOrientation=false 时，分辨率变化只刷新 CanvasScaler，不覆盖固定 match 值。
        /// </summary>
        public void Configure(string groupName, int sortingOrder, Vector2 referenceResolution,
            float matchWidthOrHeight, bool autoMatchByOrientation)
        {
            m_GroupName = groupName;
            SetResolutionPolicy(referenceResolution, matchWidthOrHeight, autoMatchByOrientation);

            EnsureCachedComponents();

            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.overrideSorting = true;
            Canvas.sortingOrder = sortingOrder;
            Canvas.pixelPerfect = false;

            Scaler.referencePixelsPerUnit = 100f;

            ApplyResolutionPolicy();
        }

        /// <summary>
        /// 运行时统一覆盖 UIGroup 的分辨率策略。用于设备模拟器切型、业务自定义设计尺寸、
        /// 或从 UIRootHelper 复用预制 group canvas 后统一应用项目策略。
        /// </summary>
        public void ConfigureResolution(Vector2 referenceResolution, float matchWidthOrHeight,
            bool autoMatchByOrientation)
        {
            SetResolutionPolicy(referenceResolution, matchWidthOrHeight, autoMatchByOrientation);
            EnsureCachedComponents();
            ApplyResolutionPolicy();
        }

        private void SetResolutionPolicy(Vector2 referenceResolution, float matchWidthOrHeight,
            bool autoMatchByOrientation)
        {
            if (referenceResolution.x <= 0f || referenceResolution.y <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(referenceResolution),
                    referenceResolution,
                    "Reference resolution dimensions must be greater than zero.");
            }

            if (matchWidthOrHeight < 0f || matchWidthOrHeight > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(matchWidthOrHeight),
                    matchWidthOrHeight,
                    "Match width or height must be in the inclusive range [0, 1].");
            }

            m_ReferenceResolution = referenceResolution;
            m_MatchWidthOrHeight = matchWidthOrHeight;
            m_AutoMatchByOrientation = autoMatchByOrientation;
        }

        private void ApplyResolutionPolicy()
        {
            EnsureCachedComponents();

            Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            Scaler.referenceResolution = m_ReferenceResolution;
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            ApplyScalerMatch();
        }

        // Configure 可能在 Awake 之前被调用（edit-time prefab build / AddComponent 后立刻配置），
        // 在那种情况下 Awake 还没把字段缓存好；这里按需 lazy 取一次保证不空引用。
        private void EnsureCachedComponents()
        {
            if (Canvas == null) Canvas = GetComponent<Canvas>();
            if (Scaler == null) Scaler = GetComponent<CanvasScaler>();
            if (Raycaster == null) Raycaster = GetComponent<GraphicRaycaster>();

            if (Canvas == null || Scaler == null || Raycaster == null)
            {
                throw new MissingComponentException(
                    $"{nameof(UIGroupCanvasHelper)} requires Canvas, CanvasScaler, and GraphicRaycaster.");
            }
        }

        /// <summary>
        /// <see cref="EjoyFramework.Core.UI.IUIGroupHelper"/> 契约实现：framework 在 form 进入本 group
        /// 时统一调用。Unity 侧把 <c>form.Handle</c> 解读为 GameObject 并 SetParent 到本 Canvas；
        /// <c>m_AutoSubCanvasPerForm=true</c> 时附加独立 Sub-Canvas + GraphicRaycaster，避免组内
        /// 多 form 的 mesh 重建相互拖累。
        /// </summary>
        public void AttachUIForm(EjoyFramework.Core.UI.IUIForm form)
        {
            if (form == null) return;
            var formGo = form.Handle as GameObject;
            if (formGo == null) return;

            EnsureCachedComponents();
            formGo.transform.SetParent(transform, false);

            if (m_AutoSubCanvasPerForm)
            {
                var sub = formGo.GetComponent<Canvas>();
                if (sub == null) sub = formGo.AddComponent<Canvas>();
                sub.overrideSorting = true;
                sub.sortingOrder = Canvas.sortingOrder;   // 同一 group 内共享 order
                if (formGo.GetComponent<GraphicRaycaster>() == null)
                    formGo.AddComponent<GraphicRaycaster>();
            }
        }

        /// <summary>
        /// 根据当前屏幕方向 / 分辨率重算 matchWidthOrHeight 并写回 CanvasScaler。
        /// 暴露为 public 以便业务在 EditMode 编辑器调试时强制刷新。
        /// </summary>
        public void ApplyScalerMatch()
        {
            EnsureCachedComponents();
            Scaler.referenceResolution = m_ReferenceResolution;
            Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            if (Application.isPlaying)
            {
                ViewportMetrics metrics = m_ViewportManager.Current;
                ApplyScalerMatch(in metrics);
                return;
            }

            float match;
            if (m_AutoMatchByOrientation)
            {
                bool landscape = m_ReferenceResolution.x >= m_ReferenceResolution.y;
                match = landscape ? 1f : 0f;
            }
            else
            {
                match = m_MatchWidthOrHeight;
            }
            Scaler.matchWidthOrHeight = match;
        }

        private void ApplyScalerMatch(in ViewportMetrics metrics)
        {
            float match = m_MatchWidthOrHeight;
            if (m_AutoMatchByOrientation)
            {
                match = metrics.Width >= metrics.Height ? 1f : 0f;
            }

            Scaler.matchWidthOrHeight = match;
        }
    }
}
