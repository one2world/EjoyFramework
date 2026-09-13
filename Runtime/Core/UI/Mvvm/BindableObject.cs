//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// ViewModel / 普通可绑定 model 的基类。提供 SetField helper —— 业务子类用
    /// <c>SetField(ref m_X, value)</c> 即可完成"判等 + 赋值 + 通知"。CallerMemberName 让属性名零开销自动填入。
    ///
    /// 线程：PropertyChanged 在 Setter 所在线程回调。Unity 业务请仅在主线程修改。
    /// </summary>
    public abstract class BindableObject : IBindable
    {
        private FastEvent<IBindable, string> m_PropertyChanged;

        public event Action<IBindable, string> PropertyChanged
        {
            add { (m_PropertyChanged ??= new FastEvent<IBindable, string>()).Add(value); }
            remove { m_PropertyChanged?.Remove(value); }
        }

        /// <summary>
        /// 标准 setter helper：判等不变则什么也不做；变则赋值后触发 PropertyChanged。
        /// 返回是否实际修改 —— 业务可基于返回值做联动（如重算派生属性）。
        /// </summary>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 显式触发某个属性变化通知。业务在派生属性场景（A 变 → B 也变）下使用。
        /// 传 null / 空串表示 "所有属性都可能变了"，binder 应当全量刷新。
        ///
        /// 隔离遍历：单个订阅者抛异常不会阻止其它订阅者收到通知（默认 multicast 是一抛全停）。
        /// </summary>
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            m_PropertyChanged?.Invoke(this, propertyName ?? string.Empty, "BindableObject");
        }

        /// <summary>
        /// 创建一个挂在本 VM 上的零 GC 文本属性，并把它的内容变更转发成本 VM 的属性通知。
        /// 用法：<c>public BindableText Hp { get; }</c>，构造函数里 <c>Hp = CreateText();</c> —— CallerMemberName
        /// 在构造函数里拿不到属性名，因此赋值给属性时需显式传入名字，或在属性 getter 侧初始化。
        /// 之所以由 VM 转发而不是让 binder 直接订阅 BindableText：BinderBase 只订阅 DataContext 的
        /// PropertyChanged，且 BindableText 实例引用终生不变，靠 SetField 无法触发通知。
        ///
        /// 重要：新增 BindableText 属性后必须重跑 MVVM accessor codegen（MvvmAccessorGenerator）。
        /// 玩家构建没有反射兜底，未登记的成员 <see cref="PropertyAccessor.GetGetter"/> 返回 null，
        /// 该绑定会静默失效——编辑器里一切正常、真机上 UI 不动，是最难排查的一类问题。
        /// </summary>
        /// <param name="propertyName">对外暴露该文本的属性名。</param>
        /// <param name="capacity">初始缓冲容量，0 表示延迟到首次写入。</param>
        /// <returns>已完成通知转发的可绑定文本。</returns>
        protected BindableText CreateText(string propertyName, int capacity = 0)
        {
            BindableText text = new BindableText(capacity);
            text.PropertyChanged += (sender, name) => RaisePropertyChanged(propertyName);
            return text;
        }

        /// <summary>批量通知：当一次操作改了多个属性时一次性通知，避免多次刷新。</summary>
        protected void RaisePropertyChangedMany(params string[] propertyNames)
        {
            if (propertyNames == null) return;
            for (int i = 0; i < propertyNames.Length; i++) RaisePropertyChanged(propertyNames[i]);
        }
    }
}
