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
    /// bool → !bool。挂在 binder 的 m_Converter 字段上即可反转：
    /// 如 VisibilityBinder + 该 converter → "IsLoading=true 时隐藏"。
    /// （VisibilityBinder 自身也有 m_Invert，二者择一即可。本 converter 适合给其它 binder 复用。）
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Converters/Bool Negate")]
    public sealed class BoolNegateConverter : MonoBehaviour, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter)
        {
            return !ToBool(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter)
        {
            return !ToBool(value);
        }

        private static bool ToBool(object value)
        {
            if (value is bool b) return b;
            if (value == null) return false;
            try { return System.Convert.ToBoolean(value); }
            catch { return false; }
        }
    }
}
