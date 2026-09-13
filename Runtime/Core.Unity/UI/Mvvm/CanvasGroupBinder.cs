//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/CanvasGroup")]
    public sealed class CanvasGroupBinder : BinderBase
    {
        private CanvasGroup m_Target;

        protected override Type GetSourceType() { return typeof(IControlViewState<CanvasGroup>); }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<CanvasGroup>();
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<CanvasGroup>();
            if (value is IControlViewState<CanvasGroup> state)
            {
                state.ApplyTo(m_Target);
            }
        }
    }
}
