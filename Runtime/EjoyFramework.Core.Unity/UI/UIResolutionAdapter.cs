//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using EjoyFramework.Core.UI;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    [DefaultExecutionOrder(-9500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/UI/Viewport Adapter")]
    public sealed class UIResolutionAdapter : MonoBehaviour
    {
        private UnityViewportHelper m_Helper;
        private IViewportManager m_Manager;

        private void Awake()
        {
            m_Manager = Framework.GetModule<IViewportManager>();
            m_Helper = new UnityViewportHelper();
            m_Manager.SetHelper(m_Helper);
        }

        public void Refresh()
        {
            m_Helper.Invalidate();
            m_Manager.Refresh();
        }
    }

    internal sealed class UnityViewportHelper : IViewportHelper
    {
        private int m_LastWidth = -1;
        private int m_LastHeight = -1;
        private Rect m_LastSafeArea;
        private ScreenOrientation m_LastOrientation = (ScreenOrientation)(-1);
        private bool m_Invalidated = true;

        public bool TryGetMetrics(out ViewportMetrics metrics)
        {
            int width = Screen.width;
            int height = Screen.height;
            Rect safeArea = Screen.safeArea;
            ScreenOrientation orientation = Screen.orientation;
            bool changed = m_Invalidated
                || width != m_LastWidth
                || height != m_LastHeight
                || safeArea != m_LastSafeArea
                || orientation != m_LastOrientation;
            if (!changed)
            {
                metrics = default;
                return false;
            }

            m_Invalidated = false;
            m_LastWidth = width;
            m_LastHeight = height;
            m_LastSafeArea = safeArea;
            m_LastOrientation = orientation;

            Rect[] cutouts = Screen.cutouts;
            ViewportRect item0 = ToViewportRect(cutouts, 0);
            ViewportRect item1 = ToViewportRect(cutouts, 1);
            ViewportRect item2 = ToViewportRect(cutouts, 2);
            ViewportRect item3 = ToViewportRect(cutouts, 3);
            int cutoutCount = cutouts != null ? cutouts.Length : 0;
            var occlusions = new ViewportOcclusions(
                cutoutCount,
                cutoutCount > 4,
                in item0,
                in item1,
                in item2,
                in item3);
            var fullRect = new ViewportRect(0, 0, width, height);
            var safeRect = new ViewportRect(
                Mathf.RoundToInt(safeArea.x),
                Mathf.RoundToInt(safeArea.y),
                Mathf.RoundToInt(safeArea.width),
                Mathf.RoundToInt(safeArea.height));
            metrics = new ViewportMetrics(
                in fullRect,
                in safeRect,
                in occlusions,
                ToViewportOrientation(orientation));
            return true;
        }

        public void Invalidate()
        {
            m_Invalidated = true;
        }

        private static ViewportRect ToViewportRect(Rect[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length) return default;
            Rect value = values[index];
            return new ViewportRect(
                Mathf.RoundToInt(value.x),
                Mathf.RoundToInt(value.y),
                Mathf.RoundToInt(value.width),
                Mathf.RoundToInt(value.height));
        }

        private static ViewportOrientation ToViewportOrientation(ScreenOrientation value)
        {
            switch (value)
            {
                case ScreenOrientation.Portrait: return ViewportOrientation.Portrait;
                case ScreenOrientation.PortraitUpsideDown: return ViewportOrientation.PortraitUpsideDown;
                case ScreenOrientation.LandscapeLeft: return ViewportOrientation.LandscapeLeft;
                case ScreenOrientation.LandscapeRight: return ViewportOrientation.LandscapeRight;
                case ScreenOrientation.AutoRotation: return ViewportOrientation.AutoRotation;
                default: return ViewportOrientation.Unknown;
            }
        }
    }

}
