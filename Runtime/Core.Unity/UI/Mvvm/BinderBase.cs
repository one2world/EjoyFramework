//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using EjoyFramework.Core.UI.Mvvm;
using UnityEngine;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 所有 binder 的抽象基类。子类只需 override <see cref="ApplyValue"/> 把 VM 值写到 View。
    /// 生命周期：OnEnable → Subscribe；OnDisable → Unsubscribe。安全可重入。
    ///
    /// 工作流：
    ///   1. MvvmView / MvvmContext 在 SetDataContext 时遍历子节点 BinderBase 调 Bind(vm)
    ///   2. binder 解析 path 上的 root 段，订阅 VM.PropertyChanged
    ///   3. 收到通知后用 BindingPath.Resolve 取最新值 → ApplyValue 写到 View
    ///   4. SetDataContext(null) 时取消订阅
    /// </summary>
    public abstract class BinderBase : MonoBehaviour
    {
        [SerializeField, Tooltip("VM 属性路径，支持嵌套（User.Profile.Name）。")]
        protected string m_PropertyPath;

        [SerializeField, Tooltip("绑定方向。")]
        protected BindingMode m_Mode = BindingMode.OneWay;

        [SerializeField, Tooltip("可选 IValueConverter MonoBehaviour 实现。挂在同一 GameObject 或 ViewModel root 上。")]
        protected MonoBehaviour m_Converter;

        [SerializeField, Tooltip("Converter 用的参数（converter 自行解析）。")]
        protected string m_ConverterParameter;

        protected BindingPath m_CompiledPath;
        protected IBindable m_DataContext;
        private bool m_Subscribed;

        public string PropertyPath
        {
            get { return m_PropertyPath; }
            set { m_PropertyPath = value; m_CompiledPath = null; if (m_DataContext != null) RefreshFromSource(); }
        }
        public BindingMode Mode { get { return m_Mode; } set { m_Mode = value; } }

        /// <summary>由 MvvmView 调用：替换 DataContext 并完整建立绑定（含 UI 事件 hook）。</summary>
        public void Bind(IBindable dataContext)
        {
            // Bind 是幂等的：先彻底拆除旧上下文（含 OnUnbind 钩子的 UI 事件 unhook），再建立新的。
            Unbind();
            m_DataContext = dataContext;
            if (m_DataContext == null) return;
            if (m_CompiledPath == null) m_CompiledPath = new BindingPath(m_PropertyPath ?? string.Empty);

            NormalizeMode();
            SubscribeToSource();
            OnBound();           // 子类 hook UI 事件（onClick / onValueChanged 等）
            RefreshFromSource();
        }

        /// <summary>由 MvvmView 调用：完整拆除绑定（清空 DataContext + 触发子类 unhook UI 事件）。</summary>
        public void Unbind()
        {
            UnsubscribeFromSource();
            if (m_DataContext != null) OnUnbind();
            m_DataContext = null;
        }

        // ===== MonoBehaviour 生命周期 =====
        // 关键：OnDisable 不调 Unbind。UIForm.OnPause 会 SetActive(false)，进而触发本组件的 OnDisable。
        // 若此时 Unbind 则 OnResume 时无人重建绑定 → UI 死掉。所以仅在 OnDestroy 做真正拆除。
        // PropertyChanged 即使在 GameObject 关闭期间也会照常收到 —— 写入 inactive 的 UI 无害，
        // 表单重新 Active 时 UI 已是最新状态，避免一次额外 RefreshFromSource。
        protected virtual void OnDestroy() { Unbind(); }

        /// <summary>
        /// 子类是否支持 View -> VM 反向写。只读展示型 binder（文本、图片等）override 返回 false。
        /// </summary>
        protected virtual bool SupportsSourceWrites { get { return true; } }

        /// <summary>
        /// 纠正配错的反向绑定模式。必须在 SubscribeToSource 之前、且在 Bind 里做，不能放 Awake：
        /// ListBinder.CreateEntry 用 includeInactive:true 收集子 binder 并直接 Bind，未激活节点的 Awake
        /// 尚未执行；等它后来激活时再改 m_Mode 已经太晚，没有人会回头补订阅，表现为初值正常、后续更新全丢。
        /// 对不支持反向写的 binder，反向模式还会让 BinderBase 跳过订阅，绑定直接是死的，
        /// 所以这里在玩家构建也纠正（好过留一个死绑定），仅告警是编辑期的。
        /// </summary>
        private void NormalizeMode()
        {
            if (SupportsSourceWrites) return;
            if (m_Mode != BindingMode.TwoWay && m_Mode != BindingMode.OneWayToSource) return;
#if UNITY_EDITOR
            FrameworkLog.Warning("[{0}] '{1}' is configured as {2}, but this binder is display-only. Falling back to OneWay.",
                GetType().Name, name, m_Mode);
#endif
            m_Mode = BindingMode.OneWay;
        }

        // 子类钩子
        protected virtual void OnBound() { }
        protected virtual void OnUnbind() { }

        private void SubscribeToSource()
        {
            if (m_DataContext == null || m_Subscribed) return;
            if (m_Mode == BindingMode.OneTime || m_Mode == BindingMode.OneWayToSource) return;
            m_DataContext.PropertyChanged += OnPropertyChanged;
            m_Subscribed = true;
        }

        private void UnsubscribeFromSource()
        {
            if (m_DataContext == null || !m_Subscribed) return;
            m_DataContext.PropertyChanged -= OnPropertyChanged;
            m_Subscribed = false;
        }

        /// <summary>把当前 VM 值写入 View。子类必须实现。</summary>
        protected abstract void ApplyValue(object value);

        /// <summary>
        /// View → VM 反向写。仅当 m_Mode = TwoWay / OneWayToSource 时由子类的事件回调调用。
        /// </summary>
        protected void PushToSource(object value)
        {
            if (m_DataContext == null || m_CompiledPath == null) return;
            if (m_Mode != BindingMode.TwoWay && m_Mode != BindingMode.OneWayToSource) return;
            object writeValue = value;
            if (m_Converter is IValueConverter conv)
            {
                writeValue = conv.ConvertBack(value, GetSourceType(), m_ConverterParameter);
            }
            try { m_CompiledPath.TryAssign(m_DataContext, writeValue); }
            catch (Exception ex) { FrameworkLog.Error("[{0}] PushToSource '{1}' threw: {2}", GetType().Name, m_PropertyPath, ex); }
        }

        protected virtual Type GetSourceType() { return typeof(object); }

        private void OnPropertyChanged(IBindable sender, string propertyName)
        {
            // path 为空 → 任何变化都重读；否则只匹配首段（嵌套属性的中间节点变化也会经由 PropertyChanged 冒泡）
            if (m_CompiledPath == null) return;
            if (string.IsNullOrEmpty(propertyName)
                || m_CompiledPath.RootSegment == propertyName
                || m_CompiledPath.SegmentCount == 0)
            {
                RefreshFromSource();
            }
        }

        protected void RefreshFromSource()
        {
            if (m_DataContext == null || m_CompiledPath == null) return;
            object raw;
            try { raw = m_CompiledPath.Resolve(m_DataContext); }
            catch (Exception ex) { FrameworkLog.Error("[{0}] Resolve '{1}' threw: {2}", GetType().Name, m_PropertyPath, ex); return; }
            object cooked = raw;
            if (m_Converter is IValueConverter conv)
            {
                try { cooked = conv.Convert(raw, GetSourceType(), m_ConverterParameter); }
                catch (Exception ex) { FrameworkLog.Error("[{0}] Converter.Convert threw: {1}", GetType().Name, ex); }
            }
            try { ApplyValue(cooked); }
            catch (Exception ex) { FrameworkLog.Error("[{0}] ApplyValue threw: {1}", GetType().Name, ex); }
        }
    }
}
