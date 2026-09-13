using System;
using System.Collections.Generic;
using System.Threading;

namespace EjoyFramework.Core.Ecs
{
    /// <summary>
    /// 独立、单线程的 ECS 世界。组件 ref 只能使用到下一次结构变更前。
    /// ForEach/SystemGroup 执行期间禁止直接增删；使用 CommandBuffer 延迟提交。
    /// </summary>
    public sealed class World : IDisposable
    {
        private struct Slot
        {
            public uint Version;
            public int DenseIndex;
            public int NextFree;
            public byte State; // 0 free, 1 reserved, 2 alive, 3 retired
            public CommandBuffer Owner;
        }

        private static long s_NextId;
        private Slot[] m_Slots;
        private int[] m_Alive;
        private int m_SlotCount;
        private int m_FreeHead = -1;
        private int m_IterationDepth;
        private readonly Dictionary<Type, IComponentPool> m_Pools = new Dictionary<Type, IComponentPool>();

        public long Id { get; }
        public int EntityCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public World(int initialCapacity = 64)
        {
            if (initialCapacity < 1) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            Id = Interlocked.Increment(ref s_NextId);
            if (Id <= 0) throw new InvalidOperationException("World identifiers exhausted.");
            m_Slots = new Slot[initialCapacity];
            m_Alive = new int[initialCapacity];
        }

        public Entity CreateEntity()
        {
            RequireStructuralChange();
            var entity = ReserveEntity(null);
            Activate(entity, null);
            return entity;
        }

        public bool IsAlive(Entity entity) => Matches(entity) && m_Slots[entity.Index].State == 2;

        public void DestroyEntity(Entity entity)
        {
            RequireAlive(entity);
            RequireStructuralChange();
            foreach (var pool in m_Pools.Values) pool.Remove(entity.Index);
            int denseIndex = m_Slots[entity.Index].DenseIndex;
            int moved = m_Alive[--EntityCount];
            if (denseIndex != EntityCount)
            {
                m_Alive[denseIndex] = moved;
                m_Slots[moved].DenseIndex = denseIndex;
            }
            m_Alive[EntityCount] = 0;
            ReleaseSlot(entity.Index);
        }

        /// <summary>插入或覆盖组件。覆盖已有值不是结构变更。</summary>
        public void Set<T>(Entity entity, T value) where T : struct
        {
            RequireAlive(entity);
            var pool = GetPool<T>();
            if (pool.Has(entity.Index)) pool.Get(entity.Index) = value;
            else
            {
                RequireStructuralChange();
                pool.Add(entity.Index, value);
            }
        }

        /// <summary>返回原位引用；禁止跨结构变更、World.Dispose 或线程保存此引用。</summary>
        public ref T Get<T>(Entity entity) where T : struct
        {
            RequireAlive(entity);
            if (!m_Pools.TryGetValue(typeof(T), out var raw) || !raw.Has(entity.Index))
                throw new InvalidOperationException($"{entity} has no {typeof(T).Name} component.");
            return ref ((ComponentPool<T>)raw).Get(entity.Index);
        }

        public bool Has<T>(Entity entity) where T : struct
        {
            RequireNotDisposed();
            return IsAlive(entity) && m_Pools.TryGetValue(typeof(T), out var pool) && pool.Has(entity.Index);
        }

        public bool TryGet<T>(Entity entity, out T value) where T : struct
        {
            RequireNotDisposed();
            if (IsAlive(entity) && m_Pools.TryGetValue(typeof(T), out var raw) && raw.Has(entity.Index))
            {
                value = ((ComponentPool<T>)raw).Get(entity.Index);
                return true;
            }
            value = default;
            return false;
        }

        public bool Remove<T>(Entity entity) where T : struct
        {
            RequireAlive(entity);
            if (!m_Pools.TryGetValue(typeof(T), out var pool) || !pool.Has(entity.Index)) return false;
            RequireStructuralChange();
            return pool.Remove(entity.Index);
        }

