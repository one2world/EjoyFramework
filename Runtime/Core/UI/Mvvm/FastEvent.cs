//------------------------------------------------------------
// EjoyGame Framework
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.Core.UI.Mvvm
{
    internal sealed class FastEvent
    {
        private Action[] m_Handlers = Array.Empty<Action>();

        public void Add(Action handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            var next = new Action[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            m_Handlers = next;
        }

        public void Remove(Action handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            int index = Array.IndexOf(old, handler);
            if (index < 0) return;
            var next = new Action[old.Length - 1];
            if (index > 0) Array.Copy(old, 0, next, 0, index);
            if (index < old.Length - 1) Array.Copy(old, index + 1, next, index, old.Length - index - 1);
            m_Handlers = next;
        }

        public void Invoke(string source)
        {
            var handlers = m_Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](); }
                catch (Exception ex) { FrameworkLog.Error("[{0}] handler threw: {1}", source, ex); }
            }
        }
    }

    internal sealed class FastEvent<T>
    {
        private Action<T>[] m_Handlers = Array.Empty<Action<T>>();

        public void Add(Action<T> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            var next = new Action<T>[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            m_Handlers = next;
        }

        public void Remove(Action<T> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            int index = Array.IndexOf(old, handler);
            if (index < 0) return;
            var next = new Action<T>[old.Length - 1];
            if (index > 0) Array.Copy(old, 0, next, 0, index);
            if (index < old.Length - 1) Array.Copy(old, index + 1, next, index, old.Length - index - 1);
            m_Handlers = next;
        }

        public void Invoke(T arg, string source)
        {
            var handlers = m_Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](arg); }
                catch (Exception ex) { FrameworkLog.Error("[{0}] handler threw: {1}", source, ex); }
            }
        }
    }

    internal sealed class FastEvent<T1, T2>
    {
        private Action<T1, T2>[] m_Handlers = Array.Empty<Action<T1, T2>>();

        public void Add(Action<T1, T2> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            var next = new Action<T1, T2>[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            m_Handlers = next;
        }

        public void Remove(Action<T1, T2> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            int index = Array.IndexOf(old, handler);
            if (index < 0) return;
            var next = new Action<T1, T2>[old.Length - 1];
            if (index > 0) Array.Copy(old, 0, next, 0, index);
            if (index < old.Length - 1) Array.Copy(old, index + 1, next, index, old.Length - index - 1);
            m_Handlers = next;
        }

        public void Invoke(T1 arg1, T2 arg2, string source)
        {
            var handlers = m_Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](arg1, arg2); }
                catch (Exception ex) { FrameworkLog.Error("[{0}] handler threw: {1}", source, ex); }
            }
        }
    }

    internal sealed class FastEvent<T1, T2, T3>
    {
        private Action<T1, T2, T3>[] m_Handlers = Array.Empty<Action<T1, T2, T3>>();

        public void Add(Action<T1, T2, T3> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            var next = new Action<T1, T2, T3>[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = handler;
            m_Handlers = next;
        }

        public void Remove(Action<T1, T2, T3> handler)
        {
            if (handler == null) return;
            var old = m_Handlers;
            int index = Array.IndexOf(old, handler);
            if (index < 0) return;
            var next = new Action<T1, T2, T3>[old.Length - 1];
            if (index > 0) Array.Copy(old, 0, next, 0, index);
            if (index < old.Length - 1) Array.Copy(old, index + 1, next, index, old.Length - index - 1);
            m_Handlers = next;
        }

        public void Invoke(T1 arg1, T2 arg2, T3 arg3, string source)
        {
            var handlers = m_Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](arg1, arg2, arg3); }
                catch (Exception ex) { FrameworkLog.Error("[{0}] handler threw: {1}", source, ex); }
            }
        }
    }
}
