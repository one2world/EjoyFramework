//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    public enum ViewportRegionMode
    {
        FullScreen = 0,
        SafeArea = 1,
        HorizontalSafeArea = 2,
        VerticalSafeArea = 3,
        CustomSafeArea = 4,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Viewport Region")]
    public sealed class UIViewportRegion : UIBehaviour, ILayoutSelfController
    {
        [SerializeField] private ViewportRegionMode m_Mode = ViewportRegionMode.SafeArea;
        [SerializeField] private bool m_ApplyLeft = true;
        [SerializeField] private bool m_ApplyRight = true;
        [SerializeField] private bool m_ApplyTop = true;
        [SerializeField] private bool m_ApplyBottom = true;

        private RectTransform m_RectTransform;
        private IViewportManager m_Manager;
        private long m_AppliedRevision = -1;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_RectTransform = (RectTransform)transform;
            m_Manager = Framework.GetModule<IViewportManager>();
            m_Manager.Changed += OnViewportChanged;
            Refresh();
        }

        protected override void OnDisable()
        {
            if (m_Manager != null) m_Manager.Changed -= OnViewportChanged;
            m_Manager = null;
            base.OnDisable();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            m_AppliedRevision = -1;
        }

        public void Configure(ViewportRegionMode mode)
        {
            m_Mode = mode;
            Refresh();
        }

        public void ConfigureCustom(bool left, bool right, bool top, bool bottom)
        {
            m_Mode = ViewportRegionMode.CustomSafeArea;
            m_ApplyLeft = left;
            m_ApplyRight = right;
            m_ApplyTop = top;
            m_ApplyBottom = bottom;
            Refresh();
        }

        public void Refresh()
        {
            m_AppliedRevision = -1;
            if (m_RectTransform != null) LayoutRebuilder.MarkLayoutForRebuild(m_RectTransform);
        }

        public void ApplyImmediately()
        {
            Apply();
        }

        public void SetLayoutHorizontal()
        {
            Apply();
        }

        public void SetLayoutVertical()
        {
            Apply();
        }

        private void OnViewportChanged(in ViewportMetrics current, in ViewportMetrics previous)
        {
            Refresh();
        }

        private void Apply()
        {
            if (m_RectTransform == null || m_Manager == null || !m_Manager.HasValue) return;
            ViewportMetrics metrics = m_Manager.Current;
            if (m_AppliedRevision == metrics.Revision) return;
            m_AppliedRevision = metrics.Revision;

            bool left;
            bool right;
            bool top;
            bool bottom;
            ResolveEdges(out left, out right, out top, out bottom);
            NormalizedViewportRect region = ViewportLayout.GetSafeRect(
                in metrics, left, bottom, right, top);
            m_RectTransform.anchorMin = new Vector2(region.XMin, region.YMin);
            m_RectTransform.anchorMax = new Vector2(region.XMax, region.YMax);
            m_RectTransform.offsetMin = Vector2.zero;
            m_RectTransform.offsetMax = Vector2.zero;
        }

        private void ResolveEdges(out bool left, out bool right, out bool top, out bool bottom)
        {
            switch (m_Mode)
            {
                case ViewportRegionMode.FullScreen:
                    left = right = top = bottom = false;
                    return;
                case ViewportRegionMode.HorizontalSafeArea:
                    left = right = true;
                    top = bottom = false;
                    return;
                case ViewportRegionMode.VerticalSafeArea:
                    left = right = false;
                    top = bottom = true;
                    return;
                case ViewportRegionMode.CustomSafeArea:
                    left = m_ApplyLeft;
                    right = m_ApplyRight;
                    top = m_ApplyTop;
                    bottom = m_ApplyBottom;
                    return;
                default:
                    left = right = top = bottom = true;
                    return;
            }
        }
    }
}
