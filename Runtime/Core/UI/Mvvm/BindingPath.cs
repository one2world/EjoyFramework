//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 属性路径解析器。支持 "User.Profile.Name" 这种嵌套 path。
    ///
    /// 设计选择：每个 path 段独立用 PropertyAccessor 取值；
    /// 中间任一段为 null 则整体返回 null（null-safe）。
    /// 暂不支持索引器（list[0]）—— 列表场景请用 ListBinder。
    /// </summary>
    public sealed class BindingPath
    {
        private readonly string[] m_Segments;
        private readonly Type[] m_CachedGetterOwnerTypes;
        private readonly Func<object, object>[] m_CachedGetters;
        private Type m_CachedSetterOwnerType;
        private Action<object, object> m_CachedSetter;

        public string Raw { get; }

        public BindingPath(string raw)
        {
            Raw = raw ?? string.Empty;
            m_Segments = string.IsNullOrEmpty(raw)
                ? Array.Empty<string>()
                : raw.Split('.');
            m_CachedGetterOwnerTypes = m_Segments.Length == 0 ? Array.Empty<Type>() : new Type[m_Segments.Length];
            m_CachedGetters = m_Segments.Length == 0 ? Array.Empty<Func<object, object>>() : new Func<object, object>[m_Segments.Length];
        }

        public int SegmentCount => m_Segments.Length;

        /// <summary>路径上首段的属性名 —— binder 订阅 PropertyChanged 时仅关心首段（嵌套段不监听）。</summary>
        public string RootSegment => m_Segments.Length == 0 ? string.Empty : m_Segments[0];

        /// <summary>从 root 起按段逐步取值。任何一段 null 立刻返回 null。</summary>
        public object Resolve(object root)
        {
            if (root == null || m_Segments.Length == 0) return root;
            object current = root;
            for (int i = 0; i < m_Segments.Length; i++)
            {
                if (current == null) return null;
                var get = GetCachedGetter(i, current.GetType());
                if (get == null) return null;
                current = get(current);
            }
            return current;
        }

        /// <summary>
        /// 把值写回 path 末端。中间段为 null 写不进，返回 false。
        /// 仅在 TwoWay/OneWayToSource 模式被 binder 调用。
        /// </summary>
        public bool TryAssign(object root, object value)
        {
            if (root == null || m_Segments.Length == 0) return false;

            object current = root;
            for (int i = 0; i < m_Segments.Length - 1; i++)
            {
                if (current == null) return false;
                var get = GetCachedGetter(i, current.GetType());
                if (get == null) return false;
                current = get(current);
            }
            if (current == null) return false;

            var setter = GetCachedSetter(current.GetType());
            if (setter == null) return false;
            setter(current, value);
            return true;
        }

        private Func<object, object> GetCachedGetter(int segmentIndex, Type ownerType)
        {
            if (segmentIndex < 0 || segmentIndex >= m_Segments.Length) return null;
            if (m_CachedGetterOwnerTypes[segmentIndex] == ownerType) return m_CachedGetters[segmentIndex];

            var getter = PropertyAccessor.GetGetter(ownerType, m_Segments[segmentIndex]);
            m_CachedGetterOwnerTypes[segmentIndex] = ownerType;
            m_CachedGetters[segmentIndex] = getter;
            return getter;
        }

        private Action<object, object> GetCachedSetter(Type ownerType)
        {
            if (m_CachedSetterOwnerType == ownerType) return m_CachedSetter;

            m_CachedSetterOwnerType = ownerType;
            m_CachedSetter = PropertyAccessor.GetSetter(ownerType, m_Segments[m_Segments.Length - 1]);
            return m_CachedSetter;
        }

        public override string ToString() => Raw;
    }
}
