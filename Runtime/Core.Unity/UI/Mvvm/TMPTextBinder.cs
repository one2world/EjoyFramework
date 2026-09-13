//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;
using TMPro;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// TextMeshPro 文本 binder。直接强类型访问 <see cref="TMP_Text"/>（com.unity.ugui 内置 TextMeshPro），
    /// 不再经反射查找类型/属性——可读性与性能更好，且 IL2CPP 不会因裁剪 TMP 成员而失效。
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/TMP Text")]
    public sealed class TMPTextBinder : BinderBase
    {
        [SerializeField, Tooltip("可选 string.Format pattern。")]
        private string m_Format;

        private TMP_Text m_Target;

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<TMP_Text>();
            if (m_Target == null) FrameworkLog.Warning("[TMPTextBinder] no TMP_Text on '{0}'.", name);
#if UNITY_EDITOR
            WarnIfConverterBreaksZeroAllocPath();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// converter 跑在 ApplyValue 之前，会把 BindableText 变成别的东西（通常是 string），
        /// 于是零 GC 直通路径被无声地绕开——功能照常，只是每帧又开始分配。这种退化在 profiler 之外
        /// 完全不可见，所以在编辑期显式告警。
        /// </summary>
        private void WarnIfConverterBreaksZeroAllocPath()
        {
            if (m_Converter is not IValueConverter || m_DataContext == null || m_CompiledPath == null) return;
            object raw;
            try { raw = m_CompiledPath.Resolve(m_DataContext); }
            catch { return; }
            if (raw is BindableText)
            {
                FrameworkLog.Warning("[TMPTextBinder] '{0}' binds a BindableText but has a converter attached; the zero-alloc SetCharArray path will be bypassed. Compose the text in the ViewModel with TempText instead.", name);
            }
        }
#endif

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) return;
            if (value is IControlViewState<Component> state)
            {
                state.ApplyTo(m_Target);
                return;
            }

            // 零 GC 直通：BindableText 内部就是 char[]，直接下发给 TMP，绕开 string 分配与 string.Format。
            // 这条路径忽略 m_Format —— 需要拼接的部分请在 ViewModel 侧用 TempText 组装好后写进 BindableText。
            if (value is BindableText text)
            {
                if (text.Length > 0)
                {
                    m_Target.SetCharArray(text.Buffer, 0, text.Length);
                }
                else
                {
                    // 长度为 0 时 buffer 可能还未分配，走 string.Empty（驻留常量，无分配）。
                    m_Target.text = string.Empty;
                }

                return;
            }

            string s;
            if (value == null) s = string.Empty;
            else if (!string.IsNullOrEmpty(m_Format))
            {
                try { s = string.Format(m_Format, value); }
                catch { s = value.ToString(); }
            }
            else s = value.ToString();
            m_Target.text = s;
        }


        /// <summary>文本只做展示，不是可编辑输入源，从不调用 PushToSource。</summary>
        protected override bool SupportsSourceWrites { get { return false; } }

        protected override Type GetSourceType() { return typeof(string); }
    }
}
