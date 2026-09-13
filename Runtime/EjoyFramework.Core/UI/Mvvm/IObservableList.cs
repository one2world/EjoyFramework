//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.UI.Mvvm
{
    /// <summary>
    /// 可观察集合的非泛型接口 —— 让 binder 能不知道 T 也订阅。
    /// 设计：粒度变化事件（Inserted/Removed/Replaced/Moved/Reset），便于 ListBinder 做增量更新而非全量重建。
    /// </summary>
    public interface IObservableList
    {
        event Action<int, object> ItemInserted;
        event Action<int, object> ItemRemoved;
        event Action<int, object, object> ItemReplaced;        // index, oldItem, newItem
        event Action<int, int> ItemMoved;                       // oldIndex, newIndex
        event Action Reset;                                     // 大变化 / Clear

        int Count { get; }
        object GetItem(int index);
    }

    /// <summary>泛型版本 —— 业务直接用这个。</summary>
    public interface IObservableList<T> : IObservableList, IList<T>
    {
        new event Action<int, T> ItemInserted;
        new event Action<int, T> ItemRemoved;
        new event Action<int, T, T> ItemReplaced;
    }
}
