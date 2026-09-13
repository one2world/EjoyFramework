//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 泛型 MVVM UIForm 基类。业务子类只需写：
    ///   - VM 类型（继承 BindableObject）
    ///   - CreateViewModel() 构造首次 VM
    ///   - 可选 override OnViewModelBound() 做一次性绑定（订阅 VM 事件等）
    ///
    /// 框架自动：
    ///   - OnFirstInit 时调用 CreateViewModel()，把 VM 注入 ViewModel 属性
    ///   - 自动遍历子节点所有 BinderBase 调 Bind(VM)
    ///   - OnClose 时反向 Unbind 所有 binder（不销毁 VM，便于复用）
    ///   - OnRecycle 时 ViewModel = null（彻底回归实例池）
    /// </summary>
    public abstract class MvvmView<TViewModel> : UIFormBehaviour where TViewModel : BindableObject
    {
        private TViewModel m_ViewModel;
        private readonly List<BinderBase> m_Binders = new List<BinderBase>(16);
        private bool m_BindersIndexed;

        /// <summary>当前 ViewModel。OnFirstInit 之后非 null。</summary>
        public TViewModel ViewModel { get { return m_ViewModel; } }

        /// <summary>业务必须实现：构造 VM 初态。可读 userData 做初始注入。</summary>
        protected abstract TViewModel CreateViewModel(object userData);

        /// <summary>VM 已绑定到所有子节点 binder 后的钩子。业务在此订阅 VM 事件 / 触发首次刷新。</summary>
        protected virtual void OnViewModelBound() { }

        /// <summary>VM 即将与 binder 解绑前的钩子。业务在此取消 VM 订阅。</summary>
        protected virtual void OnViewModelUnbinding() { }

        protected sealed override void OnFirstInit(object userData)
        {
            m_ViewModel = CreateViewModel(userData);
            IndexBinders();
            ApplyBinding(m_ViewModel);
            OnViewModelBound();
        }

        protected sealed override void OnReuseInit(object userData)
        {
            // 实例池复用：直接重新调 CreateViewModel 让业务给出新初态（也可在 override 里做 partial reset）。
            UnbindAll();
            m_ViewModel = CreateViewModel(userData);
            ApplyBinding(m_ViewModel);
            OnViewModelBound();
        }

        public override void OnClose(bool isShutdown, object userData)
        {
            OnViewModelUnbinding();
            UnbindAll();
            base.OnClose(isShutdown, userData);
        }

        public override void OnRecycle()
        {
            // 彻底回收（form 被关进 instance pool 等待下次 reuse）
            m_ViewModel = null;
            base.OnRecycle();
        }

        // ===== 内部 =====

        private void IndexBinders()
        {
            if (m_BindersIndexed) return;
            var all = GetComponentsInChildren<BinderBase>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                // 跳过被 MvvmContext 接管的子树 binder —— 它们由对应 MvvmContext 自管 Bind/Unbind。
                if (IsOwnedByNestedContext(all[i])) continue;
                m_Binders.Add(all[i]);
            }
            m_BindersIndexed = true;
        }

        private bool IsOwnedByNestedContext(BinderBase b)
        {
            var t = b.transform;
            while (t != null && t != transform)
            {
                if (t.TryGetComponent<MvvmContext>(out _)) return true;
                t = t.parent;
            }
            return false;
        }

        private void ApplyBinding(TViewModel vm)
        {
            for (int i = 0; i < m_Binders.Count; i++)
            {
                var b = m_Binders[i];
                if (b == null) continue;
                try { b.Bind(vm); }
                catch (Exception ex) { FrameworkLog.Error("[MvvmView] Binder {0}.Bind threw: {1}", b.GetType().Name, ex); }
            }
        }

        private void UnbindAll()
        {
            for (int i = 0; i < m_Binders.Count; i++)
            {
                var b = m_Binders[i];
                if (b == null) continue;
                try { b.Unbind(); }
                catch (Exception ex) { FrameworkLog.Error("[MvvmView] Binder {0}.Unbind threw: {1}", b.GetType().Name, ex); }
            }
        }
    }
}
