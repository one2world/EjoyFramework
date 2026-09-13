//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM bool → Selectable.interactable。OneWay。挂在 Button/Toggle/Slider 等可交互控件上。</summary>
    [RequireComponent(typeof(Selectable))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Interactable")]
    public sealed class InteractableBinder : BinderBase
    {
        [SerializeField, Tooltip("反转 bool。")]
        private bool m_Invert;

        private Selectable m_Target;

        protected override void OnBound() { if (m_Target == null) m_Target = GetComponent<Selectable>(); }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Selectable>();
            bool b = value is bool typed ? typed : value != null;
            if (m_Invert) b = !b;
            m_Target.interactable = b;
        }

        protected override Type GetSourceType() { return typeof(bool); }
    }
}
