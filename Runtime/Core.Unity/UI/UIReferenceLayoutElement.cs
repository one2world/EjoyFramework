//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Moves and optionally resizes a RectTransform when its parent reference layout
    /// area grows beyond the authored reference size.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Reference Layout Element")]
    public sealed class UIReferenceLayoutElement : MonoBehaviour
    {
        [SerializeField] private Vector2 m_ReferenceParentSize = Vector2.zero;
        [SerializeField, Range(0f, 1f)] private float m_HorizontalAnchor = 0f;
        [SerializeField, Range(0f, 1f)] private float m_VerticalAnchor = 0f;
        [SerializeField] private bool m_StretchWidth;
        [SerializeField] private bool m_StretchHeight;

        private RectTransform m_RectTransform;
        private RectTransform m_Parent;
        private Vector2 m_BaseAnchoredPosition;
        private Vector2 m_BaseSizeDelta;
        private Vector2 m_LastParentSize = new Vector2(-1f, -1f);
        private bool m_HasBase;
        private bool m_Dirty = true;

        private void Awake() { CaptureBase(); Apply(); }
        private void OnEnable() { CaptureBase(); Apply(); }
        private void OnRectTransformDimensionsChange() { m_Dirty = true; }
        private void Update() { Apply(); }

        public void Configure(Vector2 referenceParentSize, float horizontalAnchor, float verticalAnchor,
            bool stretchWidth, bool stretchHeight)
        {
            m_ReferenceParentSize = referenceParentSize;
            m_HorizontalAnchor = Mathf.Clamp01(horizontalAnchor);
            m_VerticalAnchor = Mathf.Clamp01(verticalAnchor);
            m_StretchWidth = stretchWidth;
            m_StretchHeight = stretchHeight;
            m_Dirty = true;
            Apply();
        }

        private void CaptureBase()
        {
            if (m_HasBase) return;
            if (m_RectTransform == null) m_RectTransform = (RectTransform)transform;
            m_Parent = m_RectTransform.parent as RectTransform;
            m_BaseAnchoredPosition = m_RectTransform.anchoredPosition;
            m_BaseSizeDelta = m_RectTransform.sizeDelta;
            if (m_ReferenceParentSize.x <= 0f || m_ReferenceParentSize.y <= 0f)
            {
                m_ReferenceParentSize = m_Parent != null ? m_Parent.rect.size : Vector2.zero;
            }
            m_HasBase = true;
        }

        private void Apply()
        {
            if (m_RectTransform == null) m_RectTransform = (RectTransform)transform;
            if (m_Parent == null || m_Parent != m_RectTransform.parent)
            {
                m_Parent = m_RectTransform.parent as RectTransform;
                m_Dirty = true;
            }
            if (m_Parent == null || m_ReferenceParentSize.x <= 0f || m_ReferenceParentSize.y <= 0f) return;

            Vector2 parentSize = m_Parent.rect.size;
            if (!m_Dirty && parentSize == m_LastParentSize) return;
            m_LastParentSize = parentSize;

            Vector2 extra = new Vector2(
                Mathf.Max(0f, parentSize.x - m_ReferenceParentSize.x),
                Mathf.Max(0f, parentSize.y - m_ReferenceParentSize.y));

            Vector2 position = new Vector2(
                m_BaseAnchoredPosition.x + extra.x * m_HorizontalAnchor,
                m_BaseAnchoredPosition.y - extra.y * m_VerticalAnchor);

            Vector2 size = new Vector2(
                m_BaseSizeDelta.x + (m_StretchWidth ? extra.x : 0f),
                m_BaseSizeDelta.y + (m_StretchHeight ? extra.y : 0f));

            if (m_RectTransform.anchoredPosition != position) m_RectTransform.anchoredPosition = position;
            if (m_RectTransform.sizeDelta != size) m_RectTransform.sizeDelta = size;
            m_Dirty = false;
        }
    }
}
