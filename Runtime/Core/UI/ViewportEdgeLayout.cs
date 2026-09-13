// Copyright (c) Ejoy. All rights reserved.

using System;

namespace EjoyFramework.Core.UI
{
    /// <summary>
    /// Identifies a physical viewport edge.
    /// </summary>
    public enum ViewportEdge
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3,
    }

    /// <summary>
    /// Selects the authoritative geometry used to resolve a viewport edge.
    /// </summary>
    public enum ViewportInsetSource
    {
        SafeArea = 0,
        Occlusion = 1,
    }

    /// <summary>
    /// Resolves edge-specific obstruction insets from viewport metrics.
    /// </summary>
    public static class ViewportEdgeLayout
    {
        /// <summary>
        /// Gets the inset required at one edge from the selected geometry source.
        /// </summary>
        public static int GetInset(
            in ViewportMetrics metrics,
            ViewportEdge edge,
            ViewportInsetSource source)
        {
            if (source == ViewportInsetSource.SafeArea)
            {
                return GetSafeInset(in metrics, edge);
            }

            if (source != ViewportInsetSource.Occlusion)
            {
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
            }

            ViewportOcclusions occlusions = metrics.Occlusions;
            if (occlusions.IsTruncated)
            {
                throw new InvalidOperationException(
                    "Viewport occlusion topology exceeds the supported capacity.");
            }

            ViewportRect fullRect = metrics.FullRect;
            int inset = 0;
            for (int i = 0; i < occlusions.Count; i++)
            {
                ViewportRect occlusion = occlusions.Get(i);
                if (!BelongsToEdge(in occlusion, in fullRect, edge))
                {
                    continue;
                }

                int candidate = GetOcclusionInset(in occlusion, in fullRect, edge);
                inset = Math.Max(inset, candidate);
            }

            return Math.Max(0, inset);
        }

        private static int GetSafeInset(in ViewportMetrics metrics, ViewportEdge edge)
        {
            switch (edge)
            {
                case ViewportEdge.Left:
                    return metrics.SafeInsets.Left;
                case ViewportEdge.Right:
                    return metrics.SafeInsets.Right;
                case ViewportEdge.Top:
                    return metrics.SafeInsets.Top;
                case ViewportEdge.Bottom:
                    return metrics.SafeInsets.Bottom;
                default:
                    return 0;
            }
        }

        private static bool BelongsToEdge(
            in ViewportRect occlusion,
            in ViewportRect fullRect,
            ViewportEdge edge)
        {
            int leftDistance = Math.Max(0, occlusion.XMin - fullRect.XMin);
            int rightDistance = Math.Max(0, fullRect.XMax - occlusion.XMax);
            int bottomDistance = Math.Max(0, occlusion.YMin - fullRect.YMin);
            int topDistance = Math.Max(0, fullRect.YMax - occlusion.YMax);
            int nearestDistance = Math.Min(
                Math.Min(leftDistance, rightDistance),
                Math.Min(bottomDistance, topDistance));

            switch (edge)
            {
                case ViewportEdge.Left:
                    return leftDistance == nearestDistance;
                case ViewportEdge.Right:
                    return rightDistance == nearestDistance;
                case ViewportEdge.Top:
                    return topDistance == nearestDistance;
                case ViewportEdge.Bottom:
                    return bottomDistance == nearestDistance;
                default:
                    return false;
            }
        }

        private static int GetOcclusionInset(
            in ViewportRect occlusion,
            in ViewportRect fullRect,
            ViewportEdge edge)
        {
            switch (edge)
            {
                case ViewportEdge.Left:
                    return Math.Min(fullRect.Width, occlusion.XMax - fullRect.XMin);
                case ViewportEdge.Right:
                    return Math.Min(fullRect.Width, fullRect.XMax - occlusion.XMin);
                case ViewportEdge.Top:
                    return Math.Min(fullRect.Height, fullRect.YMax - occlusion.YMin);
                case ViewportEdge.Bottom:
                    return Math.Min(fullRect.Height, occlusion.YMax - fullRect.YMin);
                default:
                    return 0;
            }
        }
    }
}
