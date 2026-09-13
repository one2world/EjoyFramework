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
    /// VM IObservableList → 子节点 GameObject 列表。性能：
    ///   - 监听粒度变化事件，仅对 (Inserted/Removed/Replaced/Moved) 做局部更新
    ///   - 仅 Reset 才整体重建
    ///   - 容器下的 item 实例不在游戏 ObjectPool 系统里循环（保持简单），未来可拓展
    ///
    /// 业务侧：
    ///   1. 准备 item prefab（任意子节点，可在 prefab root 上加 IListItemBinder 实现接口 Bind(item)）
    ///   2. ListBinder.m_ItemPrefab 指向该 prefab，m_Container 指向放 item 的 RectTransform（默认本 transform）
    ///   3. VM 改 ObservableList 时本组件自动 sync
    /// </summary>
    [AddComponentMenu("EjoyFramework/Core/UI/Binders/List")]
    public sealed class ListBinder : BinderBase
    {
        [SerializeField] private GameObject m_ItemPrefab;
        [SerializeField] private Transform m_Container;

        private IObservableList m_BoundList;
        private readonly List<ViewEntry> m_Views = new List<ViewEntry>();

        private struct ViewEntry
        {
            public GameObject Go;
            public IListItemBinder ItemBinder;
            public BinderBase[] SubBinders;
        }

        protected override void OnUnbind()
        {
            DetachList();
            ClearViews();
        }

        protected override void ApplyValue(object value)
        {
            var nextList = value as IObservableList;
            if (ReferenceEquals(m_BoundList, nextList)) return;

            DetachList();
            ClearViews();
            m_BoundList = nextList;
            if (m_BoundList == null) return;
            AttachList();
            RebuildAll();
        }

        protected override Type GetSourceType() { return typeof(IObservableList); }

        private void AttachList()
        {
            m_BoundList.ItemInserted += OnInserted;
            m_BoundList.ItemRemoved += OnRemoved;
            m_BoundList.ItemReplaced += OnReplaced;
            m_BoundList.ItemMoved += OnMoved;
            m_BoundList.Reset += OnReset;
        }

        private void DetachList()
        {
            if (m_BoundList == null) return;
            m_BoundList.ItemInserted -= OnInserted;
            m_BoundList.ItemRemoved -= OnRemoved;
            m_BoundList.ItemReplaced -= OnReplaced;
            m_BoundList.ItemMoved -= OnMoved;
            m_BoundList.Reset -= OnReset;
            m_BoundList = null;
        }

        private Transform GetContainer() { return m_Container != null ? m_Container : transform; }

        private void RebuildAll()
        {
            ClearViews();
            if (m_BoundList == null || m_ItemPrefab == null) return;
            for (int i = 0; i < m_BoundList.Count; i++) AppendView(m_BoundList.GetItem(i));
        }

        private void ClearViews()
        {
            for (int i = 0; i < m_Views.Count; i++)
            {
                DestroyView(m_Views[i]);
            }
            m_Views.Clear();
        }

        private void AppendView(object itemData) { InsertView(m_Views.Count, itemData); }

        private void InsertView(int index, object itemData)
        {
            if (m_ItemPrefab == null) { FrameworkLog.Warning("[ListBinder] item prefab not set."); return; }
            var go = Instantiate(m_ItemPrefab, GetContainer());
            go.transform.SetSiblingIndex(index);
            var entry = CreateEntry(go);
            m_Views.Insert(index, entry);
            ApplyItemData(ref entry, itemData);
            m_Views[index] = entry;
        }

        private ViewEntry CreateEntry(GameObject viewGo)
        {
            var entry = new ViewEntry
            {
                Go = viewGo,
                ItemBinder = viewGo.GetComponent<IListItemBinder>()
            };

            if (entry.ItemBinder == null)
            {
                entry.SubBinders = viewGo.GetComponentsInChildren<BinderBase>(includeInactive: true);
            }

            return entry;
        }

        private void ApplyItemData(ref ViewEntry entry, object data)
        {
            if (entry.ItemBinder != null)
            {
                try { entry.ItemBinder.OnBindListItem(data); }
                catch (Exception ex) { FrameworkLog.Error("[ListBinder] item Bind threw: {0}", ex); }
                return;
            }

            // 如 itemData 自己是 BindableObject，把它当作 nested DataContext 传给 item 内的 binder
            if (data is IBindable bindable)
            {
                var subBinders = entry.SubBinders;
                if (subBinders == null || subBinders.Length == 0) return;
                for (int i = 0; i < subBinders.Length; i++)
                {
                    if (subBinders[i] == null) continue;
                    try { subBinders[i].Bind(bindable); }
                    catch (Exception ex) { FrameworkLog.Error("[ListBinder] sub-binder Bind threw: {0}", ex); }
                }
                return;
            }

            UnbindSubBinders(entry.SubBinders);
        }

        private void UnbindSubBinders(BinderBase[] subBinders)
        {
            if (subBinders == null) return;
            for (int i = 0; i < subBinders.Length; i++)
            {
                if (subBinders[i] == null) continue;
                try { subBinders[i].Unbind(); }
                catch (Exception ex) { FrameworkLog.Error("[ListBinder] sub-binder Unbind threw: {0}", ex); }
            }
        }

        private void DestroyView(ViewEntry entry)
        {
            UnbindSubBinders(entry.SubBinders);
            if (entry.Go == null) return;
            if (Application.isPlaying) Destroy(entry.Go);
            else DestroyImmediate(entry.Go);
        }

        // ===== 事件 =====
        private void OnInserted(int index, object item) { InsertView(index, item); }

        private void OnRemoved(int index, object item)
        {
            if (index < 0 || index >= m_Views.Count) return;
            var entry = m_Views[index];
            m_Views.RemoveAt(index);
            DestroyView(entry);
        }

        private void OnReplaced(int index, object oldItem, object newItem)
        {
            if (index < 0 || index >= m_Views.Count) return;
            var entry = m_Views[index];
            ApplyItemData(ref entry, newItem);
            m_Views[index] = entry;
        }

        private void OnMoved(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= m_Views.Count) return;
            if (newIndex < 0 || newIndex >= m_Views.Count) return;

            var entry = m_Views[oldIndex];
            m_Views.RemoveAt(oldIndex);
            m_Views.Insert(newIndex, entry);
            if (entry.Go != null) entry.Go.transform.SetSiblingIndex(newIndex);
        }

        private void OnReset() { RebuildAll(); }
    }
}
