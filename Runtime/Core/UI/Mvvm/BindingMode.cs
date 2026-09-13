//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 绑定方向。
    /// </summary>
    public enum BindingMode
    {
        /// <summary>VM → View（默认）。最常见：文本显示、图片显示。</summary>
        OneWay = 0,

        /// <summary>View ↔ VM 双向。InputField / Toggle / Slider 等可交互控件。</summary>
        TwoWay = 1,

        /// <summary>绑定时同步一次后不再监听变化（性能优化用于静态值）。</summary>
        OneTime = 2,

        /// <summary>View → VM（仅源更新；如把 InputField 的值同步到 VM 不反向更新）。罕见，但完整性提供。</summary>
        OneWayToSource = 3,
    }
}
