// Copyright (c) Ejoy. All rights reserved.

using EjoyFramework.Core;
using EjoyFramework.Core.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Moves a UI group away from one unsafe screen edge without changing its
    /// authored size or coordinate system.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIViewportEdgeOffset : UIBehaviour, ILayoutSelfController
    {
        [SerializeField]
        private ViewportEdge _edge = ViewportEdge.Left;

        [SerializeField]
        private ViewportInsetSource _source = ViewportInsetSource.SafeArea;

        private RectTransform _rectTransform;
        private IViewportManager _viewportManager;
        private Vector2 _authoredPosition;
        private bool _hasAuthoredPosition;

        /// <summary>
        /// Gets the safe edge followed by this group.
        /// </summary>
        public ViewportEdge Edge => _edge;

        /// <summary>
        /// Gets the authoritative geometry source used by this group.
        /// </summary>
        public ViewportInsetSource Source => _source;

        /// <summary>
        /// Configures the safe edge.
        /// </summary>
        public void Configure(ViewportEdge edge, ViewportInsetSource source)
        {
            if (_edge == edge && _source == source)
            {
                return;
            }

            _edge = edge;
            _source = source;
            SetLayoutDirty();
        }

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            CacheRectTransform();
            CaptureAuthoredPosition();
        }

        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();
            CacheRectTransform();
            CaptureAuthoredPosition();

            _viewportManager = Framework.GetModule<IViewportManager>();
            _viewportManager.Changed += OnViewportChanged;
            SetLayoutDirty();
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            if (_viewportManager != null)
            {
                _viewportManager.Changed -= OnViewportChanged;
                _viewportManager = null;
            }

            RestoreAuthoredPosition();
            base.OnDisable();
        }

        /// <inheritdoc />
        public void SetLayoutHorizontal()
        {
            ApplyHorizontalOffset();
        }

        /// <inheritdoc />
        public void SetLayoutVertical()
        {
            ApplyVerticalOffset();
        }

        private void OnViewportChanged(in ViewportMetrics current, in ViewportMetrics previous)
        {
            SetLayoutDirty();
        }

        private void ApplyHorizontalOffset()
        {
            if (!_hasAuthoredPosition || _viewportManager == null || !_viewportManager.HasValue)
            {
                return;
            }

            ViewportMetrics metrics = _viewportManager.Current;
            float offset = 0f;
            if (_edge == ViewportEdge.Left)
            {
                offset = ViewportEdgeLayout.GetInset(in metrics, _edge, _source) * GetHorizontalScale(metrics);
            }
            else if (_edge == ViewportEdge.Right)
            {
                offset = -ViewportEdgeLayout.GetInset(in metrics, _edge, _source) * GetHorizontalScale(metrics);
            }

            Vector2 position = _rectTransform.anchoredPosition;
            position.x = _authoredPosition.x + offset;
            if (!Mathf.Approximately(_rectTransform.anchoredPosition.x, position.x))
            {
                _rectTransform.anchoredPosition = position;
            }
        }

        private void ApplyVerticalOffset()
        {
            if (!_hasAuthoredPosition || _viewportManager == null || !_viewportManager.HasValue)
            {
                return;
            }

            ViewportMetrics metrics = _viewportManager.Current;
            float offset = 0f;
            if (_edge == ViewportEdge.Top)
            {
                offset = -ViewportEdgeLayout.GetInset(in metrics, _edge, _source) * GetVerticalScale(metrics);
            }
            else if (_edge == ViewportEdge.Bottom)
            {
                offset = ViewportEdgeLayout.GetInset(in metrics, _edge, _source) * GetVerticalScale(metrics);
            }

            Vector2 position = _rectTransform.anchoredPosition;
            position.y = _authoredPosition.y + offset;
            if (!Mathf.Approximately(_rectTransform.anchoredPosition.y, position.y))
            {
                _rectTransform.anchoredPosition = position;
            }
        }

        private float GetHorizontalScale(in ViewportMetrics metrics)
        {
            RectTransform parent = _rectTransform.parent as RectTransform;
            return parent == null || metrics.FullRect.Width <= 0
                ? 0f
                : parent.rect.width / metrics.FullRect.Width;
        }

        private float GetVerticalScale(in ViewportMetrics metrics)
        {
            RectTransform parent = _rectTransform.parent as RectTransform;
            return parent == null || metrics.FullRect.Height <= 0
                ? 0f
                : parent.rect.height / metrics.FullRect.Height;
        }

        private void CacheRectTransform()
        {
            if (_rectTransform == null)
            {
                _rectTransform = (RectTransform)transform;
            }
        }

        private void CaptureAuthoredPosition()
        {
            if (_hasAuthoredPosition)
            {
                return;
            }

            _authoredPosition = _rectTransform.anchoredPosition;
            _hasAuthoredPosition = true;
        }

        private void RestoreAuthoredPosition()
        {
            if (!_hasAuthoredPosition || _rectTransform == null)
            {
                return;
            }

            _rectTransform.anchoredPosition = _authoredPosition;
            _hasAuthoredPosition = false;
        }

        private void SetLayoutDirty()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            CacheRectTransform();
            LayoutRebuilder.MarkLayoutForRebuild(_rectTransform);
        }
    }
}
