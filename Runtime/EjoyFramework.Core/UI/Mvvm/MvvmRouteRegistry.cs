//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI.Mvvm
{
    public readonly struct MvvmNoArguments
    {
    }

    public delegate TViewModel ScreenViewModelFactory<TViewModel, TArguments>(
        in TArguments arguments)
        where TViewModel : BindableObject
        where TArguments : struct;

    public delegate TViewModel DialogViewModelFactory<TViewModel, TArguments, TResult>(
        in TArguments arguments,
        DialogControl<TResult> dialog)
        where TViewModel : BindableObject
        where TArguments : struct
        where TResult : struct;

    public enum ExistingScreenPolicy
    {
        Refocus,
        Update,
        Replace,
        Reject,
    }

    public interface INavigationAware<TArguments> where TArguments : struct
    {
        void OnNavigatedTo(in TArguments arguments);
    }

    public readonly struct ScreenRoute<TViewModel, TArguments>
        where TViewModel : BindableObject
        where TArguments : struct
    {
        public ScreenRoute(int formDefId) : this(formDefId, formDefId) { }

        public ScreenRoute(int routeId, int formDefId)
        {
            if (routeId == 0) throw new ArgumentOutOfRangeException(nameof(routeId));
            if (formDefId == 0) throw new ArgumentOutOfRangeException(nameof(formDefId));
            RouteId = routeId;
            FormDefId = formDefId;
        }

        public int RouteId { get; }
        public int FormDefId { get; }
    }

    public readonly struct DialogRoute<TViewModel, TArguments, TResult>
        where TViewModel : BindableObject
        where TArguments : struct
        where TResult : struct
    {
        public DialogRoute(int formDefId) : this(formDefId, formDefId) { }

        public DialogRoute(int routeId, int formDefId)
        {
            if (routeId == 0) throw new ArgumentOutOfRangeException(nameof(routeId));
            if (formDefId == 0) throw new ArgumentOutOfRangeException(nameof(formDefId));
            RouteId = routeId;
            FormDefId = formDefId;
        }

        public int RouteId { get; }
        public int FormDefId { get; }
    }

    /// <summary>注册强类型 UI Route；参数、ViewModel 和结果契约在注册期确定。</summary>
    public sealed class MvvmRouteRegistry
    {
        private readonly UIFormRegistry m_FormRegistry;
        private readonly Dictionary<int, RouteDefinition> m_Definitions =
            new Dictionary<int, RouteDefinition>(32);

        public MvvmRouteRegistry(UIFormRegistry formRegistry)
        {
            m_FormRegistry = formRegistry ?? throw new ArgumentNullException(nameof(formRegistry));
        }

        public void Register<TViewModel, TArguments>(
            ScreenRoute<TViewModel, TArguments> route,
            ScreenViewModelFactory<TViewModel, TArguments> factory,
            ExistingScreenPolicy existingPolicy = ExistingScreenPolicy.Refocus)
            where TViewModel : BindableObject
            where TArguments : struct
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            UIFormDef formDef = m_FormRegistry.Get(route.FormDefId);
            if (formDef.Modal)
                throw new InvalidOperationException(Utility.Text.Format(
                    "UI form '{0}' is modal and must be registered as a DialogRoute.", route.FormDefId));
            Add(new ScreenDefinition<TViewModel, TArguments>(route, factory, existingPolicy));
        }

        public void RegisterDialog<TViewModel, TArguments, TResult>(
            DialogRoute<TViewModel, TArguments, TResult> route,
            DialogViewModelFactory<TViewModel, TArguments, TResult> factory,
            int initialResultCapacity = 4)
            where TViewModel : BindableObject
            where TArguments : struct
            where TResult : struct
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (initialResultCapacity < 1) throw new ArgumentOutOfRangeException(nameof(initialResultCapacity));
            UIFormDef formDef = m_FormRegistry.Get(route.FormDefId);
            if (!formDef.Modal)
                throw new InvalidOperationException(Utility.Text.Format(
                    "UI form '{0}' is not modal and cannot be registered as a DialogRoute.", route.FormDefId));
            Add(new DialogDefinition<TViewModel, TArguments, TResult>(
                route, factory, initialResultCapacity));
        }

        internal ScreenDefinition<TViewModel, TArguments> Get<TViewModel, TArguments>(
            ScreenRoute<TViewModel, TArguments> route)
            where TViewModel : BindableObject
            where TArguments : struct
        {
            RouteDefinition definition = GetDefinition(route.RouteId);
            var typed = definition as ScreenDefinition<TViewModel, TArguments>;
            if (typed == null || typed.FormDefId != route.FormDefId)
                throw ContractMismatch(route.RouteId, typeof(TViewModel), typeof(TArguments), null);
            return typed;
        }

        internal DialogDefinition<TViewModel, TArguments, TResult>
            Get<TViewModel, TArguments, TResult>(DialogRoute<TViewModel, TArguments, TResult> route)
            where TViewModel : BindableObject
            where TArguments : struct
            where TResult : struct
        {
            RouteDefinition definition = GetDefinition(route.RouteId);
            var typed = definition as DialogDefinition<TViewModel, TArguments, TResult>;
            if (typed == null || typed.FormDefId != route.FormDefId)
                throw ContractMismatch(route.RouteId, typeof(TViewModel), typeof(TArguments), typeof(TResult));
            return typed;
        }

        private void Add(RouteDefinition definition)
        {
            if (m_Definitions.ContainsKey(definition.RouteId))
                throw new InvalidOperationException(Utility.Text.Format(
                    "UI route '{0}' is already registered.", definition.RouteId));
            m_Definitions.Add(definition.RouteId, definition);
        }

        private RouteDefinition GetDefinition(int routeId)
        {
            RouteDefinition definition;
            if (!m_Definitions.TryGetValue(routeId, out definition))
                throw new KeyNotFoundException(Utility.Text.Format(
                    "UI route '{0}' is not registered.", routeId));
            return definition;
        }

        private static InvalidOperationException ContractMismatch(
            int routeId,
            Type viewModelType,
            Type argumentsType,
            Type resultType)
        {
            return new InvalidOperationException(Utility.Text.Format(
                "UI route '{0}' contract mismatch. ViewModel='{1}', Arguments='{2}', Result='{3}'.",
                routeId, viewModelType.FullName, argumentsType.FullName,
                resultType == null ? "none" : resultType.FullName));
        }

        internal abstract class RouteDefinition
        {
            protected RouteDefinition(int routeId, int formDefId)
            {
                RouteId = routeId;
                FormDefId = formDefId;
            }

            public int RouteId { get; }
            public int FormDefId { get; }
        }

        internal sealed class ScreenDefinition<TViewModel, TArguments> : RouteDefinition
            where TViewModel : BindableObject
            where TArguments : struct
        {
            private readonly ScreenViewModelFactory<TViewModel, TArguments> m_Factory;

            public ScreenDefinition(
                ScreenRoute<TViewModel, TArguments> route,
                ScreenViewModelFactory<TViewModel, TArguments> factory,
                ExistingScreenPolicy existingPolicy)
                : base(route.RouteId, route.FormDefId)
            {
                m_Factory = factory;
                ExistingPolicy = existingPolicy;
            }

            public ExistingScreenPolicy ExistingPolicy { get; }
            public TViewModel Create(in TArguments arguments) { return m_Factory(in arguments); }
        }

        internal sealed class DialogDefinition<TViewModel, TArguments, TResult> :
            RouteDefinition,
            IDialogResultSource<TResult>
            where TViewModel : BindableObject
            where TArguments : struct
            where TResult : struct
        {
            private struct ResultSlot
            {
                public DialogState State;
                public bool HasResult;
                public TResult Result;
                public string Error;
            }

            private readonly DialogViewModelFactory<TViewModel, TArguments, TResult> m_Factory;
            private readonly Dictionary<int, ResultSlot> m_Results;

            public DialogDefinition(
                DialogRoute<TViewModel, TArguments, TResult> route,
                DialogViewModelFactory<TViewModel, TArguments, TResult> factory,
                int initialResultCapacity)
                : base(route.RouteId, route.FormDefId)
            {
                m_Factory = factory;
                m_Results = new Dictionary<int, ResultSlot>(initialResultCapacity);
            }

            public void Begin(int serialId)
            {
                m_Results.Add(serialId, new ResultSlot { State = DialogState.Opening });
            }

            public TViewModel Create(
                in TArguments arguments,
                DialogControl<TResult> dialog)
            {
                return m_Factory(in arguments, dialog);
            }

            public DialogState GetState(int serialId)
            {
                ResultSlot slot;
                return m_Results.TryGetValue(serialId, out slot) ? slot.State : DialogState.Released;
            }

            public bool TryConsume(int serialId, out DialogCompletion<TResult> completion)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot) ||
                    (slot.State != DialogState.Completed &&
                     slot.State != DialogState.Canceled &&
                     slot.State != DialogState.Failed))
                {
                    completion = default;
                    return false;
                }

                completion = new DialogCompletion<TResult>(
                    slot.State, slot.HasResult, in slot.Result, slot.Error);
                m_Results.Remove(serialId);
                return true;
            }

            public void RequestComplete(UIRouter router, int serialId, in TResult result)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot) || !IsActive(slot.State)) return;
                slot.Result = result;
                slot.HasResult = true;
                slot.State = DialogState.Closing;
                m_Results[serialId] = slot;
                router.RequestClose(serialId);
            }

            public void RequestCancel(UIRouter router, int serialId)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot) || !IsActive(slot.State)) return;
                slot.State = DialogState.Closing;
                m_Results[serialId] = slot;
                router.RequestClose(serialId);
            }

            public void MarkOpened(int serialId)
            {
                SetState(serialId, DialogState.Opened);
            }

            public void MarkClosing(int serialId)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot) || !IsActive(slot.State)) return;
                slot.State = DialogState.Closing;
                m_Results[serialId] = slot;
            }

            public void MarkClosed(int serialId)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot)) return;
                slot.State = slot.HasResult ? DialogState.Completed : DialogState.Canceled;
                m_Results[serialId] = slot;
            }

            public void MarkFailed(int serialId, string error)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot)) return;
                slot.State = DialogState.Failed;
                slot.Error = error;
                m_Results[serialId] = slot;
            }

            public void Release(int serialId) { m_Results.Remove(serialId); }

            private void SetState(int serialId, DialogState state)
            {
                ResultSlot slot;
                if (!m_Results.TryGetValue(serialId, out slot)) return;
                slot.State = state;
                m_Results[serialId] = slot;
            }

            private static bool IsActive(DialogState state)
            {
                return state == DialogState.Opening || state == DialogState.Opened;
            }
        }
    }
}
