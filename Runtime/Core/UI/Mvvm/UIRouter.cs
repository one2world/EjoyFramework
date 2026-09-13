//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI.Mvvm
{
    public enum NavigationState
    {
        Opening,
        Opened,
        Closing,
        Closed,
        Failed,
    }

    public enum DialogState
    {
        Opening,
        Opened,
        Closing,
        Completed,
        Canceled,
        Failed,
        Released,
    }

    public readonly struct DialogCompletion<TResult> where TResult : struct
    {
        internal DialogCompletion(
            DialogState state,
            bool hasResult,
            in TResult result,
            string error)
        {
            State = state;
            HasResult = hasResult;
            Result = result;
            Error = error;
        }

        public DialogState State { get; }
        public bool HasResult { get; }
        public TResult Result { get; }
        public string Error { get; }
    }

    public readonly struct ScreenHandle<TViewModel> where TViewModel : BindableObject
    {
        private readonly UIRouter m_Router;

        internal ScreenHandle(UIRouter router, int serialId, TViewModel viewModel)
        {
            m_Router = router;
            SerialId = serialId;
            ViewModel = viewModel;
        }

        public int SerialId { get; }
        public TViewModel ViewModel { get; }
        public NavigationState State => m_Router.GetNavigationState(SerialId);
    }

    public readonly struct DialogHandle<TResult> where TResult : struct
    {
        private readonly IDialogResultSource<TResult> m_Source;

        internal DialogHandle(IDialogResultSource<TResult> source, int serialId)
        {
            m_Source = source;
            SerialId = serialId;
        }

        public int SerialId { get; }
        public DialogState State => m_Source == null
            ? DialogState.Released
            : m_Source.GetState(SerialId);

        public bool TryConsume(out DialogCompletion<TResult> completion)
        {
            if (m_Source != null) return m_Source.TryConsume(SerialId, out completion);
            completion = default;
            return false;
        }
    }

    public readonly struct DialogControl<TResult> where TResult : struct
    {
        private readonly UIRouter m_Router;
        private readonly IDialogResultSource<TResult> m_Source;

        internal DialogControl(
            UIRouter router,
            IDialogResultSource<TResult> source,
            int serialId)
        {
            m_Router = router;
            m_Source = source;
            SerialId = serialId;
        }

        public int SerialId { get; }

        public void Complete(in TResult result)
        {
            if (m_Source == null) throw new InvalidOperationException("Dialog control is not initialized.");
            m_Source.RequestComplete(m_Router, SerialId, in result);
        }

        public void Cancel()
        {
            if (m_Source == null) throw new InvalidOperationException("Dialog control is not initialized.");
            m_Source.RequestCancel(m_Router, SerialId);
        }
    }

    internal interface IDialogRouteState
    {
        void MarkOpened(int serialId);
        void MarkClosing(int serialId);
        void MarkClosed(int serialId);
        void MarkFailed(int serialId, string error);
        void Release(int serialId);
    }

    internal interface IDialogResultSource<TResult> : IDialogRouteState where TResult : struct
    {
        DialogState GetState(int serialId);
        bool TryConsume(int serialId, out DialogCompletion<TResult> completion);
        void RequestComplete(UIRouter router, int serialId, in TResult result);
        void RequestCancel(UIRouter router, int serialId);
    }

    /// <summary>无 object 参数传递、无 Task 的强类型 UI 路由器。</summary>
    public sealed class UIRouter : IDisposable
    {
        private readonly UIController m_Controller;
        private readonly MvvmRouteRegistry m_Registry;
        private readonly Dictionary<int, BindableObject> m_ViewModels =
            new Dictionary<int, BindableObject>(16);
        private readonly Dictionary<int, NavigationState> m_NavigationStates =
            new Dictionary<int, NavigationState>(16);
        private readonly Dictionary<int, IDialogRouteState> m_Dialogs =
            new Dictionary<int, IDialogRouteState>(8);
        private bool m_Disposed;
        private bool m_IsReady;

        public UIRouter(
            UIController controller,
            MvvmRouteRegistry registry,
            bool isReady = true)
        {
            m_Controller = controller ?? throw new ArgumentNullException(nameof(controller));
            m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_IsReady = isReady;
            m_Controller.FormOpened += OnFormOpened;
            m_Controller.FormOpenFailed += OnFormOpenFailed;
            m_Controller.FormClosing += OnFormClosing;
            m_Controller.FormClosed += OnFormClosed;
        }

        public ScreenHandle<TViewModel> Open<TViewModel, TArguments>(
            ScreenRoute<TViewModel, TArguments> route,
            in TArguments arguments)
            where TViewModel : BindableObject
            where TArguments : struct
        {
            ThrowIfUnavailable();
            MvvmRouteRegistry.ScreenDefinition<TViewModel, TArguments> definition =
                m_Registry.Get(route);
            UIFormDef formDef = m_Controller.Registry.Get(definition.FormDefId);
            UIStack stack = m_Controller.GetStack(formDef.GroupName);
            UIStack.StackEntry existing = formDef.AllowMultiple
                ? default
                : stack.FindByFormDefId(definition.FormDefId);
            if (existing.IsValid)
                return HandleExisting(definition, existing, in arguments);

            TViewModel viewModel = definition.Create(in arguments);
            EnsureViewModel(viewModel);
            int serialId = m_Controller.Manager.PeekNextOpenUIFormSerial();
            m_ViewModels.Add(serialId, viewModel);
            m_NavigationStates[serialId] = NavigationState.Opening;
            UIStack.StackEntry entry = m_Controller.Open(definition.FormDefId);
            EnsureSerial(serialId, entry.SerialId);
            return new ScreenHandle<TViewModel>(this, serialId, viewModel);
        }

        public DialogHandle<TResult> Show<TViewModel, TArguments, TResult>(
            DialogRoute<TViewModel, TArguments, TResult> route,
            in TArguments arguments)
            where TViewModel : BindableObject
            where TArguments : struct
            where TResult : struct
        {
            ThrowIfUnavailable();
            MvvmRouteRegistry.DialogDefinition<TViewModel, TArguments, TResult> definition =
                m_Registry.Get(route);
            UIFormDef formDef = m_Controller.Registry.Get(definition.FormDefId);
            UIStack stack = m_Controller.GetStack(formDef.GroupName);
            if (!formDef.AllowMultiple && stack.FindByFormDefId(definition.FormDefId).IsValid)
                throw new InvalidOperationException(Utility.Text.Format(
                    "Dialog route '{0}' is already open. Keep and reuse its DialogHandle.", route.RouteId));

            int serialId = m_Controller.Manager.PeekNextOpenUIFormSerial();
            definition.Begin(serialId);
            try
            {
                var control = new DialogControl<TResult>(this, definition, serialId);
                TViewModel viewModel = definition.Create(in arguments, control);
                EnsureViewModel(viewModel);
                m_ViewModels.Add(serialId, viewModel);
                m_NavigationStates[serialId] = NavigationState.Opening;
                m_Dialogs.Add(serialId, definition);
                UIStack.StackEntry entry = m_Controller.Open(definition.FormDefId);
                EnsureSerial(serialId, entry.SerialId);
                return new DialogHandle<TResult>(definition, serialId);
            }
            catch
            {
                m_ViewModels.Remove(serialId);
                m_NavigationStates.Remove(serialId);
                m_Dialogs.Remove(serialId);
                definition.Release(serialId);
                throw;
            }
        }

        public bool TryResolve<TViewModel>(int serialId, out TViewModel viewModel)
            where TViewModel : BindableObject
        {
            BindableObject value;
            if (m_ViewModels.TryGetValue(serialId, out value) && value is TViewModel typed)
            {
                viewModel = typed;
                return true;
            }
            viewModel = null;
            return false;
        }

        public bool IsReady { get { return m_IsReady && !m_Disposed; } }

        public void SetReady()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(UIRouter));
            m_IsReady = true;
        }

        public bool Back(string groupName) { ThrowIfUnavailable(); return m_Controller.Back(groupName); }
        public bool Back() { ThrowIfUnavailable(); return m_Controller.Back(); }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_IsReady = false;
            m_Controller.FormOpened -= OnFormOpened;
            m_Controller.FormOpenFailed -= OnFormOpenFailed;
            m_Controller.FormClosing -= OnFormClosing;
            m_Controller.FormClosed -= OnFormClosed;
            foreach (KeyValuePair<int, IDialogRouteState> pair in m_Dialogs)
                pair.Value.MarkClosed(pair.Key);
            m_Dialogs.Clear();
            m_ViewModels.Clear();
            m_NavigationStates.Clear();
        }

        internal void RequestClose(int serialId)
        {
            ThrowIfUnavailable();
            m_Controller.CloseUIForm(serialId);
        }

        internal NavigationState GetNavigationState(int serialId)
        {
            NavigationState state;
            return m_NavigationStates.TryGetValue(serialId, out state)
                ? state
                : NavigationState.Closed;
        }

        private ScreenHandle<TViewModel> HandleExisting<TViewModel, TArguments>(
            MvvmRouteRegistry.ScreenDefinition<TViewModel, TArguments> definition,
            UIStack.StackEntry existing,
            in TArguments arguments)
            where TViewModel : BindableObject
            where TArguments : struct
        {
            TViewModel viewModel;
            if (!TryResolve(existing.SerialId, out viewModel))
                throw new InvalidOperationException(Utility.Text.Format(
                    "UI form '{0}' is open outside its registered route.", definition.FormDefId));

            switch (definition.ExistingPolicy)
            {
                case ExistingScreenPolicy.Refocus:
                    break;
                case ExistingScreenPolicy.Update:
                    var aware = viewModel as INavigationAware<TArguments>;
                    if (aware == null)
                        throw new InvalidOperationException(Utility.Text.Format(
                            "ViewModel '{0}' must implement INavigationAware<{1}> for Update policy.",
                            typeof(TViewModel).FullName, typeof(TArguments).FullName));
                    aware.OnNavigatedTo(in arguments);
                    break;
                case ExistingScreenPolicy.Replace:
                    m_Controller.CloseUIForm(existing.SerialId);
                    return Open(
                        new ScreenRoute<TViewModel, TArguments>(definition.RouteId, definition.FormDefId),
                        in arguments);
                case ExistingScreenPolicy.Reject:
                    throw new InvalidOperationException(Utility.Text.Format(
                        "UI route '{0}' is already open.", definition.RouteId));
                default:
                    throw new ArgumentOutOfRangeException();
            }

            m_Controller.Open(definition.FormDefId);
            return new ScreenHandle<TViewModel>(this, existing.SerialId, viewModel);
        }

        private void OnFormOpened(int serialId)
        {
            if (m_NavigationStates.ContainsKey(serialId))
                m_NavigationStates[serialId] = NavigationState.Opened;
            IDialogRouteState dialog;
            if (m_Dialogs.TryGetValue(serialId, out dialog)) dialog.MarkOpened(serialId);
        }

        private void OnFormOpenFailed(int serialId, string error)
        {
            m_NavigationStates[serialId] = NavigationState.Failed;
            m_ViewModels.Remove(serialId);
            IDialogRouteState dialog;
            if (m_Dialogs.TryGetValue(serialId, out dialog))
            {
                m_Dialogs.Remove(serialId);
                dialog.MarkFailed(serialId, error);
            }
        }

        private void OnFormClosing(int serialId)
        {
            if (m_NavigationStates.ContainsKey(serialId))
                m_NavigationStates[serialId] = NavigationState.Closing;
            IDialogRouteState dialog;
            if (m_Dialogs.TryGetValue(serialId, out dialog))
            {
                dialog.MarkClosing(serialId);
                if (!m_Controller.HasUIForm(serialId)) FinalizeClose(serialId, dialog);
            }
        }

        private void OnFormClosed(int serialId)
        {
            IDialogRouteState dialog;
            m_Dialogs.TryGetValue(serialId, out dialog);
            FinalizeClose(serialId, dialog);
        }

        private void FinalizeClose(int serialId, IDialogRouteState dialog)
        {
            if (dialog != null)
            {
                m_Dialogs.Remove(serialId);
                dialog.MarkClosed(serialId);
            }
            m_ViewModels.Remove(serialId);
            m_NavigationStates.Remove(serialId);
        }

        private static void EnsureViewModel<TViewModel>(TViewModel viewModel)
            where TViewModel : BindableObject
        {
            if (viewModel == null)
                throw new InvalidOperationException(Utility.Text.Format(
                    "UI route factory for '{0}' returned null.", typeof(TViewModel).FullName));
        }

        private static void EnsureSerial(int expected, int actual)
        {
            if (expected != actual)
                throw new FrameworkException(Utility.Text.Format(
                    "UI serial changed during route open. Expected '{0}', actual '{1}'.", expected, actual));
        }

        private void ThrowIfUnavailable()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(UIRouter));
            if (!m_IsReady)
                throw new InvalidOperationException(
                    "UIRouter is not ready. Register routes now and navigate after UIComponent.Start completes.");
        }
    }
}
