//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 任意对象 → bool（null/string.Empty/Unity null 视为 false，否则 true）。
    /// 主要给 VisibilityBinder 用：把 "VM.Avatar 是否非空" 直接驱动头像 GameObject 的显隐。
    /// 可与 m_TreatEmptyStringAsFalse / m_Invert 组合。
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Converters/Null To Bool")]
    public sealed class NullToBoolConverter : MonoBehaviour, IValueConverter
    {
        [SerializeField, Tooltip("把 string.Empty 也视为 null（→ false）。")]
        private bool m_TreatEmptyStringAsFalse = true;

        [SerializeField, Tooltip("结果取反：null → true / 非 null → false。")]
        private bool m_Invert;

        public object Convert(object value, Type targetType, object parameter)
        {
            bool isNonNull = !IsNullish(value);
            return m_Invert ? !isNonNull : isNonNull;
        }

        public object ConvertBack(object value, Type targetType, object parameter)
        {
            // 反向无意义（bool 无法变回原对象）—— 仅满足接口契约。
            return null;
        }

        private bool IsNullish(object value)
        {
            if (value == null) return true;
            if (value is UnityEngine.Object uo && uo == null) return true;   // 处理 Unity 的 "fake null"
            if (m_TreatEmptyStringAsFalse && value is string s && string.IsNullOrEmpty(s)) return true;
            return false;
        }
    }
}
