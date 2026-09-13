//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// VM bool → gameObject.SetActive。OneWay。
    /// 可指定 m_Invert 反转：用于 "如果 IsLoading=true 则隐藏 Hint" 这类场景。
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/Visibility")]
    public sealed class VisibilityBinder : BinderBase
    {
        [SerializeField, Tooltip("反转：true 时把 bool 取反。")]
        private bool m_Invert;

        [SerializeField, Tooltip("自定义目标 GameObject。空则使用本组件挂载的对象。")]
        private GameObject m_Target;

        protected override void ApplyValue(object value)
        {
            var go = m_Target != null ? m_Target : gameObject;
            bool b = value is bool typed ? typed : value != null;
            if (m_Invert) b = !b;
            if (go.activeSelf != b) go.SetActive(b);
        }

        protected override Type GetSourceType() { return typeof(bool); }
    }
}
