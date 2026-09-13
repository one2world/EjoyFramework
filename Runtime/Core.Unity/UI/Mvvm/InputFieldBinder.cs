//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM string ↔ InputField.text。默认 TwoWay。可选 OnEndEdit 触发模式。</summary>
    [RequireComponent(typeof(InputField))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Input Field")]
    public sealed class InputFieldBinder : BinderBase
    {
        [SerializeField, Tooltip("true=用 onEndEdit（提交时同步）；false=用 onValueChanged（每键同步）。")]
        private bool m_UpdateOnEndEdit = true;

        private InputField m_Target;
        private bool m_Suppress;

        private void Reset() { m_Mode = EjoyFramework.Core.UI.Mvvm.BindingMode.TwoWay; }

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<InputField>();
            if (m_UpdateOnEndEdit) m_Target.onEndEdit.AddListener(OnViewChanged);
            else m_Target.onValueChanged.AddListener(OnViewChanged);
        }

        protected override void OnUnbind()
        {
            if (m_Target == null) return;
            if (m_UpdateOnEndEdit) m_Target.onEndEdit.RemoveListener(OnViewChanged);
            else m_Target.onValueChanged.RemoveListener(OnViewChanged);
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<InputField>();
            string s = value as string ?? value?.ToString() ?? string.Empty;
            if (m_Target.text == s) return;
            m_Suppress = true;
            try { m_Target.text = s; }
            finally { m_Suppress = false; }
        }

        private void OnViewChanged(string v) { if (!m_Suppress) PushToSource(v); }

        protected override Type GetSourceType() { return typeof(string); }
    }
}
