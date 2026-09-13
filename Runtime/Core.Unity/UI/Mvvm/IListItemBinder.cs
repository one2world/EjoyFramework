//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

namespace EjoyFramework.Core.Unity
{
    /// <summary>
    /// 实现该接口的 item view（挂在 ListBinder 的 ItemPrefab root 上）在
    /// <see cref="ListBinder"/> 注入 item data 时收到回调。
    ///
    /// 选择本接口而非把 itemData 当 nested DataContext 的好处：
    ///   - itemData 不需要继承 BindableObject（DTO / 普通 POCO 也能用）
    ///   - 0 反射，性能更高
    ///   - 业务可在 OnBindListItem 内做一次性自定义渲染（动画 / 排版）
    ///
    /// 若 itemData 本身是 BindableObject，<see cref="ListBinder"/> 在
    /// item view 未实现本接口时，自动 fallback 到把 itemData 作为子节点 binder 的 DataContext。
    /// </summary>
    public interface IListItemBinder
    {
        /// <summary>每次 itemData 变化（Insert / Replace）调用。Remove 时不会回调。</summary>
        void OnBindListItem(object itemData);
    }
}
