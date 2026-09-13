//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM float ↔ Slider.value。默认 TwoWay。</summary>
    [RequireComponent(typeof(Slider))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Slider")]
    public sealed class SliderBinder : BinderBase
    {
        private Slider m_Target;
        private bool m_Suppress;

        private void Reset() { m_Mode = EjoyFramework.Core.UI.Mvvm.BindingMode.TwoWay; }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Slider>();
            m_Target.onValueChanged.AddListener(OnViewChanged);
        }

        protected override void OnUnbind()
        {
            if (m_Target != null) m_Target.onValueChanged.RemoveListener(OnViewChanged);
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Slider>();
            float f = value is float fv ? fv : Convert.ToSingle(value ?? 0f);
            if (Mathf.Approximately(m_Target.value, f)) return;
            m_Suppress = true;
            try { m_Target.value = f; }
            finally { m_Suppress = false; }
        }

        private void OnViewChanged(float v) { if (!m_Suppress) PushToSource(v); }

        protected override Type GetSourceType() { return typeof(float); }
    }
}
