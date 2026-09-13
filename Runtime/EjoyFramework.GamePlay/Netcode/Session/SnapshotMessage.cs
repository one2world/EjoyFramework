//------------------------------------------------------------
// EjoyGame Framework — GamePlay
// Copyright (c) 2024-2026 EjoyGame. All rights reserved.
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EjoyFramework.GamePlay.Netcode.Messages;

namespace EjoyFramework.GamePlay.Netcode.Session
{
    /// <summary>
    /// 世界快照消息（服务器→客户端），每 tick 向各客户端发送一份。
    /// <para>
    /// 携带 tick 序号、服务器时间、以及「针对接收方客户端的、服务器已处理到的最后一个输入序号」
    /// （<see cref="LastProcessedInputSequence"/>，供该客户端和解本地预测），
    /// 外加所有实体的状态向量列表。
    /// </para>
    /// <para>
    /// 实体列表的线路编码：<c>[ushort 实体数]</c>，随后每个实体
    /// <c>[int entityId][ushort valueCount][float * valueCount]</c>。
    /// </para>
    /// </summary>
    public sealed class SnapshotMessage : INetMessage
    {
        /// <summary>
        /// 单份快照允许的最大实体数。反序列化时据此拒绝畸形 / 恶意的超大计数，
        /// 防止「先按声明计数预分配大列表」的放大型 DoS。
        /// </summary>
        public const int MaxEntitiesPerSnapshot = 4096;

        /// <summary>
        /// 单个实体状态向量允许的最大浮点数个数。反序列化时据此拒绝畸形 / 恶意的超大计数，
        /// 防止逐实体的预分配放大型 DoS。
        /// </summary>
        public const int MaxValuesPerEntity = 64;

        /// <summary>
        /// 该快照对应的服务器 tick 序号。
        /// </summary>
        public uint Tick { get; set; }

        /// <summary>
        /// 该 tick 对应的服务器时间（秒），作为客户端插值的时间轴。
        /// </summary>
        public double ServerTimeSec { get; set; }

        /// <summary>
        /// 服务器针对<b>接收方该客户端</b>已处理到的最后一个输入序号（ack），供其和解本地预测。
        /// </summary>
        public uint LastProcessedInputSequence { get; set; }

        /// <summary>
        /// 本快照包含的所有实体状态：(实体 id, 状态向量)。约定非 null。
        /// </summary>
        public List<EntityState> Entities { get; set; } = new List<EntityState>();

        /// <summary>
        /// 单个实体在线路上的状态条目：实体 id 与其状态浮点向量。
        /// </summary>
        public readonly struct EntityState
        {
            /// <summary>实体唯一标识。</summary>
            public readonly int EntityId;

            /// <summary>状态浮点向量（不透明，语义由游戏层约定）。</summary>
            public readonly float[] Values;

            /// <summary>
            /// 构造一个实体状态条目。
            /// </summary>
            /// <param name="entityId">实体唯一标识。</param>
            /// <param name="values">状态浮点向量；null 视为长度 0。</param>
            public EntityState(int entityId, float[] values)
            {
                EntityId = entityId;
                Values = values ?? Array.Empty<float>();
            }
        }

        /// <inheritdoc />
        public void Serialize(NetWriter writer)
        {
            writer.WriteUInt(Tick);
            writer.WriteLong(BitConverter.DoubleToInt64Bits(ServerTimeSec));
            writer.WriteUInt(LastProcessedInputSequence);

            List<EntityState> entities = Entities ?? new List<EntityState>();
            writer.WriteUShort((ushort)entities.Count);
            for (int i = 0; i < entities.Count; i++)
            {
                EntityState entity = entities[i];
                float[] values = entity.Values ?? Array.Empty<float>();

                writer.WriteInt(entity.EntityId);
                writer.WriteUShort((ushort)values.Length);
                for (int j = 0; j < values.Length; j++)
                {
                    writer.WriteFloat(values[j]);
                }
            }
        }

        /// <inheritdoc />
        public void Deserialize(NetReader reader)
        {
            Tick = reader.ReadUInt();
            ServerTimeSec = BitConverter.Int64BitsToDouble(reader.ReadLong());
            // 拒绝非有限的服务器时间（NaN / ±∞）：会污染时钟估计与插值时间轴。
            if (!double.IsFinite(ServerTimeSec))
            {
                throw new System.IO.InvalidDataException(
                    "SnapshotMessage.ServerTimeSec 非有限值（NaN/Infinity）。");
            }

            LastProcessedInputSequence = reader.ReadUInt();

            int entityCount = reader.ReadUShort();
            // 在分配前先校验实体计数，拒绝放大型 DoS。
            if (entityCount > MaxEntitiesPerSnapshot)
            {
                throw new System.IO.InvalidDataException(
                    "SnapshotMessage 实体计数超过上限：" + entityCount + " > " + MaxEntitiesPerSnapshot);
            }

            List<EntityState> entities = new List<EntityState>(entityCount);
            for (int i = 0; i < entityCount; i++)
            {
                int entityId = reader.ReadInt();
                int valueCount = reader.ReadUShort();
                // 在分配前先校验逐实体的值计数，拒绝放大型 DoS。
                if (valueCount > MaxValuesPerEntity)
                {
                    throw new System.IO.InvalidDataException(
                        "SnapshotMessage 单实体值计数超过上限：" + valueCount + " > " + MaxValuesPerEntity);
                }

                float[] values = new float[valueCount];
                for (int j = 0; j < valueCount; j++)
                {
                    values[j] = reader.ReadFloat();
                }

                entities.Add(new EntityState(entityId, values));
            }

            Entities = entities;
        }
    }
}
