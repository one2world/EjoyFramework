//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.UI
{
    public delegate void ViewportChangedHandler(in ViewportMetrics current, in ViewportMetrics previous);

    public interface IViewportHelper
    {
        bool TryGetMetrics(out ViewportMetrics metrics);
    }

    public interface IViewportManager
    {
        bool HasValue { get; }
        ViewportMetrics Current { get; }
        event ViewportChangedHandler Changed;

        void SetHelper(IViewportHelper helper);
        bool Refresh();
    }
}
