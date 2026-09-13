//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;
using EjoyFramework.Core;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Fits a RectTransform authored at a reference resolution into its parent.
    /// Keeps child elements uniformly scaled while adapting the logical layout size
    /// to the device aspect ratio. In ExpandCanvas mode, no letterbox area is created:
    /// extra width/height becomes real layout space that children can opt into through
    /// <see cref="UIReferenceLayoutElement"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Reference Resolution Fitter")]
    public sealed class UIReferenceResolutionFitter : MonoBehaviour
    {
        public enum FitMode
        {
            FitInside,
            ExpandCanvas,
        }

        [SerializeField] private Vector2 m_ReferenceSize = new Vector2(1920f, 1080f);
        [SerializeField] private FitMode m_Mode = FitMode.FitInside;
        [SerializeField] private bool m_AutoFollow = true;

        private RectTransform m_RectTransform;
        private Vector2 m_LastParentSize;

        public Vector2 ReferenceSize => m_ReferenceSize;
        public Vector2 CurrentLogicalSize { get; private set; }
        public float CurrentScale { get; private set; } = 1f;

        private void Awake() { m_RectTransform = (RectTransform)transform; }
        private void OnEnable() { RecomputeNow(); }
        private void OnRectTransformDimensionsChange() { if (m_AutoFollow) RecomputeIfNeeded(); }

        private void Update()
        {
            if (!m_AutoFollow) return;
            RecomputeIfNeeded();
        }

        private void RecomputeIfNeeded()
        {
            Vector2 parentSize = GetParentSize();
            if (parentSize != m_LastParentSize) RecomputeNow();
        }

        public void Configure(Vector2 referenceSize, FitMode mode)
        {
            if (referenceSize.x > 0f && referenceSize.y > 0f)
            {
                m_ReferenceSize = referenceSize;
            }
            m_Mode = mode;
            RecomputeNow();
        }

        public void RecomputeNow()
        {
            if (m_RectTransform == null) m_RectTransform = (RectTransform)transform;
            Vector2 parentSize = GetParentSize();
            m_LastParentSize = parentSize;
            if (parentSize.x <= 0f || parentSize.y <= 0f || m_ReferenceSize.x <= 0f || m_ReferenceSize.y <= 0f)
            {
                return;
            }

            float parentAspect = parentSize.x / parentSize.y;
            float referenceAspect = m_ReferenceSize.x / m_ReferenceSize.y;
            float scale;
            Vector2 logicalSize;

            if (m_Mode == FitMode.ExpandCanvas)
            {
                if (parentAspect >= referenceAspect)
                {
                    scale = parentSize.y / m_ReferenceSize.y;
                    logicalSize = new Vector2(parentSize.x / scale, m_ReferenceSize.y);
                }
                else
                {
                    scale = parentSize.x / m_ReferenceSize.x;
                    logicalSize = new Vector2(m_ReferenceSize.x, parentSize.y / scale);
                }
            }
            else
            {
                scale = Mathf.Min(parentSize.x / m_ReferenceSize.x, parentSize.y / m_ReferenceSize.y);
                logicalSize = m_ReferenceSize;
            }

            CurrentLogicalSize = logicalSize;
            CurrentScale = scale;

            m_RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            m_RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            m_RectTransform.pivot = new Vector2(0.5f, 0.5f);
            m_RectTransform.sizeDelta = logicalSize;
            m_RectTransform.anchoredPosition = Vector2.zero;
            m_RectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        private Vector2 GetParentSize()
        {
            if (m_RectTransform == null) m_RectTransform = (RectTransform)transform;
            var parent = m_RectTransform.parent as RectTransform;
            if (parent != null) return parent.rect.size;
            throw new FrameworkException("UIReferenceResolutionFitter requires a parent RectTransform.");
        }
    }
}
