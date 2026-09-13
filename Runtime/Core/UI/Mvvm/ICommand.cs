//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 命令接口。Binder（ButtonBinder 等）调用 CanExecute 控制可交互态，调用 Execute 触发动作。
    /// 设计：参数走 object —— 兼容无参 / 有参；强类型场景用 RelayCommand&lt;T&gt;。
    /// </summary>
    public interface ICommand
    {
        bool CanExecute(object parameter);
        void Execute(object parameter);
        event Action CanExecuteChanged;
    }
}
