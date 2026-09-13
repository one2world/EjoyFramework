//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM Sprite → Image.sprite。OneWay。</summary>
    [RequireComponent(typeof(Image))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Image")]
    public sealed class ImageBinder : BinderBase
    {
        [SerializeField, Tooltip("值为 null 时是否隐藏 Image（默认仅清 sprite 不隐藏）。")]
        private bool m_HideWhenNull;

        private Image m_Target;

        protected override void OnBound()
        {
            if (m_Target == null) m_Target = GetComponent<Image>();
        }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<Image>();
            if (value is IControlViewState<Image> state)
            {
                state.ApplyTo(m_Target);
                return;
            }

            var sprite = value as Sprite;
            m_Target.sprite = sprite;
            if (m_HideWhenNull) m_Target.enabled = sprite != null;
        }

        protected override Type GetSourceType() { return typeof(Sprite); }
    }
}
