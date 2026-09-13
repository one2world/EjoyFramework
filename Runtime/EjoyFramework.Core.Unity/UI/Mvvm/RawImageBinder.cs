//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>VM Texture → RawImage.texture。OneWay。</summary>
    [RequireComponent(typeof(RawImage))]
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Raw Image")]
    public sealed class RawImageBinder : BinderBase
    {
        private RawImage m_Target;

        protected override void OnBound() { if (m_Target == null) m_Target = GetComponent<RawImage>(); }

        protected override void ApplyValue(object value)
        {
            if (m_Target == null) m_Target = GetComponent<RawImage>();
            m_Target.texture = value as Texture;
        }

        protected override Type GetSourceType() { return typeof(Texture); }
    }
}
