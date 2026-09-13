//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM int ↔ Dropdown.value（选中索引）。默认 TwoWay。</summary>
    [RequireComponent(typeof(Dropdown))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Dropdown")]
    public sealed class DropdownBinder : BinderBase
    {
        private Dropdown m_Target;
        private bool m_Suppress;

        private void Reset() { m_Mode = EjoyFramework.Core.UI.Mvvm.BindingMode.TwoWay; }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Dropdown>();
            m_Target.onValueChanged.AddListener(OnViewChanged);
        }

        protected override void OnUnbind()
        {
            if (m_Target != null) m_Target.onValueChanged.RemoveListener(OnViewChanged);
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Dropdown>();
            int i = value is int iv ? iv : Convert.ToInt32(value ?? 0);
            if (m_Target.value == i) return;
            m_Suppress = true;
            try { m_Target.value = i; }
            finally { m_Suppress = false; }
        }

        private void OnViewChanged(int v) { if (!m_Suppress) PushToSource(v); }

        protected override Type GetSourceType() { return typeof(int); }
    }
}
