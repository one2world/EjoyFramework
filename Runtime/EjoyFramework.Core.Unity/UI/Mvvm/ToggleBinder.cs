//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM bool ↔ Toggle.isOn。默认 TwoWay。</summary>
    [RequireComponent(typeof(Toggle))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Toggle")]
    public sealed class ToggleBinder : BinderBase
    {
        private Toggle m_Target;
        private bool m_Suppress;   // 避免 ApplyValue → onValueChanged → PushToSource 形成回路

        private void Reset() { m_Mode = EjoyFramework.Core.UI.Mvvm.BindingMode.TwoWay; }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Toggle>();
            m_Target.onValueChanged.AddListener(OnViewChanged);
        }

        protected override void OnUnbind()
        {
            if (m_Target != null) m_Target.onValueChanged.RemoveListener(OnViewChanged);
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Toggle>();
            bool b = value is bool typed ? typed : Convert.ToBoolean(value ?? false);
            if (m_Target.isOn == b) return;
            m_Suppress = true;
            try { m_Target.isOn = b; }
            finally { m_Suppress = false; }
        }

        private void OnViewChanged(bool v)
        {
            if (m_Suppress) return;
            PushToSource(v);
        }

        protected override Type GetSourceType() { return typeof(bool); }
    }
}
