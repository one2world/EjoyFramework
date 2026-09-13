//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 可观察列表实现。提供细粒度变更事件（Inserted/Removed/Replaced/Moved/Reset），
    /// 让 ListBinder 做最小化 DOM/视图增量更新。
    ///
    /// 性能：内部一个 List&lt;T&gt; backing，Add/Insert/Remove 均 O(n)（与 List 相同）。
    /// 批量更新场景请用 ReplaceRange / Reset / using (BeginBatch())：
    ///   单事件触发，避免 binder 重复重排。
    /// </summary>
    public sealed class ObservableList<T> : IObservableList<T>
    {
        private readonly List<T> m_Items;
        private int m_BatchDepth;
        private bool m_BatchHasChanges;

        // 泛型事件
        private FastEvent<int, T> m_TypedInserted;
        private FastEvent<int, T> m_TypedRemoved;
        private FastEvent<int, T, T> m_TypedReplaced;

        // 非泛型事件（IObservableList）
        private FastEvent<int, object> m_BoxedInserted;
        private FastEvent<int, object> m_BoxedRemoved;
        private FastEvent<int, object, object> m_BoxedReplaced;
        private FastEvent<int, int> m_Moved;
        private FastEvent m_Reset;

        public ObservableList() { m_Items = new List<T>(); }
        public ObservableList(int capacity) { m_Items = new List<T>(capacity); }
        public ObservableList(IEnumerable<T> seed) { m_Items = new List<T>(seed); }

        // ===== 泛型事件 =====
        event Action<int, T> IObservableList<T>.ItemInserted
        {
            add { (m_TypedInserted ??= new FastEvent<int, T>()).Add(value); }
            remove { m_TypedInserted?.Remove(value); }
        }
        event Action<int, T> IObservableList<T>.ItemRemoved
        {
            add { (m_TypedRemoved ??= new FastEvent<int, T>()).Add(value); }
            remove { m_TypedRemoved?.Remove(value); }
        }
        event Action<int, T, T> IObservableList<T>.ItemReplaced
        {
            add { (m_TypedReplaced ??= new FastEvent<int, T, T>()).Add(value); }
            remove { m_TypedReplaced?.Remove(value); }
        }

        // ===== 非泛型事件 =====
        event Action<int, object> IObservableList.ItemInserted
        {
            add { (m_BoxedInserted ??= new FastEvent<int, object>()).Add(value); }
            remove { m_BoxedInserted?.Remove(value); }
        }
        event Action<int, object> IObservableList.ItemRemoved
        {
            add { (m_BoxedRemoved ??= new FastEvent<int, object>()).Add(value); }
            remove { m_BoxedRemoved?.Remove(value); }
        }
        event Action<int, object, object> IObservableList.ItemReplaced
        {
            add { (m_BoxedReplaced ??= new FastEvent<int, object, object>()).Add(value); }
            remove { m_BoxedReplaced?.Remove(value); }
        }
        public event Action<int, int> ItemMoved
        {
            add { (m_Moved ??= new FastEvent<int, int>()).Add(value); }
            remove { m_Moved?.Remove(value); }
        }
        public event Action Reset
        {
            add { (m_Reset ??= new FastEvent()).Add(value); }
            remove { m_Reset?.Remove(value); }
        }

        // ===== IList<T> =====
        public int Count => m_Items.Count;
        public bool IsReadOnly => false;

        public T this[int index]
        {
            get { return m_Items[index]; }
            set
            {
                var old = m_Items[index];
                if (EqualityComparer<T>.Default.Equals(old, value)) return;
                m_Items[index] = value;
                RaiseReplaced(index, old, value);
            }
        }

        public void Add(T item)
        {
            int idx = m_Items.Count;
            m_Items.Add(item);
            RaiseInserted(idx, item);
        }

        public void Insert(int index, T item)
        {
            m_Items.Insert(index, item);
            RaiseInserted(index, item);
        }

        public bool Remove(T item)
        {
            int idx = m_Items.IndexOf(item);
            if (idx < 0) return false;
            RemoveAt(idx);
            return true;
        }

        public void RemoveAt(int index)
        {
            var item = m_Items[index];
            m_Items.RemoveAt(index);
            RaiseRemoved(index, item);
        }

        public void Clear()
        {
            if (m_Items.Count == 0) return;
            m_Items.Clear();
            RaiseReset();
        }

        public void Move(int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex) return;
            if (oldIndex < 0 || oldIndex >= m_Items.Count) throw new ArgumentOutOfRangeException(nameof(oldIndex));
            if (newIndex < 0 || newIndex >= m_Items.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            var item = m_Items[oldIndex];
            m_Items.RemoveAt(oldIndex);
            m_Items.Insert(newIndex, item);
            RaiseMoved(oldIndex, newIndex);
        }

        /// <summary>整列替换（典型场景：服务器拉到新数据）。触发单次 Reset 而非 n 次 Inserted。</summary>
        public void ReplaceAll(IEnumerable<T> source)
        {
            m_Items.Clear();
            if (source != null) m_Items.AddRange(source);
            RaiseReset();
        }

        /// <summary>
        /// Synchronizes this list to a source list by stable keys.
        /// Existing items are updated in place, so UI binders can keep their instantiated views.
        /// Structural changes still emit fine-grained insert/remove/move events.
        /// </summary>
        public void SyncWith<TSource, TKey>(
            IList<TSource> source,
            Func<T, TKey> itemKeySelector,
            Func<TSource, TKey> sourceKeySelector,
            Func<TSource, T> createItem,
            Action<T, TSource> updateItem)
        {
            if (itemKeySelector == null) throw new ArgumentNullException(nameof(itemKeySelector));
            if (sourceKeySelector == null) throw new ArgumentNullException(nameof(sourceKeySelector));
            if (createItem == null) throw new ArgumentNullException(nameof(createItem));

            if (source == null)
            {
                Clear();
                return;
            }

            var comparer = EqualityComparer<TKey>.Default;
            for (int targetIndex = 0; targetIndex < source.Count; targetIndex++)
            {
                var sourceItem = source[targetIndex];
                var sourceKey = sourceKeySelector(sourceItem);
                int existingIndex = FindIndexByKey(targetIndex, sourceKey, itemKeySelector, comparer);
                if (existingIndex >= 0)
                {
                    if (existingIndex != targetIndex) Move(existingIndex, targetIndex);
                    updateItem?.Invoke(m_Items[targetIndex], sourceItem);
                    continue;
                }

                Insert(targetIndex, createItem(sourceItem));
            }

            for (int i = m_Items.Count - 1; i >= source.Count; i--) RemoveAt(i);
        }

        public bool Contains(T item) { return m_Items.Contains(item); }
        public int IndexOf(T item) { return m_Items.IndexOf(item); }
        public void CopyTo(T[] array, int arrayIndex) { m_Items.CopyTo(array, arrayIndex); }
        public List<T>.Enumerator GetEnumerator() { return m_Items.GetEnumerator(); }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return m_Items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return m_Items.GetEnumerator(); }

        // ===== IObservableList（非泛型）=====
        object IObservableList.GetItem(int index) { return m_Items[index]; }

        // ===== 批量模式 =====
        /// <summary>
        /// 进入批量模式：批量内的 Add/Insert/Remove/Replace 不触发事件，
        /// 退出最外层批量时合并触发一次 Reset。适合大批量初始化或多步骤交易。
        /// 支持嵌套（depth-counted）；仅在最外层 EndBatch 时清 hasChanges 并触发。
        /// </summary>
        public BatchScope BeginBatch() { m_BatchDepth++; return new BatchScope(this); }

        public readonly struct BatchScope : IDisposable
        {
            private readonly ObservableList<T> m_Owner;
            internal BatchScope(ObservableList<T> owner) { m_Owner = owner; }
            public void Dispose() { m_Owner?.EndBatch(); }
        }

        private void EndBatch()
        {
            if (m_BatchDepth <= 0) return;
            m_BatchDepth--;
            if (m_BatchDepth == 0 && m_BatchHasChanges)
            {
                m_BatchHasChanges = false;
                RaiseResetCore();
            }
        }

        // ===== 内部触发 —— FastEvent 快照遍历，通知期无数组分配并隔离每个 handler =====
        private const string TraceTag = "ObservableList";

        private void RaiseInserted(int idx, T item)
        {
            if (m_BatchDepth > 0) { m_BatchHasChanges = true; return; }
            m_TypedInserted?.Invoke(idx, item, TraceTag);
            m_BoxedInserted?.Invoke(idx, item, TraceTag);
        }
        private void RaiseRemoved(int idx, T item)
        {
            if (m_BatchDepth > 0) { m_BatchHasChanges = true; return; }
            m_TypedRemoved?.Invoke(idx, item, TraceTag);
            m_BoxedRemoved?.Invoke(idx, item, TraceTag);
        }
        private void RaiseReplaced(int idx, T oldItem, T newItem)
        {
            if (m_BatchDepth > 0) { m_BatchHasChanges = true; return; }
            m_TypedReplaced?.Invoke(idx, oldItem, newItem, TraceTag);
            m_BoxedReplaced?.Invoke(idx, oldItem, newItem, TraceTag);
        }
        private void RaiseMoved(int oldIdx, int newIdx)
        {
            if (m_BatchDepth > 0) { m_BatchHasChanges = true; return; }
            m_Moved?.Invoke(oldIdx, newIdx, TraceTag);
        }
        private void RaiseReset()
        {
            if (m_BatchDepth > 0) { m_BatchHasChanges = true; return; }
            RaiseResetCore();
        }
        private void RaiseResetCore() { m_Reset?.Invoke(TraceTag); }

        private int FindIndexByKey<TKey>(
            int startIndex,
            TKey key,
            Func<T, TKey> keySelector,
            EqualityComparer<TKey> comparer)
        {
            for (int i = startIndex; i < m_Items.Count; i++)
            {
                if (comparer.Equals(keySelector(m_Items[i]), key)) return i;
            }
            return -1;
        }
    }
}
