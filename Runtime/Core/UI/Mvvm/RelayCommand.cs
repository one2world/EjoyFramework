//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>无参同步命令。CanExecute 为 null 时恒可执行。</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action m_Execute;
        private readonly Func<bool> m_CanExecute;
        private FastEvent m_CanExecuteChanged;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            m_Execute = execute;
            m_CanExecute = canExecute;
        }

        public bool CanExecute(object parameter) { return m_CanExecute == null || m_CanExecute(); }

        public void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;
            try { m_Execute(); }
            catch (Exception ex) { FrameworkLog.Error("[RelayCommand] Execute threw: {0}", ex); }
        }

        public event Action CanExecuteChanged
        {
            add { (m_CanExecuteChanged ??= new FastEvent()).Add(value); }
            remove { m_CanExecuteChanged?.Remove(value); }
        }

        /// <summary>业务条件改变后调用，刷新 binder 的 interactable 状态。</summary>
        public void RaiseCanExecuteChanged() { m_CanExecuteChanged?.Invoke("RelayCommand"); }
    }

    /// <summary>带参同步命令。Binder 调用 Execute(arg) 时把强类型 arg 转给 T。</summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> m_Execute;
        private readonly Func<T, bool> m_CanExecute;
        private FastEvent m_CanExecuteChanged;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            m_Execute = execute;
            m_CanExecute = canExecute;
        }

        public bool CanExecute(object parameter) { return m_CanExecute == null || m_CanExecute(Cast(parameter)); }

        public void Execute(object parameter)
        {
            var typed = Cast(parameter);
            if (!CanExecute(parameter)) return;
            try { m_Execute(typed); }
            catch (Exception ex) { FrameworkLog.Error("[RelayCommand<{0}>] Execute threw: {1}", typeof(T).Name, ex); }
        }

        public event Action CanExecuteChanged
        {
            add { (m_CanExecuteChanged ??= new FastEvent()).Add(value); }
            remove { m_CanExecuteChanged?.Remove(value); }
        }

        public void RaiseCanExecuteChanged()
        {
            m_CanExecuteChanged?.Invoke("RelayCommand<" + typeof(T).Name + ">");
        }

        private static T Cast(object parameter)
        {
            if (parameter == null) return default;
            if (parameter is T typed) return typed;
            FrameworkLog.Warning("[RelayCommand<{0}>] parameter type mismatch: got {1}", typeof(T).Name, parameter.GetType().Name);
            return default;
        }
    }
}
