using System;

namespace EjoyFramework.Core.Ecs
{
    internal interface IComponentPool
    {
        int Count { get; }
        int EntityAt(int index);
        bool Has(int entityIndex);
        bool Remove(int entityIndex);
        void Clear();
    }

    /// <summary>稀疏索引存放 dense index + 1，组件数据按类型连续存储。</summary>
    internal sealed class ComponentPool<T> : IComponentPool where T : struct
    {
        private int[] m_Sparse = Array.Empty<int>();
        private int[] m_Entities = Array.Empty<int>();
        private T[] m_Data = Array.Empty<T>();
        public int Count { get; private set; }

        public int EntityAt(int index) => m_Entities[index];
        public bool Has(int entityIndex) => entityIndex >= 0 && entityIndex < m_Sparse.Length && m_Sparse[entityIndex] != 0;
        public ref T Get(int entityIndex) => ref m_Data[m_Sparse[entityIndex] - 1];

        public void Add(int entityIndex, T value)
        {
            if (entityIndex >= m_Sparse.Length)
                Array.Resize(ref m_Sparse, Grow(m_Sparse.Length, entityIndex + 1));
            if (Count == m_Data.Length)
            {
                int capacity = Grow(m_Data.Length, Count + 1);
                Array.Resize(ref m_Data, capacity);
                Array.Resize(ref m_Entities, capacity);
            }
            m_Entities[Count] = entityIndex;
            m_Data[Count] = value;
            m_Sparse[entityIndex] = ++Count;
        }

        public bool Remove(int entityIndex)
        {
            if (!Has(entityIndex)) return false;
            int removed = m_Sparse[entityIndex] - 1;
            int last = --Count;
            if (removed != last)
            {
                int moved = m_Entities[last];
                m_Entities[removed] = moved;
                m_Data[removed] = m_Data[last];
                m_Sparse[moved] = removed + 1;
            }
            m_Sparse[entityIndex] = 0;
            m_Data[last] = default;
            m_Entities[last] = 0;
            return true;
        }

        public void Clear()
        {
            // Queries retain pool instances; world teardown must also release their capacity.
            m_Sparse = Array.Empty<int>();
            m_Entities = Array.Empty<int>();
            m_Data = Array.Empty<T>();
            Count = 0;
        }

        internal static int Grow(int capacity, int minimum)
        {
            return Math.Max(minimum, capacity <= int.MaxValue / 2 ? Math.Max(4, capacity * 2) : int.MaxValue);
        }
    }
}