        public Query Query()
        {
            RequireNotDisposed();
            return new Query(this);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            RequireStructuralChange();
            IsDisposed = true;
            foreach (var pool in m_Pools.Values) pool.Clear();
            m_Pools.Clear();
            m_Slots = Array.Empty<Slot>();
            m_Alive = Array.Empty<int>();
            EntityCount = 0;
        }

        internal ComponentPool<T> GetPool<T>() where T : struct
        {
            RequireNotDisposed();
            if (!m_Pools.TryGetValue(typeof(T), out var pool))
            {
                pool = new ComponentPool<T>();
                m_Pools.Add(typeof(T), pool);
            }
            return (ComponentPool<T>)pool;
        }

        internal int AliveIndexAt(int denseIndex) => m_Alive[denseIndex];
        internal Entity EntityAt(int index) => new Entity(Id, index, m_Slots[index].Version);

        internal void EnterIteration()
        {
            RequireNotDisposed();
            m_IterationDepth++;
        }
        internal void ExitIteration() => m_IterationDepth--;

        internal void RequireNotDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(World));
        }

        internal void RequireStructuralChange()
        {
            RequireNotDisposed();
            if (m_IterationDepth != 0)
                throw new InvalidOperationException("Structural changes are forbidden during iteration. Use a CommandBuffer.");
        }

        internal void ValidateCommandTarget(Entity entity, CommandBuffer owner)
        {
            RequireNotDisposed();
            if (IsAlive(entity)) return;
            if (Matches(entity) && m_Slots[entity.Index].State == 1 && ReferenceEquals(m_Slots[entity.Index].Owner, owner)) return;
            throw new InvalidOperationException("Command target is stale, belongs to another world, or is reserved by another buffer.");
        }

        internal Entity ReserveEntity(CommandBuffer owner)
        {
            RequireNotDisposed();
            int index;
            if (m_FreeHead >= 0)
            {
                index = m_FreeHead;
                m_FreeHead = m_Slots[index].NextFree;
            }
            else
            {
                if (m_SlotCount == m_Slots.Length)
                    Array.Resize(ref m_Slots, ComponentPool<int>.Grow(m_Slots.Length, checked(m_SlotCount + 1)));
                index = m_SlotCount++;
                m_Slots[index].Version = 1;
            }
            m_Slots[index].State = 1;
            m_Slots[index].Owner = owner;
            return EntityAt(index);
        }

        internal void Activate(Entity entity, CommandBuffer owner)
        {
            RequireStructuralChange();
            ValidateCommandTarget(entity, owner);
            if (m_Slots[entity.Index].State != 1) throw new InvalidOperationException("Entity already activated.");
            if (EntityCount == m_Alive.Length)
                Array.Resize(ref m_Alive, ComponentPool<int>.Grow(m_Alive.Length, checked(EntityCount + 1)));
            m_Slots[entity.Index].State = 2;
            m_Slots[entity.Index].Owner = null;
            m_Slots[entity.Index].DenseIndex = EntityCount;
            m_Alive[EntityCount++] = entity.Index;
        }

        internal void CancelReservation(Entity entity, CommandBuffer owner)
        {
            if (Matches(entity) && m_Slots[entity.Index].State == 1 && ReferenceEquals(m_Slots[entity.Index].Owner, owner))
                ReleaseSlot(entity.Index);
        }

        private bool Matches(Entity entity) => !IsDisposed && entity.WorldId == Id && entity.Index >= 0
            && entity.Index < m_SlotCount && entity.Version == m_Slots[entity.Index].Version;

        private void RequireAlive(Entity entity)
        {
            RequireNotDisposed();
            if (!IsAlive(entity)) throw new InvalidOperationException("Entity is stale, reserved, or belongs to another world.");
        }

        private void ReleaseSlot(int index)
        {
            m_Slots[index].Owner = null;
            // A retired slot is never reused: generation wrap cannot resurrect an old handle.
            if (m_Slots[index].Version == uint.MaxValue)
            {
                m_Slots[index].State = 3;
                return;
            }
            m_Slots[index].Version++;
            m_Slots[index].State = 0;
            m_Slots[index].NextFree = m_FreeHead;
            m_FreeHead = index;
        }
    }
}
