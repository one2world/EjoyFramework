using System;
using System.Collections.Generic;

namespace EjoyFramework.Core.Ecs
{
    /// <summary>
    /// 按 order 升序串行执行，同 order 保持注册顺序，每个系统结束后回放命令。
    /// 持有自己的命令缓冲，不拥有 World 或注册的系统实例。不执行多线程调度。
    /// </summary>
    public sealed class SystemGroup : IDisposable
    {
        private sealed class Entry
        {
            public ISystem System;
            public int Order;
            public bool Enabled = true;
        }

        private readonly World m_World;
        private readonly CommandBuffer m_Commands;
        private readonly List<Entry> m_Systems = new List<Entry>();
        private bool m_Updating;
        private bool m_Disposed;

        public SystemGroup(World world)
        {
            m_World = world ?? throw new ArgumentNullException(nameof(world));
            m_Commands = new CommandBuffer(world, ownedByGroup: true);
        }

        public void Add(ISystem system, int order = 0)
        {
            RequireEditable();
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (Find(system) >= 0) throw new InvalidOperationException("System instance already registered.");
            int index = m_Systems.Count;
            while (index > 0 && m_Systems[index - 1].Order > order) index--;
            m_Systems.Insert(index, new Entry { System = system, Order = order });
        }

        public bool Remove(ISystem system)
        {
            RequireEditable();
            int index = Find(system);
            if (index < 0) return false;
            m_Systems.RemoveAt(index);
            return true;
        }

        public void SetEnabled(ISystem system, bool enabled)
        {
            RequireEditable();
            int index = Find(system);
            if (index < 0) throw new InvalidOperationException("System instance is not registered.");
            m_Systems[index].Enabled = enabled;
        }

        public void Update(float deltaTime)
        {
            RequireEditable();
            m_World.RequireStructuralChange();
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            m_Updating = true;
            try
            {
                for (int i = 0; i < m_Systems.Count; i++)
                {
                    if (!m_Systems[i].Enabled) continue;
                    m_World.EnterIteration();
                    try { m_Systems[i].System.Update(m_World, m_Commands, deltaTime); }
                    finally { m_World.ExitIteration(); }
                    m_Commands.Playback();
                }
            }
            catch
            {
                m_Commands.Clear();
                throw;
            }
            finally { m_Updating = false; }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            if (m_Updating) throw new InvalidOperationException("Cannot dispose a running SystemGroup.");
            m_Commands.Release();
            m_Systems.Clear();
            m_Systems.Capacity = 0;
            m_Disposed = true;
        }

        private int Find(ISystem system)
        {
            for (int i = 0; i < m_Systems.Count; i++)
                if (ReferenceEquals(m_Systems[i].System, system)) return i;
            return -1;
        }

        private void RequireEditable()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(SystemGroup));
            if (m_Updating) throw new InvalidOperationException("Cannot modify or reenter a running SystemGroup.");
            m_World.RequireNotDisposed();
        }
    }
}
