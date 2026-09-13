//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 异步命令。执行期间 IsExecuting=true、CanExecute 自动返回 false（防重入），
    /// 任务结束后自动 RaiseCanExecuteChanged 让 binder 恢复 interactable。
    ///
    /// 业务侧典型用法：
    ///   LoginCommand = new AsyncRelayCommand(LoginAsync, () => !string.IsNullOrEmpty(Username));
    ///   private async Task LoginAsync() { await api.Login(Username, Password); ... }
    /// </summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> m_Execute;
        private readonly Func<bool> m_CanExecute;
        private FastEvent m_CanExecuteChanged;
        private bool m_IsExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            m_Execute = execute;
            m_CanExecute = canExecute;
        }

        public bool IsExecuting => m_IsExecuting;

        public bool CanExecute(object parameter)
        {
            if (m_IsExecuting) return false;
            return m_CanExecute == null || m_CanExecute();
        }

        public async void Execute(object parameter)
        {
            // async void 仅用于 ICommand 兼容；异常已被 try/catch 兜住，不会传到外层。
            if (!CanExecute(parameter)) return;
            m_IsExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await m_Execute();
            }
            catch (Exception ex)
            {
                FrameworkLog.Error("[AsyncRelayCommand] Execute threw: {0}", ex);
            }
            finally
            {
                m_IsExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event Action CanExecuteChanged
        {
            add { (m_CanExecuteChanged ??= new FastEvent()).Add(value); }
            remove { m_CanExecuteChanged?.Remove(value); }
        }

        public void RaiseCanExecuteChanged() { m_CanExecuteChanged?.Invoke("AsyncRelayCommand"); }
    }
}
