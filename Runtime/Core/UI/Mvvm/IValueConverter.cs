//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 绑定值转换器。例如：bool → 字符串 "ON" / "OFF"，int → "x100" 格式化字符串，enum → Sprite。
    ///
    /// 用法：把实现该接口的 MonoBehaviour 拖到 binder 的 Converter 字段上。
    /// 仅 OneWay 用 Convert；TwoWay 时 ConvertBack 用于 View → VM 反向写。
    /// </summary>
    public interface IValueConverter
    {
        object Convert(object value, Type targetType, object parameter);
        object ConvertBack(object value, Type targetType, object parameter);
    }
}
