//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;

namespace EjoyFramework.GamePlay.Netcode.Sync
{
    /// <summary>
    /// 单个实体在某一服务器 tick 上的网络化状态。
    /// <para>
    /// <see cref="Values"/> 是一段不透明的浮点向量（例如 [x, y, z, yaw]），
    /// 由游戏层自行约定语义并在外部完成 Vector3/Quaternion 与数组之间的映射，
    /// 从而保持本层与具体引擎类型解耦（纯逻辑、引擎无关）。
    /// </para>
    /// </summary>
    public readonly struct EntitySnapshot
    {
        private readonly int m_EntityId;
        private readonly float[] m_Values;

        /// <summary>
        /// 构造一个实体快照。
        /// </summary>
        /// <param name="entityId">实体唯一标识。</param>
        /// <param name="values">该实体的状态浮点向量；不允许为 null（空状态请传长度为 0 的数组）。</param>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> 为 null 时抛出。</exception>
        public EntitySnapshot(int entityId, float[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            m_EntityId = entityId;
            m_Values = values;
        }

        /// <summary>
        /// 实体唯一标识。
        /// </summary>
        public int EntityId
        {
            get { return m_EntityId; }
        }

        /// <summary>
        /// 该实体的状态浮点向量（不透明，语义由游戏层约定）。
        /// </summary>
        public float[] Values
        {
            get { return m_Values; }
        }
    }
}
