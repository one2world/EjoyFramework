//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.Event
{
    /// <summary>
    /// 事件管理器。
    /// </summary>
    internal sealed class EventManager : FrameworkModule, IEventManager
    {
        private readonly EventPool<GameEventArgs> m_EventPool;

        /// <summary>
        /// 初始化事件管理器的新实例。
        /// </summary>
        public EventManager()
        {
            m_EventPool = new EventPool<GameEventArgs>(EventPoolMode.AllowNoHandler | EventPoolMode.AllowMultiHandler);
        }

        /// <summary>
        /// 获取事件处理函数的数量。
        /// </summary>
        public int EventHandlerCount
        {
            get
            {
                return m_EventPool.EventHandlerCount;
            }
        }

        /// <summary>
        /// 获取事件数量。
        /// </summary>
        public int EventCount
        {
            get
            {
                return m_EventPool.EventCount;
            }
        }

        /// <summary>
        /// 获取游戏框架模块优先级。Priority 90：事件分发底座，仅次于 Coroutine。
        /// </summary>
        public override int Priority
        {
            get
            {
                return 90;
            }
        }

        /// <summary>
        /// 事件管理器轮询。
        /// </summary>
        public override void Update(float elapseSeconds, float realElapseSeconds)
        {
            m_EventPool.Update(elapseSeconds, realElapseSeconds);
        }

        /// <summary>
        /// 关闭并清理事件管理器。
        /// </summary>
        public override void Shutdown()
        {
            m_EventPool.Shutdown();
        }

        /// <summary>
        /// 获取事件处理函数的数量。
        /// </summary>
        public int Count(int id)
        {
            return m_EventPool.Count(id);
        }

        /// <summary>
        /// 检查是否存在事件处理函数。
        /// </summary>
        public bool Check(int id, EventHandler<GameEventArgs> handler)
        {
            return m_EventPool.Check(id, handler);
        }

        /// <summary>
        /// 订阅事件处理函数。
        /// </summary>
        public void Subscribe(int id, EventHandler<GameEventArgs> handler)
        {
            Framework.EnsureMainThread(nameof(Subscribe));
            m_EventPool.Subscribe(id, handler);
        }

        /// <summary>
        /// 取消订阅事件处理函数。
        /// </summary>
        public void Unsubscribe(int id, EventHandler<GameEventArgs> handler)
        {
            Framework.EnsureMainThread(nameof(Unsubscribe));
            m_EventPool.Unsubscribe(id, handler);
        }

        /// <summary>
        /// 尝试取消订阅。不存在则返回 false。
        /// </summary>
        public bool TryUnsubscribe(int id, EventHandler<GameEventArgs> handler)
        {
            Framework.EnsureMainThread(nameof(TryUnsubscribe));
            return m_EventPool.TryUnsubscribe(id, handler);
        }

        /// <summary>
        /// 设置默认事件处理函数。
        /// </summary>
        public void SetDefaultHandler(EventHandler<GameEventArgs> handler)
        {
            Framework.EnsureMainThread(nameof(SetDefaultHandler));
            m_EventPool.SetDefaultHandler(handler);
        }

        /// <summary>
        /// 抛出事件，线程安全（可从任意线程调用，事件在主线程 EventPool.Update 时派发）。
        /// </summary>
        public void Fire(object sender, GameEventArgs e)
        {
            // 故意不加 EnsureMainThread：Fire 设计上从工作线程入队是合法用法。
            m_EventPool.Fire(sender, e);
        }

        /// <summary>
        /// 抛出事件立即模式，非线程安全。
        /// </summary>
        public void FireNow(object sender, GameEventArgs e)
        {
            Framework.EnsureMainThread(nameof(FireNow));
            m_EventPool.FireNow(sender, e);
        }
    }
}
