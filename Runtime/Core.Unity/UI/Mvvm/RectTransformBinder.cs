//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/RectTransform")]
    public sealed class RectTransformBinder : BinderBase
    {
        private RectTransform m_Target;

        protected override Type GetSourceType() { return typeof(IControlViewState<RectTransform>); }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = (RectTransform)transform;
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = (RectTransform)transform;
            if (value is IControlViewState<RectTransform> state)
            {
                state.ApplyTo(m_Target);
            }
        }
    }
}
