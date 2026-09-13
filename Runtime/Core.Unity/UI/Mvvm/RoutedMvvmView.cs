//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;

namespace EjoyFramework.Core.Unity
{
    /// <summary>通过 UI SerialId 获取强类型 ViewModel，不使用 userData 传递参数。</summary>
    public abstract class RoutedMvvmView<TViewModel> : MvvmView<TViewModel>
        where TViewModel : BindableObject
    {
        protected sealed override TViewModel CreateViewModel(object userData)
        {
            UIComponent ui = GameEntry.UI;
            if (ui == null || ui.Router == null)
                throw new InvalidOperationException("UIRouter is not initialized.");

            TViewModel viewModel;
            if (!ui.Router.TryResolve(SerialId, out viewModel))
                throw new InvalidOperationException(Utility.Text.Format(
                    "No routed ViewModel '{0}' exists for UI serial '{1}'.",
                    typeof(TViewModel).FullName, SerialId));
            return viewModel;
        }
    }
}
