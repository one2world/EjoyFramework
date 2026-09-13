//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Event
{
    /// <summary>
    /// 事件管理器接口。
    /// </summary>
    public interface IEventManager
    {
        /// <summary>
        /// 获取事件处理函数的数量。
        /// </summary>
        int EventHandlerCount { get; }

        /// <summary>
        /// 获取事件数量。
        /// </summary>
        int EventCount { get; }

        /// <summary>
        /// 获取事件处理函数的数量。
        /// </summary>
        /// <param name="id">事件类型编号。</param>
        /// <returns>事件处理函数的数量。</returns>
        int Count(int id);

        /// <summary>
        /// 检查是否存在事件处理函数。
        /// </summary>
        /// <param name="id">事件类型编号。</param>
        /// <param name="handler">要检查的事件处理函数。</param>
        /// <returns>是否存在事件处理函数。</returns>
        bool Check(int id, EventHandler<GameEventArgs> handler);

        /// <summary>
        /// 订阅事件处理函数。
        /// </summary>
        /// <param name="id">事件类型编号。</param>
        /// <param name="handler">要订阅的事件处理函数。</param>
        void Subscribe(int id, EventHandler<GameEventArgs> handler);

        /// <summary>
        /// 取消订阅事件处理函数。
        /// </summary>
        void Unsubscribe(int id, EventHandler<GameEventArgs> handler);

        /// <summary>
        /// 尝试取消订阅事件处理函数。不存在时返回 false 而非抛异常。
        /// 适合 GameObject.OnDestroy 路径调用，避免重复 Unsubscribe 崩溃。
        /// </summary>
        bool TryUnsubscribe(int id, EventHandler<GameEventArgs> handler);

        /// <summary>
        /// 设置默认事件处理函数。
        /// </summary>
        /// <param name="handler">要设置的默认事件处理函数。</param>
        void SetDefaultHandler(EventHandler<GameEventArgs> handler);

        /// <summary>
        /// 抛出事件，这个操作是线程安全的。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        void Fire(object sender, GameEventArgs e);

        /// <summary>
        /// 抛出事件立即模式，这个操作不是线程安全的。
        /// </summary>
        /// <param name="sender">事件源。</param>
        /// <param name="e">事件参数。</param>
        void FireNow(object sender, GameEventArgs e);
    }
}
