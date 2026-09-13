//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 实现该接口的对象能在属性变化时通知订阅者。
    /// 设计选择：用 Action&lt;string&gt; 而非标准 System.ComponentModel.INotifyPropertyChanged，
    /// 原因：(1) 没有 PropertyChangedEventArgs 的分配开销；(2) 框架内部统一回调签名；(3) 跨 AOT 平台稳定。
    /// 业务也可同时实现 INotifyPropertyChanged，但 binder 层只依赖本接口。
    /// </summary>
    public interface IBindable
    {
        /// <summary>属性变更通知。参数为属性名；空串表示"所有属性都可能变了"。</summary>
        event Action<IBindable, string> PropertyChanged;
    }
}
