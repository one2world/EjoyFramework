//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System.Collections.Generic;

namespace EjoyFramework.GamePlay.Netcode.Sync
{
    /// <summary>
    /// 某一服务器 tick 上所有实体状态的集合，附带该 tick 对应的服务器时间。
    /// <para>
    /// 用于客户端侧的快照插值：同一帧内所有实体共享相同的 <see cref="Tick"/> 与
    /// <see cref="ServerTimeSec"/>，插值时以 <see cref="ServerTimeSec"/> 作为时间轴。
    /// </para>
    /// </summary>
    public sealed class WorldSnapshot
    {
        private readonly uint m_Tick;
        private readonly double m_ServerTimeSec;
        private readonly List<EntitySnapshot> m_Entities;
        private readonly Dictionary<int, int> m_IndexById;

        /// <summary>
        /// 构造一个世界快照。
        /// </summary>
        /// <param name="tick">服务器 tick 序号。</param>
        /// <param name="serverTimeSec">该 tick 对应的服务器时间（秒）。</param>
        public WorldSnapshot(uint tick, double serverTimeSec)
        {
            m_Tick = tick;
            m_ServerTimeSec = serverTimeSec;
            m_Entities = new List<EntitySnapshot>();
            m_IndexById = new Dictionary<int, int>();
        }

        /// <summary>
        /// 服务器 tick 序号。
        /// </summary>
        public uint Tick
        {
            get { return m_Tick; }
        }

        /// <summary>
        /// 该 tick 对应的服务器时间（秒），作为插值的时间轴。
        /// </summary>
        public double ServerTimeSec
        {
            get { return m_ServerTimeSec; }
        }

        /// <summary>
        /// 本快照包含的所有实体状态（只读视图）。
        /// </summary>
        public IReadOnlyList<EntitySnapshot> Entities
        {
            get { return m_Entities; }
        }

        /// <summary>
        /// 添加一个实体状态。
        /// <para>
        /// 若同一 <see cref="EntitySnapshot.EntityId"/> 已存在，则覆盖其状态（后写胜出），
        /// 不会产生重复条目。
        /// </para>
        /// </summary>
        /// <param name="entity">待添加的实体状态。</param>
        public void AddEntity(EntitySnapshot entity)
        {
            int existingIndex;
            if (m_IndexById.TryGetValue(entity.EntityId, out existingIndex))
            {
                m_Entities[existingIndex] = entity;
                return;
            }

            m_IndexById.Add(entity.EntityId, m_Entities.Count);
            m_Entities.Add(entity);
        }

        /// <summary>
        /// 按实体 id 查找其在本快照中的状态。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <param name="entity">命中时输出对应实体状态。</param>
        /// <returns>存在返回 true，否则 false。</returns>
        public bool TryGetEntity(int entityId, out EntitySnapshot entity)
        {
            int index;
            if (m_IndexById.TryGetValue(entityId, out index))
            {
                entity = m_Entities[index];
                return true;
            }

            entity = default;
            return false;
        }
    }
}
