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
    /// 数字 → 格式化字符串（用 m_Format 字段配置；若不设，回退到 binder 的 m_ConverterParameter）。
    /// 典型用例：
    ///   m_Format = "N0"   → 1234567 → "1,234,567"
    ///   m_Format = "F2"   → 3.14159 → "3.14"
    ///   m_Format = "{0:P0}" → 0.85 → "85%"
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Converters/Number Format")]
    public sealed class NumberFormatConverter : MonoBehaviour, IValueConverter
    {
        [SerializeField, Tooltip("格式化字符串（C# standard / custom numeric format）。空则用 binder 的 ConverterParameter。")]
        private string m_Format = "F2";

        [SerializeField, Tooltip("使用 Invariant culture（推荐）。关闭则用当前线程 culture，可能因玩家 locale 不同而变。")]
        private bool m_InvariantCulture = true;

        public object Convert(object value, Type targetType, object parameter)
        {
            if (value == null) return string.Empty;
            string fmt = !string.IsNullOrEmpty(m_Format) ? m_Format : parameter as string ?? "G";
            var culture = m_InvariantCulture ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            try
            {
                if (fmt.IndexOf('{') >= 0) return string.Format(culture, fmt, value);
                return value is IFormattable f ? f.ToString(fmt, culture) : value.ToString();
            }
            catch
            {
                return value.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter)
        {
            // 反向：解析字符串到 target 数值类型。仅作 best-effort，失败返回 default。
            if (value == null) return GetDefault(targetType);
            var culture = m_InvariantCulture ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
            try { return System.Convert.ChangeType(value, targetType, culture); }
            catch { return GetDefault(targetType); }
        }

        // 反射-free 默认值：数值/枚举走 BCL（Convert.ChangeType / Enum.ToObject，IConvertible、IL2CPP 安全），
        // 取代 Activator.CreateInstance(runtimeType)；引用类型或不可转换类型回退 null。
        private static object GetDefault(Type t)
        {
            if (t == null || !t.IsValueType) return null;
            if (t.IsEnum) return Enum.ToObject(t, 0);
            try { return System.Convert.ChangeType(0, t, CultureInfo.InvariantCulture); }
            catch { return null; }
        }
    }
}
