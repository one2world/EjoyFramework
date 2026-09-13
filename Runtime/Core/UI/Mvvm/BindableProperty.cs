//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 独立的可观察属性容器。适用场景：
    ///   - VM 想暴露单一值（不需要写 SetField 样板）
    ///   - 跨 VM 共享同一属性（多个 VM 持有同一 BindableProperty 引用）
    ///   - 业务 model 不想继承 BindableObject 时
    ///
    /// 设计：value 类型与引用类型都用 EqualityComparer.Default 去重，避免无意义触发。
    /// 不实现 IBindable —— 它是单值容器，签名是 Action&lt;T&gt;（强类型），不需要属性名。
    /// </summary>
    public sealed class BindableProperty<T>
    {
        private T m_Value;
        private FastEvent<T> m_ValueChanged;

        public BindableProperty() { m_Value = default; }
        public BindableProperty(T initial) { m_Value = initial; }

        public T Value
        {
            get { return m_Value; }
            set
            {
                if (EqualityComparer<T>.Default.Equals(m_Value, value)) return;
                m_Value = value;
                Raise(value);
            }
        }

        /// <summary>订阅值变化。订阅时不立即触发；如需初始值同步，业务自取 .Value。</summary>
        public event Action<T> ValueChanged
        {
            add { (m_ValueChanged ??= new FastEvent<T>()).Add(value); }
            remove { m_ValueChanged?.Remove(value); }
        }

        /// <summary>强制触发一次通知，即使值未变。常用：内部状态修改后通知 UI 重绘。</summary>
        public void ForceNotify() { Raise(m_Value); }

        private void Raise(T value)
        {
            m_ValueChanged?.Invoke(value, "BindableProperty<" + typeof(T).Name + ">");
        }

        public static implicit operator T(BindableProperty<T> p) { return p == null ? default : p.m_Value; }
    }
}
