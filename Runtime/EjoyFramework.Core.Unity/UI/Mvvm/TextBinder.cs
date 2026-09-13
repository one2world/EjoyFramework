//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM string → uGUI Text.text。OneWay 默认。</summary>
    [RequireComponent(typeof(Text))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Text")]
    public sealed class TextBinder : BinderBase
    {
        [SerializeField, Tooltip("可选 string.Format pattern，例如 'HP: {0}/100'。空则直接 .ToString()。")]
        private string m_Format;

        private Text m_Target;

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Text>();
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Text>();
            if (value is IControlViewState<Text> state)
            {
                state.ApplyTo(m_Target);
                return;
            }

            // uGUI Text 没有 SetCharArray 之类的 char[] 接口，只能退化成 ToString()（每次一次 string 分配）。
            // 每帧刷新的数值文本请改用 TMP + TMPTextBinder 走零 GC 直通路径。
            if (value is BindableText text)
            {
                m_Target.text = text.ToString();
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
