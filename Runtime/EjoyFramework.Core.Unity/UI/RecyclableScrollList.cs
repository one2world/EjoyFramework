//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 虚拟列表（垂直方向）。仅渲染可视区内的 cell，复用同一池子的 GameObject。
    /// 适合 100+ 行的列表（背包、好友列表、聊天历史 等）。
    ///
    /// 使用方式：
    ///   1) 给 ScrollRect.content 挂上 RecyclableScrollList
    ///   2) 设 cellPrefab + cellHeight + spacing
    ///   3) 调 SetData(IReadOnlyList&lt;TData&gt;, BindAction&lt;Cell, TData&gt;)
    /// 业务的 Cell 自带 OnBind(data) 即可。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("EjoyFramework/Core/UI/Recyclable Scroll List")]
    public sealed class RecyclableScrollList : MonoBehaviour
    {
        [SerializeField] private ScrollRect m_ScrollRect;
        [SerializeField] private GameObject m_CellPrefab;
        [SerializeField] private float m_CellHeight = 100f;
        [SerializeField] private float m_Spacing = 0f;
        [SerializeField] private int m_BufferCount = 2;

        private RectTransform m_Content;
        private RectTransform m_Viewport;
        private readonly List<RecycleCell> m_ActiveCells = new List<RecycleCell>();
        private readonly Stack<RecycleCell> m_Pool = new Stack<RecycleCell>();
        private System.Collections.IList m_Data;
        private Action<RecycleCell, int, object> m_Binder;
        private int m_FirstVisibleIndex = -1;
        private int m_LastVisibleIndex = -1;

        internal struct RecycleCell
        {
            public GameObject Go;
            public RectTransform Rt;
            public int DataIndex;
            public Component CellComponent;
        }

        private void Awake()
        {
            m_Content = m_ScrollRect.content;
            m_Viewport = m_ScrollRect.viewport != null ? m_ScrollRect.viewport : (RectTransform)m_ScrollRect.transform;
            m_ScrollRect.onValueChanged.AddListener(_ => Refresh());
        }

        /// <summary>
        /// 数据 + binder。binder 签名：(cell-MonoBehaviour-or-null, dataIndex, dataItem) → 由业务在 cell 上设值。
        /// 如果 cellPrefab 上挂了实现 IRecyclableCell 的脚本，binder 可传 null（自动调用 cell.OnBind）。
        /// </summary>
        internal void SetData(System.Collections.IList data, Action<RecycleCell, int, object> binder)
        {
            m_Data = data;
            m_Binder = binder;
            // 重置：所有 cell 回池
            for (int i = 0; i < m_ActiveCells.Count; i++)
            {
                m_ActiveCells[i].Go.SetActive(false);
                m_Pool.Push(m_ActiveCells[i]);
            }
            m_ActiveCells.Clear();
            m_FirstVisibleIndex = -1;
            m_LastVisibleIndex = -1;

            // 调整 content 高度
            int n = data?.Count ?? 0;
            float total = n * m_CellHeight + Math.Max(0, n - 1) * m_Spacing;
            var size = m_Content.sizeDelta;
            size.y = total;
            m_Content.sizeDelta = size;
            m_Content.anchoredPosition = Vector2.zero;

            Refresh();
        }

        /// <summary>滚动到指定 index 的 cell。</summary>
        public void ScrollToIndex(int index)
        {
            if (m_Data == null || m_Data.Count == 0) return;
            index = Mathf.Clamp(index, 0, m_Data.Count - 1);
            float y = index * (m_CellHeight + m_Spacing);
            var pos = m_Content.anchoredPosition;
            pos.y = y;
            m_Content.anchoredPosition = pos;
            Refresh();
        }

        /// <summary>仅刷新所有可视 cell（数据本身改了，业务调）。</summary>
        public void NotifyDataChanged()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (m_Data == null || m_CellPrefab == null) return;

            // 计算可视区 [first, last]
            float scrollY = m_Content.anchoredPosition.y;
            float viewportH = m_Viewport.rect.height;
            float pitch = m_CellHeight + m_Spacing;
            int first = Mathf.Max(0, Mathf.FloorToInt(scrollY / pitch) - m_BufferCount);
            int last = Mathf.Min(m_Data.Count - 1, Mathf.CeilToInt((scrollY + viewportH) / pitch) + m_BufferCount);

            if (first == m_FirstVisibleIndex && last == m_LastVisibleIndex)
            {
                return; // 无变化
            }

            // 回收：active 中不在 [first,last] 的
            for (int i = m_ActiveCells.Count - 1; i >= 0; i--)
            {
                int di = m_ActiveCells[i].DataIndex;
                if (di < first || di > last)
                {
                    var c = m_ActiveCells[i];
                    c.Go.SetActive(false);
                    m_Pool.Push(c);
                    m_ActiveCells.RemoveAt(i);
                }
            }

            // 补齐：[first,last] 中尚无 active 的
            for (int di = first; di <= last; di++)
            {
                bool found = false;
                for (int i = 0; i < m_ActiveCells.Count; i++)
                {
                    if (m_ActiveCells[i].DataIndex == di) { found = true; break; }
                }
                if (found) continue;

                RecycleCell cell;
                if (m_Pool.Count > 0)
                {
                    cell = m_Pool.Pop();
                }
                else
                {
                    var go = Instantiate(m_CellPrefab, m_Content);
                    cell = new RecycleCell
                    {
                        Go = go,
                        Rt = (RectTransform)go.transform,
                        CellComponent = go.GetComponent(typeof(IRecyclableCell)) as Component,
                    };
                }
                cell.DataIndex = di;
                cell.Rt.anchorMin = new Vector2(0, 1);
                cell.Rt.anchorMax = new Vector2(1, 1);
                cell.Rt.pivot = new Vector2(0.5f, 1f);
                cell.Rt.anchoredPosition = new Vector2(0, -di * pitch);
                cell.Rt.sizeDelta = new Vector2(0, m_CellHeight);
                cell.Go.SetActive(true);

                object item = m_Data[di];
                if (m_Binder != null)
                {
                    try { m_Binder(cell, di, item); }
                    catch (Exception ex) { FrameworkLog.Error("RecyclableScrollList binder threw at index {0}: {1}", di, ex); }
                }
                else if (cell.CellComponent is IRecyclableCell rc)
                {
                    try { rc.OnBind(di, item); }
                    catch (Exception ex) { FrameworkLog.Error("RecyclableScrollList cell.OnBind threw at index {0}: {1}", di, ex); }
                }

                m_ActiveCells.Add(cell);
            }

            m_FirstVisibleIndex = first;
            m_LastVisibleIndex = last;
        }
    }

    /// <summary>
    /// 业务 cell 实现此接口即可被 RecyclableScrollList 自动调用。
    /// </summary>
    public interface IRecyclableCell
    {
        void OnBind(int index, object data);
    }
}
