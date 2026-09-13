//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// Logic-side view state for a concrete Unity control type.
    /// Binders stay control-oriented; property decisions live in the VM/application layer.
    /// </summary>
    public interface IControlViewState<in TControl>
    {
        void ApplyTo(TControl control);
    }
}
