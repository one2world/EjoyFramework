//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM Color → Graphic.color（Image / Text / RawImage 通用）。OneWay。</summary>
    [RequireComponent(typeof(Graphic))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Color")]
    public sealed class ColorBinder : BinderBase
    {
        private Graphic m_Target;

        protected override void OnBound() { if (m_Target == null) m_Target = GetComponent<Graphic>(); }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Graphic>();
            if (value is IControlViewState<Graphic> state)
            {
                state.ApplyTo(m_Target);
                return;
            }

            if (value is Color c) m_Target.color = c;
            else if (value is Color32 c32) m_Target.color = c32;
        }

        protected override Type GetSourceType() { return typeof(Color); }
    }
}
