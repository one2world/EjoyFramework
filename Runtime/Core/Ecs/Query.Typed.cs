using System;

namespace EjoyFramework.Core.Ecs
{
    public delegate void ComponentAction<T1>(Entity entity, ref T1 component1) where T1 : struct;
    public delegate void ComponentAction<T1, T2>(Entity entity, ref T1 component1, ref T2 component2) where T1 : struct where T2 : struct;
    public delegate void ComponentAction<T1, T2, T3>(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3) where T1 : struct where T2 : struct where T3 : struct;
    public delegate void ComponentAction<T1, T2, T3, T4>(Entity entity, ref T1 component1, ref T2 component2, ref T3 component3, ref T4 component4) where T1 : struct where T2 : struct where T3 : struct where T4 : struct;

    public sealed partial class Query
    {
        /// <summary>泛型参数自动加入必需组件条件；回调内允许原位修改值，禁止结构变更。</summary>
        public void ForEach<T1>(ComponentAction<T1> action) where T1 : struct
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var visitor = new Visitor<T1> { Action = action, Pool1 = m_World.GetPool<T1>() };
            Execute(ref visitor);
        }

        private struct Visitor<T1> : IVisitor where T1 : struct
        {
            public ComponentAction<T1> Action;
            public ComponentPool<T1> Pool1;
            public int PoolCount => 1;
            public IComponentPool PoolAt(int index)
            {
                switch (index)
                {
                    case 0: return Pool1;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            public void Invoke(Entity entity) => Action(entity, ref Pool1.Get(entity.Index));
        }

        /// <summary>泛型参数自动加入必需组件条件；回调内允许原位修改值，禁止结构变更。</summary>
        public void ForEach<T1, T2>(ComponentAction<T1, T2> action) where T1 : struct where T2 : struct
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var visitor = new Visitor<T1, T2> { Action = action, Pool1 = m_World.GetPool<T1>(), Pool2 = m_World.GetPool<T2>() };
            Execute(ref visitor);
        }

        private struct Visitor<T1, T2> : IVisitor where T1 : struct where T2 : struct
        {
            public ComponentAction<T1, T2> Action;
            public ComponentPool<T1> Pool1;
            public ComponentPool<T2> Pool2;
            public int PoolCount => 2;
            public IComponentPool PoolAt(int index)
            {
                switch (index)
                {
                    case 0: return Pool1;
                    case 1: return Pool2;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            public void Invoke(Entity entity) => Action(entity, ref Pool1.Get(entity.Index), ref Pool2.Get(entity.Index));
        }

        /// <summary>泛型参数自动加入必需组件条件；回调内允许原位修改值，禁止结构变更。</summary>
        public void ForEach<T1, T2, T3>(ComponentAction<T1, T2, T3> action) where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var visitor = new Visitor<T1, T2, T3> { Action = action, Pool1 = m_World.GetPool<T1>(), Pool2 = m_World.GetPool<T2>(), Pool3 = m_World.GetPool<T3>() };
            Execute(ref visitor);
        }

        private struct Visitor<T1, T2, T3> : IVisitor where T1 : struct where T2 : struct where T3 : struct
        {
            public ComponentAction<T1, T2, T3> Action;
            public ComponentPool<T1> Pool1;
            public ComponentPool<T2> Pool2;
            public ComponentPool<T3> Pool3;
            public int PoolCount => 3;
            public IComponentPool PoolAt(int index)
            {
                switch (index)
                {
                    case 0: return Pool1;
                    case 1: return Pool2;
                    case 2: return Pool3;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            public void Invoke(Entity entity) => Action(entity, ref Pool1.Get(entity.Index), ref Pool2.Get(entity.Index), ref Pool3.Get(entity.Index));
        }

        /// <summary>泛型参数自动加入必需组件条件；回调内允许原位修改值，禁止结构变更。</summary>
        public void ForEach<T1, T2, T3, T4>(ComponentAction<T1, T2, T3, T4> action) where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var visitor = new Visitor<T1, T2, T3, T4> { Action = action, Pool1 = m_World.GetPool<T1>(), Pool2 = m_World.GetPool<T2>(), Pool3 = m_World.GetPool<T3>(), Pool4 = m_World.GetPool<T4>() };
            Execute(ref visitor);
        }

        private struct Visitor<T1, T2, T3, T4> : IVisitor where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            public ComponentAction<T1, T2, T3, T4> Action;
            public ComponentPool<T1> Pool1;
            public ComponentPool<T2> Pool2;
            public ComponentPool<T3> Pool3;
            public ComponentPool<T4> Pool4;
            public int PoolCount => 4;
            public IComponentPool PoolAt(int index)
            {
                switch (index)
                {
                    case 0: return Pool1;
                    case 1: return Pool2;
                    case 2: return Pool3;
                    case 3: return Pool4;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            public void Invoke(Entity entity) => Action(entity, ref Pool1.Get(entity.Index), ref Pool2.Get(entity.Index), ref Pool3.Get(entity.Index), ref Pool4.Get(entity.Index));
        }

    }
}
