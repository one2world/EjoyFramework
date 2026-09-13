//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI
{
    public enum ViewportOrientation : byte
    {
        Unknown = 0,
        Portrait = 1,
        PortraitUpsideDown = 2,
        LandscapeLeft = 3,
        LandscapeRight = 4,
        AutoRotation = 5,
    }

    public readonly struct ViewportRect
    {
        public ViewportRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = Math.Max(0, width);
            Height = Math.Max(0, height);
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int XMin => X;
        public int YMin => Y;
        public int XMax => X + Width;
        public int YMax => Y + Height;
        public bool IsValid => Width > 0 && Height > 0;

        public bool Intersects(in ViewportRect other)
        {
            return XMin < other.XMax && XMax > other.XMin
                && YMin < other.YMax && YMax > other.YMin;
        }

        public bool IsSame(in ViewportRect other)
        {
            return X == other.X && Y == other.Y
                && Width == other.Width && Height == other.Height;
        }
    }

    public readonly struct ViewportInsets
    {
        public ViewportInsets(int left, int bottom, int right, int top)
        {
            Left = Math.Max(0, left);
            Bottom = Math.Max(0, bottom);
            Right = Math.Max(0, right);
            Top = Math.Max(0, top);
        }

        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Top { get; }
    }

    public readonly struct ViewportOcclusions
    {
        private readonly ViewportRect m_Item0;
        private readonly ViewportRect m_Item1;
        private readonly ViewportRect m_Item2;
        private readonly ViewportRect m_Item3;

        public ViewportOcclusions(
            int count,
            bool isTruncated,
            in ViewportRect item0,
            in ViewportRect item1,
            in ViewportRect item2,
            in ViewportRect item3)
        {
            Count = Math.Max(0, Math.Min(4, count));
            IsTruncated = isTruncated;
            m_Item0 = item0;
            m_Item1 = item1;
            m_Item2 = item2;
            m_Item3 = item3;
        }

        public int Count { get; }
        public bool IsTruncated { get; }

        public ViewportRect Get(int index)
        {
            switch (index)
            {
                case 0: return m_Item0;
                case 1: return m_Item1;
                case 2: return m_Item2;
                case 3: return m_Item3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public bool IsSame(in ViewportOcclusions other)
        {
            if (Count != other.Count || IsTruncated != other.IsTruncated) return false;
            for (int i = 0; i < Count; i++)
            {
                ViewportRect left = Get(i);
                ViewportRect right = other.Get(i);
                if (!left.IsSame(in right)) return false;
            }
            return true;
        }
    }

    public readonly struct ViewportMetrics
    {
        public ViewportMetrics(
            in ViewportRect fullRect,
            in ViewportRect safeRect,
            in ViewportOcclusions occlusions,
            ViewportOrientation orientation,
            long revision = 0)
        {
            FullRect = fullRect;
            SafeRect = ClampToFull(in safeRect, in fullRect);
            Occlusions = occlusions;
            Orientation = orientation;
            Revision = revision;
        }

        public ViewportMetrics(
            int width,
            int height,
            in ViewportInsets safeInsets,
            ViewportOrientation orientation)
        {
            var fullRect = new ViewportRect(0, 0, width, height);
            var safeRect = new ViewportRect(
                safeInsets.Left,
                safeInsets.Bottom,
                Math.Max(0, width - safeInsets.Left - safeInsets.Right),
                Math.Max(0, height - safeInsets.Bottom - safeInsets.Top));
            FullRect = fullRect;
            SafeRect = ClampToFull(in safeRect, in fullRect);
            Occlusions = default;
            Orientation = orientation;
            Revision = 0;
        }

        public ViewportRect FullRect { get; }
        public ViewportRect SafeRect { get; }
        public ViewportOcclusions Occlusions { get; }
        public ViewportOrientation Orientation { get; }
        public long Revision { get; }
        public int Width => FullRect.Width;
        public int Height => FullRect.Height;
        public bool IsValid => FullRect.IsValid;

        public ViewportInsets SafeInsets => new ViewportInsets(
            SafeRect.XMin - FullRect.XMin,
            SafeRect.YMin - FullRect.YMin,
            FullRect.XMax - SafeRect.XMax,
            FullRect.YMax - SafeRect.YMax);

        public bool IsSameLayout(in ViewportMetrics other)
        {
            ViewportRect fullRect = FullRect;
            ViewportRect otherFullRect = other.FullRect;
            ViewportRect safeRect = SafeRect;
            ViewportRect otherSafeRect = other.SafeRect;
            ViewportOcclusions occlusions = Occlusions;
            ViewportOcclusions otherOcclusions = other.Occlusions;
            return fullRect.IsSame(in otherFullRect)
                && safeRect.IsSame(in otherSafeRect)
                && occlusions.IsSame(in otherOcclusions)
                && Orientation == other.Orientation;
        }

        internal ViewportMetrics WithRevision(long revision)
        {
            ViewportRect fullRect = FullRect;
            ViewportRect safeRect = SafeRect;
            ViewportOcclusions occlusions = Occlusions;
            return new ViewportMetrics(
                in fullRect, in safeRect, in occlusions, Orientation, revision);
        }

        private static ViewportRect ClampToFull(in ViewportRect value, in ViewportRect full)
        {
            int xMin = Math.Max(full.XMin, Math.Min(full.XMax, value.XMin));
            int yMin = Math.Max(full.YMin, Math.Min(full.YMax, value.YMin));
            int xMax = Math.Max(xMin, Math.Min(full.XMax, value.XMax));
            int yMax = Math.Max(yMin, Math.Min(full.YMax, value.YMax));
            return new ViewportRect(xMin, yMin, xMax - xMin, yMax - yMin);
        }
    }

    public readonly struct NormalizedViewportRect
    {
        public NormalizedViewportRect(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float XMin { get; }
        public float YMin { get; }
        public float XMax { get; }
        public float YMax { get; }
    }

    public static class ViewportLayout
    {
        public static NormalizedViewportRect GetSafeRect(
            in ViewportMetrics metrics,
            bool applyLeft = true,
            bool applyBottom = true,
            bool applyRight = true,
            bool applyTop = true)
        {
            if (!metrics.IsValid) return new NormalizedViewportRect(0f, 0f, 1f, 1f);
            ViewportInsets insets = metrics.SafeInsets;
            float inverseWidth = 1f / metrics.Width;
            float inverseHeight = 1f / metrics.Height;
            return new NormalizedViewportRect(
                applyLeft ? insets.Left * inverseWidth : 0f,
                applyBottom ? insets.Bottom * inverseHeight : 0f,
                applyRight ? 1f - insets.Right * inverseWidth : 1f,
                applyTop ? 1f - insets.Top * inverseHeight : 1f);
        }
    }
}
