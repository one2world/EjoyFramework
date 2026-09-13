//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Globalization;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// enum → string（enum 名）。配合本地化时也可经由 binder.m_ConverterParameter 携带 key prefix。
    /// 反向：string → enum（Enum.Parse；失败返回 default）。
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Converters/Enum To String")]
    public sealed class EnumToStringConverter : MonoBehaviour, IValueConverter
    {
        [SerializeField, Tooltip("可选前缀，如 \"UI.Difficulty.\" + EnumName → 业务再走 LocalizationManager。")]
        private string m_KeyPrefix;

        public object Convert(object value, Type targetType, object parameter)
        {
            if (value == null) return string.Empty;
            string name = value.ToString();
            return string.IsNullOrEmpty(m_KeyPrefix) ? name : m_KeyPrefix + name;
        }

        public object ConvertBack(object value, Type targetType, object parameter)
        {
            if (value == null || !targetType.IsEnum) return GetDefault(targetType);
            string s = value.ToString();
            if (!string.IsNullOrEmpty(m_KeyPrefix) && s.StartsWith(m_KeyPrefix, StringComparison.Ordinal))
                s = s.Substring(m_KeyPrefix.Length);
            try { return Enum.Parse(targetType, s, ignoreCase: true); }
            catch { return GetDefault(targetType); }
        }

        // 反射-free 默认值：枚举用 Enum.ToObject(·,0)，其它数值用 Convert.ChangeType（均为 BCL/IConvertible 路径，
        // 非成员反射、IL2CPP 安全）；引用类型或不可转换类型回退 null。取代 Activator.CreateInstance(runtimeType)。
        private static object GetDefault(Type t)
        {
            if (t == null || !t.IsValueType) return null;
            if (t.IsEnum) return Enum.ToObject(t, 0);
            try { return System.Convert.ChangeType(0, t, CultureInfo.InvariantCulture); }
            catch { return null; }
        }
    }
}
