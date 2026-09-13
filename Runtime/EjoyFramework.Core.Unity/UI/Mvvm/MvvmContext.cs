//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 嵌套 DataContext 节点。挂在子树根节点上后，该子树内的 BinderBase 不再用 MvvmView 的 root VM，
    /// 而是用 MvvmContext.DataContext。典型场景：
    ///
    /// 1) 复用同一份 sub-prefab 渲染不同对象（例如玩家面板里嵌入装备槽 prefab，每个槽自己的 VM）
    /// 2) Master/Detail：一边列表 VM，一边详情用 SelectedItem 作为子 DataContext
    /// 3) ListBinder item view 内有多个 binder 共享同一 item DataContext —— ListBinder 已自动处理；
    ///    MvvmContext 用于非 list 的嵌套场景
    ///
    /// 使用：
    ///   ctx.DataContext = someBindableObject;   // 触发子树 binder 重新绑定
    ///   ctx.DataContext = null;                 // 解除子树绑定
    ///
    /// 设计选择：MvvmContext 不需要 MvvmView 配合。它自管子树 binder 的 Bind/Unbind。
    /// 当 MvvmContext 子树嵌套在 MvvmView 子树内时，MvvmView 把这部分 binder 跳过（避免双重绑定）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EjoyFramework/Core/UI/Context")]
    public sealed class MvvmContext : MonoBehaviour
    {
        [SerializeField, Tooltip("子树是否在 enable 时立即用当前 DataContext 重绑（默认 true）。")]
        private bool m_AutoBindOnEnable = true;

        private IBindable m_DataContext;
        private readonly List<BinderBase> m_Binders = new List<BinderBase>(8);
        private bool m_BindersIndexed;

        /// <summary>
        /// 当前子树绑定的 DataContext。setter 触发子树所有直接归属于本 context 的 binder 重新 Bind。
        /// </summary>
        public IBindable DataContext
        {
            get { return m_DataContext; }
            set
            {
                if (ReferenceEquals(m_DataContext, value)) return;
                m_DataContext = value;
                if (isActiveAndEnabled) ApplyToBinders();
            }
        }

        private void OnEnable()
        {
            if (m_AutoBindOnEnable && m_DataContext != null) ApplyToBinders();
        }

        private void OnDestroy()
        {
            // 拆除本 context 内 binder 的订阅 —— 注意只拆 owned binder，不影响外层 MvvmView。
            for (int i = 0; i < m_Binders.Count; i++)
            {
                var b = m_Binders[i];
                if (b != null) try { b.Unbind(); } catch (System.Exception ex) { FrameworkLog.Warning("Binder.Unbind threw on context destroy: {0}", ex); }
            }
            m_Binders.Clear();
        }

        // 判断 binder 是否归属于本 context（即在本 context 子树内，且没有更近的 MvvmContext 拦截）。
        // 仅内部用于 IndexBinders 过滤；MvvmView 自己走相同语义的本地 walk。
        private bool Owns(BinderBase binder)
        {
            if (binder == null) return false;
            var t = binder.transform;
            while (t != null && t != transform)
            {
                if (t.TryGetComponent<MvvmContext>(out var inner) && inner != this) return false;
                t = t.parent;
            }
            return t == transform;
        }

        private void IndexBinders()
        {
            if (m_BindersIndexed) return;
            m_Binders.Clear();
            var all = GetComponentsInChildren<BinderBase>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                if (Owns(all[i])) m_Binders.Add(all[i]);
            }
            m_BindersIndexed = true;
        }

        private void ApplyToBinders()
        {
            IndexBinders();
            for (int i = 0; i < m_Binders.Count; i++)
            {
                var b = m_Binders[i];
                if (b == null) continue;
                try { b.Bind(m_DataContext); }
                catch (System.Exception ex) { FrameworkLog.Error("[MvvmContext] Bind on {0} threw: {1}", b.GetType().Name, ex); }
            }
        }
    }
}
