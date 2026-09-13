//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EjoyFramework.Core
{
    /// <summary>
    /// 事件池。
    /// 修复：
    ///   1) Update 用双队列 swap，分发期间锁完全释放（生产者不阻塞）。
    ///   2) HandleEvent 对 handler 列表 snapshot 后再迭代，handler 内 Subscribe/Unsubscribe 不破坏当前轮派发。
    ///   3) 单个 handler 抛异常被 try-catch 隔离，不影响后续 handler。
    ///   4) 删除死代码 m_DefaultHandlers 字段。
    ///   5) 提供 TryUnsubscribe（不存在的 handler 返回 false）。
    /// </summary>
    internal sealed partial class EventPool<T> where T : BaseEventArgs
    {
        // 用于 ToArray-style snapshot 的临时缓冲池：每次 dispatch 借出，结束归还。
        // 这里不放在 [ThreadStatic] 上——EventPool 实例化按 Manager 一对一，主线程使用。
        // 提供给 HandleEvent 的 handlers 数组用 thread-local 池避免分配。
        [ThreadStatic]
        private static EventHandler<T>[] s_HandlerScratch;
        // 重入深度：handler 内同步调用 FireNow 会重入 HandleEvent。
        // depth==0 时使用共享 s_HandlerScratch（零分配快路径）；
        // depth>0（嵌套）时为本次派发分配独立数组，避免内层覆盖/清空外层正在迭代的 scratch。
        [ThreadStatic]
        private static int s_Depth;

        private readonly MultiDictionary<int, EventHandler<T>> m_EventHandlers;
        // 双队列 swap：m_PendingEvents 由 Fire 写入，m_DispatchEvents 由 Update 消费。
        // 不能锁 m_PendingEvents 本身：Update 会交换该字段，等待旧锁的生产者可能随后写入新队列。
        private readonly object m_QueueLock = new object();
        private Queue<Event> m_PendingEvents;
        private Queue<Event> m_DispatchEvents;
        private readonly EventPoolMode m_EventPoolMode;
        private EventHandler<T> m_DefaultHandler;

        public EventPool(EventPoolMode mode)
        {
            m_EventHandlers = new MultiDictionary<int, EventHandler<T>>();
            m_PendingEvents = new Queue<Event>();
            m_DispatchEvents = new Queue<Event>();
            m_EventPoolMode = mode;
            m_DefaultHandler = null;
        }

        /// <summary>
        /// 已订阅的处理器**总数**（跨所有事件 ID 求和；不是事件 ID 种类数）。
        /// O(K) where K = 事件 ID 种类数；用于调试/统计，非热点路径。
        /// </summary>
        public int EventHandlerCount
        {
            get
            {
                int total = 0;
                foreach (var kv in m_EventHandlers)
                {
                    total += kv.Value.Count;
                }
                return total;
            }
        }

        public int EventCount
        {
            get
            {
                lock (m_QueueLock) { return m_PendingEvents.Count; }
            }
        }

        public void Update(float elapseSeconds, float realElapseSeconds)
        {
            // 1) swap 队列：持锁仅一瞬。
            Queue<Event> toDispatch;
            lock (m_QueueLock)
            {
                if (m_PendingEvents.Count == 0)
                {
                    return;
                }
                toDispatch = m_PendingEvents;
                m_PendingEvents = m_DispatchEvents;
                m_DispatchEvents = toDispatch;
            }

            // 2) 锁外分发，生产者线程可继续 Fire 到 m_PendingEvents。
            while (toDispatch.Count > 0)
            {
                Event eventNode = toDispatch.Dequeue();
                HandleEvent(eventNode.Sender, eventNode.EventArgs);
                ReferencePool.Release(eventNode);
            }
        }

        public void Shutdown()
        {
            Clear();
            // m_EventHandlers 不允许被外部操作，直接清。
            // 注：Update 与 Shutdown 在同一主线程。
            // 取消所有订阅前快照所有 key，避免迭代修改。
            // 这里直接 Clear。
            ClearAllHandlers();
            m_DefaultHandler = null;
        }

        public void Clear()
        {
            // 逐个出队并释放池化的 Event 结点与其 EventArgs；直接 Clear() 会丢弃这些引用 → 泄漏。
            // 关停期其他模块仍可能 Fire（事件入 m_PendingEvents 但 Update 不再运行），此处必须回收。
            lock (m_QueueLock)
            {
                while (m_PendingEvents.Count > 0)
                {
                    Event node = m_PendingEvents.Dequeue();
                    T args = node.EventArgs;
                    if (args != null) ReferencePool.Release(args);   // Event.Clear 不会释放内嵌 args
                    ReferencePool.Release(node);
                }
            }
            // 注：单线程下 Shutdown 不会打断 Update，故 m_DispatchEvents 此刻必为空，无需处理。
        }

        private void ClearAllHandlers()
        {
            // MultiDictionary 没有 keys 枚举……先一次性 Clear。
            m_EventHandlers.Clear();
        }

        public int Count(int id)
        {
            GameLinkedListRange<EventHandler<T>> range = default(GameLinkedListRange<EventHandler<T>>);
            if (m_EventHandlers.TryGetValue(id, out range))
            {
                return range.Count;
            }

            return 0;
        }

        public bool Check(int id, EventHandler<T> handler)
        {
            if (handler == null)
            {
                throw new FrameworkException("Event handler is invalid.");
            }

            return m_EventHandlers.Contains(id, handler);
        }

        public void Subscribe(int id, EventHandler<T> handler)
        {
            if (handler == null)
            {
                throw new FrameworkException("Event handler is invalid.");
            }

            if (!m_EventPoolMode.HasFlag(EventPoolMode.AllowMultiHandler))
            {
                GameLinkedListRange<EventHandler<T>> range = default(GameLinkedListRange<EventHandler<T>>);
                if (m_EventHandlers.TryGetValue(id, out range) && range.Count > 0)
                {
                    throw new FrameworkException(Utility.Text.Format("Event '{0}' not allow multi handler.", id));
                }
            }

            if (!m_EventPoolMode.HasFlag(EventPoolMode.AllowDuplicateHandler))
            {
                if (Check(id, handler))
                {
                    throw new FrameworkException(Utility.Text.Format("Event '{0}' not allow duplicate handler.", id));
                }
            }

            m_EventHandlers.Add(id, handler);
        }

        public void Unsubscribe(int id, EventHandler<T> handler)
        {
            if (handler == null)
            {
                throw new FrameworkException("Event handler is invalid.");
            }

            if (!m_EventHandlers.Remove(id, handler))
            {
                throw new FrameworkException(Utility.Text.Format("Event '{0}' not exists specified handler.", id));
            }
        }

        /// <summary>
        /// 尝试取消订阅。不存在时返回 false 而非抛异常，适合 GameObject 销毁路径调用。
        /// </summary>
        public bool TryUnsubscribe(int id, EventHandler<T> handler)
        {
            if (handler == null) return false;
            return m_EventHandlers.Remove(id, handler);
        }

        public void SetDefaultHandler(EventHandler<T> handler)
        {
            m_DefaultHandler = handler;
        }

        /// <summary>
        /// 抛出事件（线程安全），下一帧 Update 派发。
        /// 所有权约定：调用方移交 e 的所有权；e 必须由 ReferencePool.Acquire 获得，派发后框架自动 Release，调用方不得再持有/复用。
        /// </summary>
        public void Fire(object sender, T e)
        {
            if (e == null)
            {
                throw new FrameworkException("Event is invalid.");
            }

            Event eventNode = Event.Create(sender, e);
            lock (m_QueueLock)
            {
                m_PendingEvents.Enqueue(eventNode);
            }
        }

        /// <summary>
        /// 立即派发。仅可在主线程调用。
        /// 所有权约定：调用方移交 e 的所有权；e 必须由 ReferencePool.Acquire 获得，派发后框架自动 Release，调用方不得再持有/复用。
        /// </summary>
        public void FireNow(object sender, T e)
        {
            if (e == null)
            {
                throw new FrameworkException("Event is invalid.");
            }

            HandleEvent(sender, e);
        }

        private void HandleEvent(object sender, T e)
        {
            bool noHandlerException = false;
            int handlerCount = 0;
            EventHandler<T>[] handlers = null;
            bool depthEntered = false;

            GameLinkedListRange<EventHandler<T>> range = default(GameLinkedListRange<EventHandler<T>>);
            if (m_EventHandlers.TryGetValue(e.Id, out range) && range.IsValid)
            {
                // snapshot 一份 handler 列表：handler 内 Subscribe/Unsubscribe 不影响本轮派发。
                // 进入 snapshot-iterate 区前记录重入深度：handler 内同步 FireNow 重入时 s_Depth>0，
                // 会拿到独立数组，绝不会覆盖/清空本轮正在迭代的 handlers。
                handlerCount = range.Count;
                handlers = AcquireScratch(handlerCount, s_Depth);
                s_Depth++;
                depthEntered = true;
                int idx = 0;
                for (LinkedListNode<EventHandler<T>> n = range.First; n != null && n != range.Terminal; n = n.Next)
                {
                    handlers[idx++] = n.Value;
                }
            }
            else if (m_DefaultHandler != null)
            {
                try
                {
                    m_DefaultHandler(sender, e);
                }
                catch (Exception ex)
                {
                    FrameworkLog.Error("Event default handler threw on event id={0}: {1}", e.Id, ex);
                }
            }
            else if (!m_EventPoolMode.HasFlag(EventPoolMode.AllowNoHandler))
            {
                noHandlerException = true;
            }

            try
            {
                if (handlerCount > 0)
                {
                    for (int i = 0; i < handlerCount; i++)
                    {
                        EventHandler<T> h = handlers[i];
                        handlers[i] = null; // 释放引用，避免 scratch 持有
                        if (h == null) continue;
                        try
                        {
                            h(sender, e);
                        }
                        catch (Exception ex)
                        {
                            FrameworkLog.Error("Event handler threw on event id={0}: {1}", e.Id, ex);
                        }
                    }
                }
            }
            finally
            {
                if (depthEntered)
                {
                    s_Depth--;
                }
            }

            // 先把 Id 读进局部变量，再 Release(e)；否则下方异常分支会读已归还到池的对象（use-after-release）。
            int eventId = e.Id;
            ReferencePool.Release(e);

            if (noHandlerException)
            {
                throw new FrameworkException(Utility.Text.Format("Event '{0}' not allow no handler.", eventId));
            }
        }

        /// <summary>
        /// 获取 handler snapshot 缓冲。depthAtEntry==0（非重入）时复用共享 s_HandlerScratch 实现零分配快路径；
        /// depthAtEntry>0（嵌套派发）时分配一份独立数组，避免内层覆盖外层正在迭代的共享缓冲。
        /// </summary>
        private static EventHandler<T>[] AcquireScratch(int neededCount, int depthAtEntry)
        {
            if (depthAtEntry > 0)
            {
                int local = 8;
                while (local < neededCount) local *= 2;
                return new EventHandler<T>[local];
            }

            EventHandler<T>[] scratch = s_HandlerScratch;
            if (scratch == null || scratch.Length < neededCount)
            {
                int sz = 8;
                while (sz < neededCount) sz *= 2;
                scratch = new EventHandler<T>[sz];
                s_HandlerScratch = scratch;
            }
            return scratch;
        }
    }
}
